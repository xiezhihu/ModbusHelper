using System.Text.Json.Serialization;

using ModbusHelper.Core.Enums;

namespace ModbusHelper.Core.Configuration;

/// <summary>
/// 轮询任务配置。
/// </summary>
public sealed class PollingTaskConfiguration
{
    /// <summary>任务唯一标识。</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>任务显示名称。</summary>
    public string Name { get; set; } = "新建轮询任务";

    /// <summary>是否允许启动。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>读取功能码。</summary>
    public ModbusFunction Function { get; set; } = ModbusFunction.ReadHoldingRegisters;

    /// <summary>从站地址。</summary>
    public byte UnitId { get; set; } = 1;

    /// <summary>0 基协议起始地址。</summary>
    public ushort StartAddress { get; set; }

    /// <summary>读取数量。</summary>
    public ushort Count { get; set; } = 1;

    /// <summary>轮询间隔，单位毫秒，最小为 100。</summary>
    public int IntervalMilliseconds { get; set; } = 1000;

    /// <summary>数据解析类型。</summary>
    public DataValueType DataType { get; set; } = DataValueType.UInt16;

    /// <summary>寄存器数据布局。</summary>
    public RegisterByteOrder ByteOrder { get; set; } = RegisterByteOrder.ABCD;

    /// <summary>是否加入趋势图。</summary>
    public bool ShowInTrend { get; set; }

    /// <summary>趋势曲线是否显示。</summary>
    public bool TrendVisible { get; set; } = true;

    /// <summary>趋势曲线 ARGB 颜色。</summary>
    public int TrendColorArgb { get; set; } = unchecked((int)0xFF0078D7);

    /// <summary>关联点位标识，可为空。</summary>
    public string? PointId { get; set; }

    /// <summary>运行时轮询标记，不写入 JSON。</summary>
    [JsonIgnore]
    public bool IsRunning { get; set; }
}

/// <summary>
/// 可复用点位配置。
/// </summary>
public sealed class PointConfiguration
{
    /// <summary>点位唯一标识。</summary>
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    /// <summary>点位显示名称。</summary>
    public string Name { get; set; } = "新点位";

    /// <summary>从站地址。</summary>
    public byte UnitId { get; set; } = 1;

    /// <summary>读取功能码。</summary>
    public ModbusFunction Function { get; set; } = ModbusFunction.ReadHoldingRegisters;

    /// <summary>0 基协议起始地址。</summary>
    public ushort StartAddress { get; set; }

    /// <summary>读取数量。</summary>
    public ushort Count { get; set; } = 1;

    /// <summary>数据解析类型。</summary>
    public DataValueType DataType { get; set; } = DataValueType.UInt16;

    /// <summary>寄存器数据布局。</summary>
    public RegisterByteOrder ByteOrder { get; set; } = RegisterByteOrder.ABCD;

    /// <summary>趋势曲线颜色。</summary>
    public int TrendColorArgb { get; set; } = unchecked((int)0xFF0078D7);

    /// <summary>是否默认加入趋势图。</summary>
    public bool ShowInTrend { get; set; }
}

/// <summary>
/// 数据解析配置。
/// </summary>
public sealed class DataParsingConfiguration
{
    /// <summary>当前数据类型。</summary>
    public DataValueType DataType { get; set; } = DataValueType.UInt16;

    /// <summary>当前寄存器数据布局。</summary>
    public RegisterByteOrder ByteOrder { get; set; } = RegisterByteOrder.ABCD;
}
