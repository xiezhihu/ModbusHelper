using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using ModbusHelper.Core.Enums;

namespace ModbusHelper.Business.Data;

/// <summary>
/// 16 位寄存器与常用数据类型之间的双向转换。
/// </summary>
public static class RegisterCodec
{
    /// <summary>
    /// 获取固定长度数据类型所需的寄存器数量。
    /// </summary>
    /// <param name="dataType">数据类型。</param>
    /// <returns>寄存器数量；字符串返回 0 表示长度可变。</returns>
    public static int GetFixedRegisterCount(DataValueType dataType)
    {
        return dataType switch
        {
            DataValueType.Int16 or DataValueType.UInt16 => 1,
            DataValueType.Int32 or DataValueType.UInt32 or DataValueType.Float => 2,
            DataValueType.Double => 4,
            DataValueType.Ascii or DataValueType.Utf8 => 0,
            _ => throw new ArgumentOutOfRangeException(nameof(dataType))
        };
    }

    /// <summary>
    /// 将寄存器解码为指定类型。
    /// </summary>
    /// <param name="registers">按 Modbus 地址递增顺序排列的寄存器。</param>
    /// <param name="dataType">目标数据类型。</param>
    /// <param name="byteOrder">字序和寄存器内字节序布局。</param>
    /// <returns>解析后的普通对象，数值为 CLR 数值类型，字符串为 string。</returns>
    /// <exception cref="ArgumentException">寄存器数量不足。</exception>
    public static object Decode(
        ushort[] registers,
        DataValueType dataType,
        RegisterByteOrder byteOrder)
    {
        ArgumentNullException.ThrowIfNull(registers);
        int required = GetFixedRegisterCount(dataType);
        if (dataType is DataValueType.Ascii or DataValueType.Utf8)
        {
            required = Math.Max(registers.Length, 1);
        }

        if (registers.Length < required)
        {
            throw new ArgumentException(
                $"数据类型 {dataType} 至少需要 {required} 个寄存器，实际只有 {registers.Length} 个。",
                nameof(registers));
        }

        byte[] bytes = ToCanonicalBytes(registers, byteOrder, required);
        return dataType switch
        {
            DataValueType.Int16 => BinaryPrimitives.ReadInt16BigEndian(bytes),
            DataValueType.UInt16 => BinaryPrimitives.ReadUInt16BigEndian(bytes),
            DataValueType.Int32 => BinaryPrimitives.ReadInt32BigEndian(bytes),
            DataValueType.UInt32 => BinaryPrimitives.ReadUInt32BigEndian(bytes),
            DataValueType.Float => BitConverter.Int32BitsToSingle(
                BinaryPrimitives.ReadInt32BigEndian(bytes)),
            DataValueType.Double => BitConverter.Int64BitsToDouble(
                BinaryPrimitives.ReadInt64BigEndian(bytes)),
            DataValueType.Ascii => Encoding.ASCII.GetString(bytes).TrimEnd('\0'),
            DataValueType.Utf8 => Encoding.UTF8.GetString(bytes).TrimEnd('\0'),
            _ => throw new ArgumentOutOfRangeException(nameof(dataType))
        };
    }

    /// <summary>
    /// 将普通对象编码为寄存器数组。
    /// </summary>
    /// <param name="value">待编码值。</param>
    /// <param name="dataType">数据类型。</param>
    /// <param name="byteOrder">字序和寄存器内字节序布局。</param>
    /// <returns>按 Modbus 地址递增顺序排列的寄存器。</returns>
    /// <exception cref="ArgumentException">输入值无法转换为目标类型。</exception>
    public static ushort[] Encode(
        object value,
        DataValueType dataType,
        RegisterByteOrder byteOrder)
    {
        ArgumentNullException.ThrowIfNull(value);

        byte[] canonicalBytes = dataType switch
        {
            DataValueType.Int16 => EncodeInt16(Convert.ToInt16(value, CultureInfo.InvariantCulture)),
            DataValueType.UInt16 => EncodeUInt16(Convert.ToUInt16(value, CultureInfo.InvariantCulture)),
            DataValueType.Int32 => EncodeInt32(Convert.ToInt32(value, CultureInfo.InvariantCulture)),
            DataValueType.UInt32 => EncodeUInt32(Convert.ToUInt32(value, CultureInfo.InvariantCulture)),
            DataValueType.Float => EncodeSingle(Convert.ToSingle(value, CultureInfo.InvariantCulture)),
            DataValueType.Double => EncodeDouble(Convert.ToDouble(value, CultureInfo.InvariantCulture)),
            DataValueType.Ascii => Encoding.ASCII.GetBytes(Convert.ToString(
                value,
                CultureInfo.InvariantCulture) ?? string.Empty),
            DataValueType.Utf8 => Encoding.UTF8.GetBytes(Convert.ToString(
                value,
                CultureInfo.InvariantCulture) ?? string.Empty),
            _ => throw new ArgumentOutOfRangeException(nameof(dataType))
        };

        return FromCanonicalBytes(canonicalBytes, byteOrder);
    }

