namespace ModbusHelper.Core.Protocol;

/// <summary>
/// Modbus 协议参数上限。
/// </summary>
public static class ProtocolLimits
{
    /// <summary>合法从站地址下限。</summary>
    public const byte MinimumUnitId = 1;

    /// <summary>合法从站地址上限。</summary>
    public const byte MaximumUnitId = 247;

    /// <summary>广播地址。</summary>
    public const byte BroadcastUnitId = 0;

    /// <summary>单次读取寄存器数量上限。</summary>
    public const ushort MaximumReadRegisters = 125;

    /// <summary>单次读取线圈数量上限。</summary>
    public const ushort MaximumReadBits = 2000;

    /// <summary>单次写入寄存器数量上限。</summary>
    public const ushort MaximumWriteRegisters = 123;

    /// <summary>单次写入线圈数量上限。</summary>
    public const ushort MaximumWriteBits = 1968;

    /// <summary>最小轮询间隔，单位毫秒。</summary>
    public const int MinimumPollingIntervalMilliseconds = 100;

    /// <summary>最大协议地址。</summary>
    public const ushort MaximumAddress = ushort.MaxValue;
}
