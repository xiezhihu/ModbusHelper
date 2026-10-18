using ModbusHelper.Core.Enums;
using ModbusHelper.Core.Protocol;

namespace ModbusHelper.Communication.Validation;

/// <summary>
/// Modbus 主站请求参数校验。
/// </summary>
public static class ModbusRequestValidator
{
    /// <summary>
    /// 校验读操作从站地址。
    /// </summary>
    /// <param name="unitId">从站地址。</param>
    /// <returns>校验结果。</returns>
    public static ValidationResult ValidateReadUnitId(byte unitId)
    {
        ValidationResult result = new();

        if (unitId is < ProtocolLimits.MinimumUnitId or > ProtocolLimits.MaximumUnitId)
        {
            result.AddError("读操作仅允许从站地址 1 至 247。");
        }

        return result;
    }

    /// <summary>
    /// 校验写操作从站地址。
    /// </summary>
    /// <param name="unitId">从站地址。</param>
    /// <param name="connectionType">连接类型。</param>
    /// <returns>校验结果。</returns>
    public static ValidationResult ValidateWriteUnitId(byte unitId, ConnectionType connectionType)
    {
        ValidationResult result = new();

        if (unitId == ProtocolLimits.BroadcastUnitId)
        {
            result.AddWarning("地址 0 为广播写，从站不会返回响应。");
            return result;
        }

        if (unitId is >= ProtocolLimits.MinimumUnitId and <= ProtocolLimits.MaximumUnitId)
        {
            return result;
        }

        if (unitId is >= 248 and <= 255)
        {
            string mode = connectionType == ConnectionType.Rtu ? "RTU" : "TCP";
            result.AddWarning(
                $"{mode} 模式下从站地址 {unitId} 不属于标准单播范围，建议禁用；仅在确认网关或专用设备使用该地址时继续。");
            return result;
        }

        result.AddError("写操作仅允许从站地址 0 或 1 至 247。");
        return result;
    }

    /// <summary>
    /// 校验单次读取数量。
    /// </summary>
    /// <param name="function">读取功能码。</param>
    /// <param name="count">读取数量。</param>
    /// <returns>校验结果。</returns>
    public static ValidationResult ValidateReadCount(ModbusFunction function, int count)
    {
        ValidationResult result = new();
        if (function is not (ModbusFunction.ReadCoils
            or ModbusFunction.ReadDiscreteInputs
            or ModbusFunction.ReadHoldingRegisters
            or ModbusFunction.ReadInputRegisters))
        {
            result.AddError("指定功能码不是读取功能码。");
            return result;
        }

        int maximum = function.UsesRegisters()
            ? ProtocolLimits.MaximumReadRegisters
            : ProtocolLimits.MaximumReadBits;

        if (count is < 1 || count > maximum)
        {
            result.AddError($"读取数量必须为 1 至 {maximum}。");
        }

        return result;
    }

    /// <summary>
    /// 校验写入数量。
    /// </summary>
    /// <param name="function">写入功能码。</param>
    /// <param name="count">写入数量。</param>
    /// <returns>校验结果。</returns>
    public static ValidationResult ValidateWriteCount(ModbusFunction function, int count)
    {
        ValidationResult result = new();

        if (function == ModbusFunction.WriteSingleCoil || function == ModbusFunction.WriteSingleRegister)
        {
            if (count != 1)
            {
                result.AddError("单写功能码的写入数量必须为 1。");
            }

            return result;
        }

        int maximum;
        if (function == ModbusFunction.WriteMultipleCoils)
        {
            maximum = ProtocolLimits.MaximumWriteBits;
        }
        else if (function == ModbusFunction.WriteMultipleRegisters)
        {
            maximum = ProtocolLimits.MaximumWriteRegisters;
        }
        else
        {
            result.AddError("指定功能码不是写入功能码。");
            return result;
        }

        if (count is < 1 || count > maximum)
        {
            result.AddError($"写入数量必须为 1 至 {maximum}。");
        }

        return result;
    }

    /// <summary>
    /// 校验起始地址与数量范围。
    /// </summary>
    /// <param name="startAddress">0 基起始地址。</param>
    /// <param name="count">数量。</param>
    /// <returns>校验结果。</returns>
    public static ValidationResult ValidateAddressRange(ushort startAddress, int count)
    {
        ValidationResult result = new();
        if (!ModbusAddressConverter.IsRangeValid(startAddress, count))
        {
            result.AddError("起始地址与数量组合超出协议地址范围 0 至 65535。");
        }

        return result;
    }

    /// <summary>
    /// 校验轮询间隔。
    /// </summary>
    /// <param name="intervalMilliseconds">轮询间隔，单位毫秒。</param>
    /// <returns>校验结果。</returns>
    public static ValidationResult ValidatePollingInterval(int intervalMilliseconds)
    {
        ValidationResult result = new();
        if (intervalMilliseconds < ProtocolLimits.MinimumPollingIntervalMilliseconds)
        {
            result.AddError("轮询间隔不能小于 100 毫秒。");
        }

        return result;
    }

    /// <summary>
    /// 校验从站模拟器单元地址。
    /// </summary>
    /// <param name="unitId">从站地址。</param>
    /// <returns>校验结果。</returns>
    public static ValidationResult ValidateSlaveUnitId(byte unitId)
    {
        ValidationResult result = new();
        if (unitId is < ProtocolLimits.MinimumUnitId or > ProtocolLimits.MaximumUnitId)
        {
            result.AddError("从站模拟器地址必须为 1 至 247。");
        }

        return result;
    }
}
