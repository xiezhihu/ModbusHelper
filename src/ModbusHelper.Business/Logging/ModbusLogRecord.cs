using ModbusHelper.Core.Enums;

namespace ModbusHelper.Business.Logging;

/// <summary>
/// 一条普通日志记录。该类型不依赖 UI 控件，可直接序列化。
/// </summary>
public sealed class ModbusLogRecord
{
    /// <summary>本机时间戳。</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;

    /// <summary>日志级别。</summary>
    public ModbusLogLevel Level { get; init; } = ModbusLogLevel.Info;

    /// <summary>报文角色。</summary>
    public ModbusRole? Role { get; init; }

    /// <summary>报文方向。</summary>
    public DataDirection? Direction { get; init; }

    /// <summary>原始十六进制报文。</summary>
    public string Hex { get; init; } = string.Empty;

    /// <summary>解析后的中文含义。</summary>
    public string Meaning { get; init; } = string.Empty;

    /// <summary>异常提示或排查建议。</summary>
    public string? ErrorHint { get; init; }
}
