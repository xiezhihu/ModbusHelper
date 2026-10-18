using ModbusHelper.Core.Enums;
using ModbusHelper.Core.Models;

namespace ModbusHelper.Business.Statistics;

/// <summary>
/// 线程安全的通信统计。
/// </summary>
public sealed class CommunicationStatistics
{
    private readonly object _durationLock = new();
    private long _requests;
    private long _responses;
    private long _errors;
    private long _timeouts;
    private long _slaveExceptions;
    private long _checksumErrors;
    private long _operations;
    private long _totalDurationMilliseconds;
    private long _lastDurationMilliseconds;
    private long _maximumDurationMilliseconds;

    /// <summary>
    /// 记录一条完整帧。
    /// </summary>
    /// <param name="frame">原始帧。</param>
    public void RecordFrame(RawFrameRecord frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.Role != ModbusRole.Master)
        {
            return;
        }

        if (frame.Direction == DataDirection.Tx)
        {
            Interlocked.Increment(ref _requests);
        }
        else
        {
            Interlocked.Increment(ref _responses);
        }

        if (frame.ChecksumStatus == false)
        {
            Interlocked.Increment(ref _checksumErrors);
        }
    }

    /// <summary>
    /// 记录一次操作耗时和分类。
    /// </summary>
    /// <param name="elapsedMilliseconds">耗时。</param>
    /// <param name="errorKind">成功时为 null。</param>
    public void RecordOperation(long elapsedMilliseconds, CommunicationErrorKind? errorKind)
    {
        lock (_durationLock)
        {
            _operations++;
            _totalDurationMilliseconds += elapsedMilliseconds;
            _lastDurationMilliseconds = elapsedMilliseconds;
            _maximumDurationMilliseconds = Math.Max(
                _maximumDurationMilliseconds,
                elapsedMilliseconds);
        }

        if (!errorKind.HasValue)
        {
            return;
        }

        Interlocked.Increment(ref _errors);
        if (errorKind == CommunicationErrorKind.Timeout)
        {
            Interlocked.Increment(ref _timeouts);
        }
        else if (errorKind == CommunicationErrorKind.SlaveException)
        {
            Interlocked.Increment(ref _slaveExceptions);
        }
        else if (errorKind == CommunicationErrorKind.Checksum)
        {
            Interlocked.Increment(ref _checksumErrors);
        }
    }

    /// <summary>
    /// 获取当前统计快照。
    /// </summary>
    /// <returns>不可变统计值。</returns>
    public CommunicationStatisticsSnapshot GetSnapshot()
    {
        long errorCount = Interlocked.Read(ref _errors);
        long operationCount;
        double average;
        long last;
        long maximum;
        lock (_durationLock)
        {
            operationCount = _operations;
            average = operationCount == 0
                ? 0
                : (double)_totalDurationMilliseconds / operationCount;
            last = _lastDurationMilliseconds;
            maximum = _maximumDurationMilliseconds;
        }

        return new CommunicationStatisticsSnapshot(
            Interlocked.Read(ref _requests),
            Interlocked.Read(ref _responses),
            operationCount,
            errorCount,
            Interlocked.Read(ref _timeouts),
            Interlocked.Read(ref _slaveExceptions),
            Interlocked.Read(ref _checksumErrors),
            average,
            last,
            maximum);
    }

    /// <summary>
    /// 清空统计。
    /// </summary>
    public void Reset()
    {
        Interlocked.Exchange(ref _requests, 0);
        Interlocked.Exchange(ref _responses, 0);
        Interlocked.Exchange(ref _errors, 0);
        Interlocked.Exchange(ref _timeouts, 0);
        Interlocked.Exchange(ref _slaveExceptions, 0);
        Interlocked.Exchange(ref _checksumErrors, 0);
        lock (_durationLock)
        {
            _operations = 0;
            _totalDurationMilliseconds = 0;
            _lastDurationMilliseconds = 0;
            _maximumDurationMilliseconds = 0;
        }
    }
}

/// <summary>
/// 通信统计快照。
/// </summary>
/// <param name="Requests">发送请求帧数。</param>
/// <param name="Responses">接收响应帧数。</param>
/// <param name="Operations">完成操作数。</param>
/// <param name="Errors">错误数。</param>
/// <param name="Timeouts">超时数。</param>
/// <param name="SlaveExceptions">从站异常响应数。</param>
/// <param name="ChecksumErrors">CRC 错误数。</param>
/// <param name="AverageDurationMilliseconds">平均耗时，单位毫秒。</param>
/// <param name="LastDurationMilliseconds">最近耗时，单位毫秒。</param>
/// <param name="MaximumDurationMilliseconds">最大耗时，单位毫秒。</param>
public sealed record CommunicationStatisticsSnapshot(
    long Requests,
    long Responses,
    long Operations,
    long Errors,
    long Timeouts,
    long SlaveExceptions,
    long ChecksumErrors,
    double AverageDurationMilliseconds,
    long LastDurationMilliseconds,
    long MaximumDurationMilliseconds);
