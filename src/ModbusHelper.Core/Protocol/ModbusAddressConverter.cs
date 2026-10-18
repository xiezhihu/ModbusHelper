using ModbusHelper.Core.Enums;

namespace ModbusHelper.Core.Protocol;

/// <summary>
/// 地址基准换算。协议与 NModbus 始终使用 0 基地址。
/// </summary>
public static class ModbusAddressConverter
{
    private const int HoldingRegisterPrefix = 40001;
    private const int InputRegisterPrefix = 30001;
    private const int DiscreteInputPrefix = 10001;

    /// <summary>
    /// 将用户输入地址转换为 0 基协议地址。
    /// </summary>
    /// <param name="inputAddress">用户输入地址。</param>
    /// <param name="addressBase">地址基准。</param>
    /// <param name="function">功能码，用于识别 40001、30001、10001 等设备手册格式。</param>
    /// <returns>0 基协议地址。</returns>
    /// <exception cref="ArgumentOutOfRangeException">地址为负数或超出协议范围。</exception>
    public static ushort ToProtocolAddress(int inputAddress, AddressBase addressBase, ModbusFunction function)
    {
        if (inputAddress < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(inputAddress), "地址不能为负数。");
        }

        if (addressBase == AddressBase.ZeroBased)
        {
            if (inputAddress > ushort.MaxValue)
            {
                throw new ArgumentOutOfRangeException(nameof(inputAddress), "地址超出 0 至 65535 范围。");
            }

            return checked((ushort)inputAddress);
        }

        int protocolAddress = function switch
        {
            ModbusFunction.ReadHoldingRegisters or
            ModbusFunction.WriteSingleRegister or
            ModbusFunction.WriteMultipleRegisters => inputAddress >= HoldingRegisterPrefix
                ? inputAddress - HoldingRegisterPrefix
                : inputAddress - 1,
            ModbusFunction.ReadInputRegisters => inputAddress >= InputRegisterPrefix
                ? inputAddress - InputRegisterPrefix
                : inputAddress - 1,
            ModbusFunction.ReadDiscreteInputs => inputAddress >= DiscreteInputPrefix
                ? inputAddress - DiscreteInputPrefix
                : inputAddress - 1,
            _ => inputAddress - 1
        };

        if (protocolAddress is < 0 or > ushort.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(inputAddress), "地址换算后超出 0 至 65535 范围。");
        }

        return (ushort)protocolAddress;
    }

    /// <summary>
    /// 将 0 基协议地址转换为界面显示格式。
    /// </summary>
    /// <param name="protocolAddress">0 基协议地址。</param>
    /// <param name="addressBase">地址基准。</param>
    /// <param name="function">功能码。</param>
    /// <returns>界面显示地址。</returns>
    public static int ToDisplayAddress(ushort protocolAddress, AddressBase addressBase, ModbusFunction function)
    {
        if (addressBase == AddressBase.ZeroBased)
        {
            return protocolAddress;
        }

        return function switch
        {
            ModbusFunction.ReadHoldingRegisters or
            ModbusFunction.WriteSingleRegister or
            ModbusFunction.WriteMultipleRegisters => HoldingRegisterPrefix + protocolAddress,
            ModbusFunction.ReadInputRegisters => InputRegisterPrefix + protocolAddress,
            ModbusFunction.ReadDiscreteInputs => DiscreteInputPrefix + protocolAddress,
            _ => protocolAddress + 1
        };
    }

    /// <summary>
    /// 校验起始地址与数量是否跨越 16 位地址空间。
    /// </summary>
    /// <param name="startAddress">0 基起始地址。</param>
    /// <param name="count">数量。</param>
    /// <returns>地址范围合法时返回真。</returns>
    public static bool IsRangeValid(ushort startAddress, int count)
    {
        return count >= 1 && startAddress + count - 1 <= ushort.MaxValue;
    }
}
