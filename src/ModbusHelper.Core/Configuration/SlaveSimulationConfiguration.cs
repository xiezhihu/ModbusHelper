using ModbusHelper.Core.Enums;

namespace ModbusHelper.Core.Configuration;

/// <summary>
/// 从站模拟器配置。
/// </summary>
public sealed class SlaveSimulationConfiguration
{
    /// <summary>监听传输类型。</summary>
    public ConnectionType Type { get; set; } = ConnectionType.Tcp;

    /// <summary>从站模拟界面地址基准，不影响主站连接和轮询页面。</summary>
    public AddressBase AddressBase { get; set; } = AddressBase.ZeroBased;

    /// <summary>RTU 监听串口参数。</summary>
    public RtuConnectionConfiguration Rtu { get; set; } = new() { PortName = string.Empty };

    /// <summary>TCP 监听参数。</summary>
    public TcpSlaveConfiguration Tcp { get; set; } = new();

    /// <summary>挂载的从站单元。</summary>
    public List<SlaveUnitConfiguration> Units { get; set; } = new();
}

/// <summary>
/// TCP 从站监听参数。
/// </summary>
public sealed class TcpSlaveConfiguration
{
    /// <summary>监听地址。</summary>
    public string ListenAddress { get; set; } = "0.0.0.0";

    /// <summary>监听端口。</summary>
    public int Port { get; set; } = 502;
}

/// <summary>
/// 单个从站单元配置。
/// </summary>
public sealed class SlaveUnitConfiguration
{
    /// <summary>从站地址，范围 1 至 247。</summary>
    public byte UnitId { get; set; } = 1;

    /// <summary>保持寄存器初始值。</summary>
    public List<RegisterInitialValue> HoldingRegisters { get; set; } = new();

    /// <summary>输入寄存器初始值。</summary>
    public List<RegisterInitialValue> InputRegisters { get; set; } = new();

    /// <summary>线圈初始值。</summary>
    public List<CoilInitialValue> Coils { get; set; } = new();

    /// <summary>离散输入初始值。</summary>
    public List<CoilInitialValue> DiscreteInputs { get; set; } = new();

    /// <summary>异常注入规则。</summary>
    public List<ExceptionInjectionRule> ExceptionRules { get; set; } = new();
}

/// <summary>
/// 寄存器初始值。
/// </summary>
public sealed class RegisterInitialValue
{
    /// <summary>0 基协议地址。</summary>
    public ushort Address { get; set; }

    /// <summary>寄存器值。</summary>
    public ushort Value { get; set; }
}

/// <summary>
/// 线圈或离散输入初始值。
/// </summary>
public sealed class CoilInitialValue
{
    /// <summary>0 基协议地址。</summary>
    public ushort Address { get; set; }

    /// <summary>布尔值。</summary>
    public bool Value { get; set; }
}

/// <summary>
/// 强制异常响应规则。
/// </summary>
public sealed class ExceptionInjectionRule
{
    /// <summary>规则是否启用。</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>从站地址。</summary>
    public byte UnitId { get; set; } = 1;

    /// <summary>目标功能码。</summary>
    public ModbusFunction Function { get; set; } = ModbusFunction.ReadHoldingRegisters;

    /// <summary>目标起始地址。</summary>
    public ushort StartAddress { get; set; }

    /// <summary>命中数量，至少为 1。</summary>
    public ushort Count { get; set; } = 1;

    /// <summary>返回的 Modbus 异常码。</summary>
    public byte ExceptionCode { get; set; } = 2;
}
