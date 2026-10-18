using System.Globalization;

using ModbusHelper.Core.Enums;
using ModbusHelper.Core.Models;

using NModbus;
using NModbus.Logging;

namespace ModbusHelper.Communication.Capturing;

/// <summary>
/// 从 NModbus 传输日志中提取完整 TX/RX 帧。NModbus 在传输层完成组帧和收帧后才写该日志，
/// 因此主站和从站都能获得准确的功能码级报文边界，无需业务层自行解析 RTU 字节流。
/// </summary>
public sealed class FrameCaptureLogger : IModbusLogger
{
    private static readonly char[] PayloadSeparators = { ' ', ',' };
    private bool _usesCrc;

    /// <summary>
    /// 初始化帧采集日志器。
    /// </summary>
    /// <param name="role">当前传输角色。</param>
    /// <param name="usesCrc">传输帧是否包含 RTU CRC。</param>
    public FrameCaptureLogger(ModbusRole role, bool usesCrc)
    {
        Role = role;
        _usesCrc = usesCrc;
    }

    /// <summary>当前日志器所属角色。</summary>
    public ModbusRole Role { get; }

    /// <summary>
    /// 在监听启动前设置当前传输是否使用 RTU CRC。停止状态下不会与报文解析并发。
    /// </summary>
    /// <param name="usesCrc">RTU 传输时为真。</param>
    public void SetUsesCrc(bool usesCrc)
    {
        _usesCrc = usesCrc;
    }

    /// <summary>采集到完整报文时触发。</summary>
    public event Action<RawFrameRecord>? FrameCaptured;

    /// <summary>传输层警告或错误消息。</summary>
    public event Action<TransportLogMessage>? DiagnosticMessage;

    /// <summary>
    /// 允许 NModbus 输出全部诊断级别。
    /// </summary>
    /// <param name="level">日志级别。</param>
    /// <returns>始终返回真。</returns>
    public bool ShouldLog(LoggingLevel level)
    {
        return true;
    }

    /// <summary>
    /// 接收 NModbus 日志并提取帧。
    /// </summary>
    /// <param name="level">日志级别。</param>
    /// <param name="message">日志文本。</param>
    public void Log(LoggingLevel level, string message)
    {
        if (TryParseFrame(message, out DataDirection direction, out bool validPrefix, out byte[] frame))
        {
            string meaning = BuildMeaning(direction, frame, validPrefix);
            FrameCaptured?.Invoke(new RawFrameRecord(
                DateTimeOffset.Now,
                Role,
                direction,
                string.Join(" ", frame.Select(value => value.ToString("X2", CultureInfo.InvariantCulture))),
                meaning,
                _usesCrc ? validPrefix : null));
            return;
        }

        if (level >= LoggingLevel.Warning)
        {
            DiagnosticMessage?.Invoke(new TransportLogMessage(
                DateTimeOffset.Now,
                MapLevel(level),
                message));
        }
    }

    private static bool TryParseFrame(
        string message,
        out DataDirection direction,
        out bool validPrefix,
        out byte[] frame)
    {
        direction = default;
        validPrefix = false;
        frame = Array.Empty<byte>();

        string payload;
        bool decimalPayload = false;
        if (message.StartsWith("TX:", StringComparison.Ordinal))
        {
            direction = DataDirection.Tx;
            validPrefix = true;
            payload = message[3..].Trim();
        }
        else if (message.StartsWith("tx:", StringComparison.Ordinal))
        {
            direction = DataDirection.Tx;
            payload = message[3..].Trim();
        }
        else if (message.StartsWith("RX:", StringComparison.Ordinal))
        {
            direction = DataDirection.Rx;
            validPrefix = true;
            payload = message[3..].Trim();
        }
        else if (message.StartsWith("rx:", StringComparison.Ordinal))
        {
            direction = DataDirection.Rx;
            payload = message[3..].Trim();
        }
        else if (TryExtractTcpSlavePayload(
                     message,
                     "RX from Master at ",
                     out payload))
        {
            direction = DataDirection.Rx;
            validPrefix = true;
            decimalPayload = true;
        }
        else if (TryExtractTcpSlavePayload(
                     message,
                     "TX to Master at ",
                     out payload))
        {
            direction = DataDirection.Tx;
            validPrefix = true;
            decimalPayload = true;
        }
        else
        {
            return false;
        }

        if (payload.Length == 0)
        {
            return false;
        }

        List<byte> bytes = new();
        foreach (string token in payload.Split(
                     PayloadSeparators,
                     StringSplitOptions.RemoveEmptyEntries))
        {
            NumberStyles style = decimalPayload ? NumberStyles.Integer : NumberStyles.HexNumber;
            if (!byte.TryParse(token, style, CultureInfo.InvariantCulture, out byte value))
            {
                return false;
            }

            bytes.Add(value);
        }

        frame = bytes.ToArray();
        return frame.Length >= 2;
    }

    private static bool TryExtractTcpSlavePayload(
        string message,
        string prefix,
        out string payload)
    {
        payload = string.Empty;
        if (!message.StartsWith(prefix, StringComparison.Ordinal))
        {
            return false;
        }

        int separator = message.LastIndexOf(": ", StringComparison.Ordinal);
        if (separator < 0)
        {
            return false;
        }

        payload = message[(separator + 2)..].Trim();
        return true;
    }

    private string BuildMeaning(DataDirection direction, byte[] frame, bool validPrefix)
    {
        int functionIndex = _usesCrc ? 1 : 7;
        if (frame.Length <= functionIndex)
        {
            return $"{RoleText} {DirectionText(direction)} 短帧";
        }

        byte rawFunction = frame[functionIndex];
        bool isException = (rawFunction & 0x80) != 0;
        byte functionCode = (byte)(rawFunction & 0x7F);
        string functionName = GetFunctionName(functionCode);
        string exceptionText = isException ? "异常响应" : string.Empty;
        string checksumText = _usesCrc && !validPrefix ? "，CRC 错误" : string.Empty;
        return $"{RoleText} {DirectionText(direction)} {exceptionText}{functionName}{checksumText}";
    }

    private static string GetFunctionName(byte functionCode)
    {
        return functionCode switch
        {
            0x01 => "01 线圈",
            0x02 => "02 离散输入",
            0x03 => "03 保持寄存器",
            0x04 => "04 输入寄存器",
            0x05 => "05 写单线圈",
            0x06 => "06 写单寄存器",
            0x0F => "0F 写多线圈",
            0x10 => "10 写多寄存器",
            _ => $"功能码 0x{functionCode:X2}"
        };
    }

    private string RoleText => Role == ModbusRole.Master ? "主站" : "从站";

    private static string DirectionText(DataDirection direction)
    {
        return direction == DataDirection.Tx ? "发送" : "接收";
    }

    private static TransportLogLevel MapLevel(LoggingLevel level)
    {
        return level switch
        {
            LoggingLevel.Trace => TransportLogLevel.Trace,
            LoggingLevel.Debug => TransportLogLevel.Debug,
            LoggingLevel.Information => TransportLogLevel.Information,
            LoggingLevel.Warning => TransportLogLevel.Warning,
            LoggingLevel.Error => TransportLogLevel.Error,
            LoggingLevel.Critical => TransportLogLevel.Critical,
            _ => TransportLogLevel.Debug
        };
    }
}
