using System.Text.Json.Serialization;

using ModbusHelper.Core.Enums;

namespace ModbusHelper.Core.Configuration;

/// <summary>
/// 连接配置。该类型为纯 POCO，可直接序列化为 JSON。
/// </summary>
public sealed class ConnectionConfiguration
{
    /// <summary>连接显示名称。</summary>
    public string Name { get; set; } = "默认连接";

    /// <summary>连接类型。</summary>
    public ConnectionType Type { get; set; } = ConnectionType.Tcp;

    /// <summary>RTU 串口参数。</summary>
    public RtuConnectionConfiguration Rtu { get; set; } = new();

    /// <summary>TCP 网络参数。</summary>
    public TcpConnectionConfiguration Tcp { get; set; } = new();

    /// <summary>传输层超时，单位毫秒。对应串口读写超时或 Socket 收发超时。</summary>
    public int TransportTimeoutMilliseconds { get; set; } = 1000;

    /// <summary>应用层应答超时，单位毫秒，独立于传输层超时。</summary>
    public int ApplicationResponseTimeoutMilliseconds { get; set; } = 1500;

    /// <summary>NModbus 传输层重试次数。</summary>
    public int RetryCount { get; set; } = 1;

    /// <summary>重试等待时间，单位毫秒。</summary>
    public int RetryDelayMilliseconds { get; set; } = 100;

    /// <summary>运行时连接状态，不写入 JSON。</summary>
    [JsonIgnore]
    public ConnectionState RuntimeState { get; set; } = ConnectionState.Disconnected;

    /// <summary>运行时连接标记，不写入 JSON。</summary>
    [JsonIgnore]
    public bool IsConnected { get; set; }
}

/// <summary>
/// RTU 串口连接参数。
/// </summary>
public sealed class RtuConnectionConfiguration
{
    /// <summary>串口名称，例如 COM3。</summary>
    public string PortName { get; set; } = string.Empty;

    /// <summary>波特率。</summary>
    public int BaudRate { get; set; } = 9600;

    /// <summary>数据位，通常为 8。</summary>
    public int DataBits { get; set; } = 8;

    /// <summary>校验方式。</summary>
    public SerialParity Parity { get; set; } = SerialParity.None;

    /// <summary>停止位。</summary>
    public SerialStopBits StopBits { get; set; } = SerialStopBits.One;
}

/// <summary>
/// TCP 连接参数。
/// </summary>
public sealed class TcpConnectionConfiguration
{
    /// <summary>目标 IP 地址或可解析的主机名。</summary>
    public string Host { get; set; } = "127.0.0.1";

    /// <summary>目标端口。</summary>
    public int Port { get; set; } = 502;

    /// <summary>建立 TCP 连接的超时时间，单位毫秒。</summary>
    public int ConnectTimeoutMilliseconds { get; set; } = 3000;
}
