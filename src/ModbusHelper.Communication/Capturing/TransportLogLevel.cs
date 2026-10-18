namespace ModbusHelper.Communication.Capturing;

/// <summary>
/// UI 和业务层可消费的中性传输日志级别，避免上层直接依赖 NModbus 类型。
/// </summary>
public enum TransportLogLevel
{
    /// <summary>跟踪。</summary>
    Trace,

    /// <summary>调试。</summary>
    Debug,

    /// <summary>信息。</summary>
    Information,

    /// <summary>警告。</summary>
    Warning,

    /// <summary>错误。</summary>
    Error,

    /// <summary>严重错误。</summary>
    Critical
}
