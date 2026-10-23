using ModbusHelper.Core.Enums;

namespace ModbusHelper.Business.Data;

/// <summary>
/// Modbus 读取数量与解析数据类型的形状规则。
/// </summary>
public static class ModbusDataShapeRules
{
    /// <summary>
    /// 判断功能码是否为需要按数据类型解析的寄存器读取。
    /// </summary>
    /// <param name="function">功能码。</param>
    /// <returns>03 或 04 返回真。</returns>
    public static bool IsRegisterRead(ModbusFunction function)
    {
        return function is ModbusFunction.ReadHoldingRegisters
            or ModbusFunction.ReadInputRegisters;
    }

    /// <summary>
    /// 获取固定数值类型所需的寄存器数量。
    /// </summary>
    /// <param name="dataType">解析数据类型。</param>
    /// <returns>寄存器数量；可变长度字符串返回 0。</returns>
    public static int GetRequiredRegisterCount(DataValueType dataType)
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
    /// 判断功能码、数据类型和数量是否兼容。
    /// </summary>
    /// <param name="function">功能码。</param>
    /// <param name="dataType">解析数据类型。</param>
    /// <param name="count">读取数量。</param>
    /// <returns>是否兼容。</returns>
    public static bool IsCompatible(
        ModbusFunction function,
        DataValueType dataType,
        int count)
    {
        if (count < 1 || !IsRegisterRead(function))
        {
            return count >= 1;
        }

        int required = GetRequiredRegisterCount(dataType);
        return required == 0 || count == required;
    }
}
