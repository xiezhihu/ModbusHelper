namespace ModbusHelper.Communication.Capturing;

/// <summary>
/// NModbus 传输层诊断消息。
/// </summary>
/// <param name="Timestamp">时间戳。</param>
/// <param name="Level">NModbus 日志级别。</param>
/// <param name="Message">原始诊断消息。</param>
public sealed record TransportLogMessage(
    DateTimeOffset Timestamp,
    TransportLogLevel Level,
    string Message);