    /// <summary>
    /// 格式化解析结果用于界面显示。
    /// </summary>
    /// <param name="value">解析后的普通对象。</param>
    /// <returns>稳定、可读的显示文本。</returns>
    public static string Format(object value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value switch
        {
            float single => single.ToString("G9", CultureInfo.InvariantCulture),
            double number => number.ToString("G17", CultureInfo.InvariantCulture),
            IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
            _ => value.ToString() ?? string.Empty
        };
    }

    private static byte[] ToCanonicalBytes(
        ushort[] registers,
        RegisterByteOrder byteOrder,
        int required)
    {
        ushort[] ordered = registers.Take(required).ToArray();
        if (byteOrder is RegisterByteOrder.CDAB or RegisterByteOrder.DCBA)
        {
            Array.Reverse(ordered);
        }

        bool swapBytesInRegister = byteOrder is RegisterByteOrder.BADC or RegisterByteOrder.DCBA;
        byte[] bytes = new byte[required * 2];
        for (int index = 0; index < required; index++)
        {
            ushort register = ordered[index];
            byte high = (byte)(register >> 8);
            byte low = (byte)register;
            bytes[index * 2] = swapBytesInRegister ? low : high;
            bytes[index * 2 + 1] = swapBytesInRegister ? high : low;
        }

        return bytes;
    }

    private static ushort[] FromCanonicalBytes(byte[] bytes, RegisterByteOrder byteOrder)
    {
        int registerCount = (bytes.Length + 1) / 2;
        ushort[] registers = new ushort[registerCount];
        for (int index = 0; index < registerCount; index++)
        {
            byte first = bytes[index * 2];
            byte second = index * 2 + 1 < bytes.Length ? bytes[index * 2 + 1] : (byte)0;
            bool swapBytesInRegister =
                byteOrder is RegisterByteOrder.BADC or RegisterByteOrder.DCBA;
            byte high = swapBytesInRegister ? second : first;
            byte low = swapBytesInRegister ? first : second;
            registers[index] = (ushort)((high << 8) | low);
        }

        if (byteOrder is RegisterByteOrder.CDAB or RegisterByteOrder.DCBA)
        {
            Array.Reverse(registers);
        }

        return registers;
    }

    private static byte[] EncodeInt16(short value)
    {
        byte[] bytes = new byte[2];
        BinaryPrimitives.WriteInt16BigEndian(bytes, value);
        return bytes;
    }

    private static byte[] EncodeUInt16(ushort value)
    {
        byte[] bytes = new byte[2];
        BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
        return bytes;
    }

    private static byte[] EncodeInt32(int value)
    {
        byte[] bytes = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, value);
        return bytes;
    }

    private static byte[] EncodeUInt32(uint value)
    {
        byte[] bytes = new byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        return bytes;
    }

    private static byte[] EncodeSingle(float value)
    {
        byte[] bytes = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, BitConverter.SingleToInt32Bits(value));
        return bytes;
    }

    private static byte[] EncodeDouble(double value)
    {
        byte[] bytes = new byte[8];
        BinaryPrimitives.WriteInt64BigEndian(bytes, BitConverter.DoubleToInt64Bits(value));
        return bytes;
    }
}
