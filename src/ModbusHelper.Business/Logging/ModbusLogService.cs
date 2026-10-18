using System.Globalization;
using System.Text;

using ModbusHelper.Core.Enums;
using ModbusHelper.Core.Exceptions;
using ModbusHelper.Core.Models;

namespace ModbusHelper.Business.Logging;

/// <summary>
/// 线程安全的报文与异常日志服务，支持暂停、过滤、搜索以及 RFC 4180 CSV 导出。
/// </summary>
public sealed class ModbusLogService
{
    private readonly object _syncRoot = new();
    private readonly List<ModbusLogRecord> _records;
    private readonly int _capacity;
    private bool _paused;
    private long _revision;

    /// <summary>
    /// 初始化日志服务。
    /// </summary>
    /// <param name="capacity">最大内存记录数，超出后删除最旧记录。</param>
    public ModbusLogService(int capacity = 100_000)
    {
        if (capacity < 100)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "日志容量不能小于 100。");
        }

        _capacity = capacity;
        _records = new List<ModbusLogRecord>(Math.Min(capacity, 10_000));
    }

    /// <summary>当前是否暂停采集。</summary>
    public bool IsPaused
    {
        get
        {
            lock (_syncRoot)
            {
                return _paused;
            }
        }
    }

    /// <summary>日志内容修订号，用于 UI 判断是否需要刷新。</summary>
    public long Revision => Interlocked.Read(ref _revision);

    /// <summary>
    /// 设置暂停状态。暂停期间的新记录被丢弃，已有记录保留。
    /// </summary>
    /// <param name="paused">是否暂停。</param>
    public void SetPaused(bool paused)
    {
        lock (_syncRoot)
        {
            _paused = paused;
            Interlocked.Increment(ref _revision);
        }
    }

    /// <summary>
    /// 记录完整原始帧。
    /// </summary>
    /// <param name="frame">原始帧。</param>
    public void AddFrame(RawFrameRecord frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ModbusLogLevel level = frame.ChecksumStatus == false
            ? ModbusLogLevel.Warn
            : ModbusLogLevel.Debug;
        Add(new ModbusLogRecord
        {
            Timestamp = frame.Timestamp,
            Level = level,
            Role = frame.Role,
            Direction = frame.Direction,
            Hex = frame.Hex,
            Meaning = frame.Meaning,
            ErrorHint = frame.ChecksumStatus == false ? "CRC 校验失败，检查波特率、校验位、停止位、干扰和终端电阻。" : null
        });
    }

    /// <summary>
    /// 记录通信异常。
    /// </summary>
    /// <param name="exception">统一通信异常。</param>
    public void AddCommunicationError(ModbusCommunicationException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        ModbusLogLevel level = exception.ErrorKind is CommunicationErrorKind.Cancelled
            ? ModbusLogLevel.Info
            : exception.ErrorKind is CommunicationErrorKind.Timeout
                or CommunicationErrorKind.SlaveException
                or CommunicationErrorKind.Validation
                ? ModbusLogLevel.Warn
                : ModbusLogLevel.Error;
        Add(new ModbusLogRecord
        {
            Level = level,
            Role = ModbusRole.Master,
            Meaning = exception.GetDisplayMessage(),
            Hex = exception.ResponseHex ?? exception.RequestHex ?? string.Empty,
            ErrorHint = exception.Hint
        });
    }

    /// <summary>
    /// 记录普通诊断消息。
    /// </summary>
    /// <param name="level">日志级别。</param>
    /// <param name="message">中文消息。</param>
    /// <param name="role">可选角色。</param>
    public void AddMessage(ModbusLogLevel level, string message, ModbusRole? role = null)
    {
        Add(new ModbusLogRecord
        {
            Level = level,
            Role = role,
            Meaning = message
        });
    }

    /// <summary>
    /// 获取按筛选条件过滤的日志快照。
    /// </summary>
    /// <param name="minimumLevel">最低日志级别。</param>
    /// <param name="keyword">可选关键字。</param>
    /// <param name="limit">最大返回数量。</param>
    /// <param name="newestFirst">是否最新记录在前。</param>
    /// <returns>日志快照。</returns>
    public IReadOnlyList<ModbusLogRecord> GetSnapshot(
        ModbusLogLevel minimumLevel = ModbusLogLevel.Debug,
        string? keyword = null,
        int limit = 5000,
        bool newestFirst = true)
    {
        if (limit < 1)
        {
            return Array.Empty<ModbusLogRecord>();
        }

        lock (_syncRoot)
        {
            IEnumerable<ModbusLogRecord> query = _records.Where(record => record.Level >= minimumLevel);
            if (!string.IsNullOrWhiteSpace(keyword))
            {
                string normalized = keyword.Trim();
                query = query.Where(record =>
                    record.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture)
                        .Contains(normalized, StringComparison.OrdinalIgnoreCase)
                    || (record.Role?.ToString().Contains(
                        normalized,
                        StringComparison.OrdinalIgnoreCase) ?? false)
                    || (record.Direction?.ToString().Contains(
                        normalized,
                        StringComparison.OrdinalIgnoreCase) ?? false)
                    || record.Hex.Contains(normalized, StringComparison.OrdinalIgnoreCase)
                    || record.Meaning.Contains(normalized, StringComparison.OrdinalIgnoreCase)
                    || (record.ErrorHint?.Contains(
                        normalized,
                        StringComparison.OrdinalIgnoreCase) ?? false));
            }

            List<ModbusLogRecord> filtered = query.ToList();
            if (newestFirst)
            {
                filtered.Reverse();
            }

            return filtered.Take(limit).ToArray();
        }
    }

    /// <summary>
    /// 清空所有日志。
    /// </summary>
    public void Clear()
    {
        lock (_syncRoot)
        {
            _records.Clear();
            Interlocked.Increment(ref _revision);
        }
    }

    /// <summary>
    /// 按 RFC 4180 转义 CSV 字段。
    /// </summary>
    /// <param name="value">字段值。</param>
    /// <returns>转义后的字段。</returns>
    public static string EscapeCsvField(string? value)
    {
        string text = value ?? string.Empty;
        if (!text.Contains(',', StringComparison.Ordinal)
            && !text.Contains('"', StringComparison.Ordinal)
            && !text.Contains('\r', StringComparison.Ordinal)
            && !text.Contains('\n', StringComparison.Ordinal))
        {
            return text;
        }

        return $"\"{text.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }

    /// <summary>
    /// 导出 CSV，编码为 UTF-8 with BOM。
    /// </summary>
    /// <param name="path">目标路径。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task ExportCsvAsync(string path, CancellationToken cancellationToken = default)
    {
        StringBuilder builder = new();
        builder.AppendLine("时间戳,级别,角色,方向,原始十六进制,解析含义,错误提示");
        foreach (ModbusLogRecord record in GetSnapshot(
                     ModbusLogLevel.Debug,
                     limit: int.MaxValue,
                     newestFirst: false))
        {
            builder.Append(EscapeCsvField(FormatTimestamp(record.Timestamp))).Append(',')
                .Append(EscapeCsvField(record.Level.ToString())).Append(',')
                .Append(EscapeCsvField(record.Role?.ToString())).Append(',')
                .Append(EscapeCsvField(record.Direction?.ToString())).Append(',')
                .Append(EscapeCsvField(record.Hex)).Append(',')
                .Append(EscapeCsvField(record.Meaning)).Append(',')
                .Append(EscapeCsvField(record.ErrorHint)).AppendLine();
        }

        return WriteUtf8WithBomAsync(path, builder.ToString(), cancellationToken);
    }

    /// <summary>
    /// 导出 TXT，编码为 UTF-8 with BOM。
    /// </summary>
    /// <param name="path">目标路径。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task ExportTxtAsync(string path, CancellationToken cancellationToken = default)
    {
        StringBuilder builder = new();
        foreach (ModbusLogRecord record in GetSnapshot(
                     ModbusLogLevel.Debug,
                     limit: int.MaxValue,
                     newestFirst: false))
        {
            builder.Append(FormatTimestamp(record.Timestamp)).Append(" [")
                .Append(record.Level).Append("] [")
                .Append(record.Role?.ToString() ?? "-").Append('/')
                .Append(record.Direction?.ToString() ?? "-").Append("] ");
            if (!string.IsNullOrWhiteSpace(record.Hex))
            {
                builder.Append(record.Hex).Append(" | ");
            }

            builder.AppendLine(record.Meaning);
            if (!string.IsNullOrWhiteSpace(record.ErrorHint))
            {
                builder.Append("  提示：").AppendLine(record.ErrorHint);
            }
        }

        return WriteUtf8WithBomAsync(path, builder.ToString(), cancellationToken);
    }

    private void Add(ModbusLogRecord record)
    {
        lock (_syncRoot)
        {
            if (_paused)
            {
                return;
            }

            _records.Add(record);
            Interlocked.Increment(ref _revision);
            if (_records.Count > _capacity)
            {
                _records.RemoveRange(0, _records.Count - _capacity);
            }
        }
    }

    private static string FormatTimestamp(DateTimeOffset timestamp)
    {
        return timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture);
    }

    private static async Task WriteUtf8WithBomAsync(
        string path,
        string content,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        string? directory = Path.GetDirectoryName(Path.GetFullPath(path));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        byte[] preamble = Encoding.UTF8.GetPreamble();
        byte[] contentBytes = Encoding.UTF8.GetBytes(content);
        await using FileStream stream = new(
            path,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 16_384,
            useAsync: true);
        await stream.WriteAsync(preamble, cancellationToken);
        await stream.WriteAsync(contentBytes, cancellationToken);
    }
}
