using ModbusHelper.Core.Configuration;
using ModbusHelper.Core.Enums;
using ModbusHelper.Core.Exceptions;

namespace ModbusHelper.Business.Polling;

/// <summary>
/// 轮询任务运行状态。
/// </summary>
public enum PollingTaskState
{
    /// <summary>已停止。</summary>
    Stopped,

    /// <summary>正在运行。</summary>
    Running,

    /// <summary>发生错误并停止。</summary>
    Faulted
}

/// <summary>
/// 轮询任务状态事件。
/// </summary>
public sealed class PollingTaskStateChangedEventArgs : EventArgs
{
    /// <summary>
    /// 初始化状态事件。
    /// </summary>
    /// <param name="taskId">任务标识。</param>
    /// <param name="taskName">任务名称。</param>
    /// <param name="state">任务状态。</param>
    /// <param name="message">中文说明。</param>
    public PollingTaskStateChangedEventArgs(
        string taskId,
        string taskName,
        PollingTaskState state,
        string message)
    {
        TaskId = taskId;
        TaskName = taskName;
        State = state;
        Message = message;
    }

    /// <summary>任务标识。</summary>
    public string TaskId { get; }

    /// <summary>任务名称。</summary>
    public string TaskName { get; }

    /// <summary>任务状态。</summary>
    public PollingTaskState State { get; }

    /// <summary>中文说明。</summary>
    public string Message { get; }
}

/// <summary>
/// 一次成功轮询结果。
/// </summary>
public sealed class PollingSampleEventArgs : EventArgs
{
    /// <summary>
    /// 初始化轮询结果。
    /// </summary>
    /// <param name="task">任务配置快照。</param>
    /// <param name="timestamp">采样时间。</param>
    /// <param name="parsedValue">解析值。</param>
    /// <param name="numericValue">可绘制数值。</param>
    /// <param name="coils">线圈结果。</param>
    /// <param name="registers">寄存器结果。</param>
    /// <param name="elapsedMilliseconds">通信耗时。</param>
    public PollingSampleEventArgs(
        PollingTaskConfiguration task,
        DateTimeOffset timestamp,
        string parsedValue,
        double numericValue,
        bool[]? coils,
        ushort[]? registers,
        long elapsedMilliseconds)
    {
        Task = task;
        Timestamp = timestamp;
        ParsedValue = parsedValue;
        NumericValue = numericValue;
        Coils = coils;
        Registers = registers;
        ElapsedMilliseconds = elapsedMilliseconds;
    }

    /// <summary>任务配置快照。</summary>
    public PollingTaskConfiguration Task { get; }

    /// <summary>采样时间。</summary>
    public DateTimeOffset Timestamp { get; }

    /// <summary>解析后的显示值。</summary>
    public string ParsedValue { get; }

    /// <summary>用于趋势图的数值。</summary>
    public double NumericValue { get; }

    /// <summary>线圈结果。</summary>
    public bool[]? Coils { get; }

    /// <summary>寄存器结果。</summary>
    public ushort[]? Registers { get; }

    /// <summary>通信耗时。</summary>
    public long ElapsedMilliseconds { get; }
}

/// <summary>
/// 轮询任务错误事件。
/// </summary>
public sealed class PollingTaskErrorEventArgs : EventArgs
{
    /// <summary>
    /// 初始化错误事件。
    /// </summary>
    /// <param name="taskId">任务标识。</param>
    /// <param name="taskName">任务名称。</param>
    /// <param name="exception">统一通信异常。</param>
    public PollingTaskErrorEventArgs(
        string taskId,
        string taskName,
        ModbusCommunicationException exception)
    {
        TaskId = taskId;
        TaskName = taskName;
        Exception = exception;
    }

    /// <summary>任务标识。</summary>
    public string TaskId { get; }

    /// <summary>任务名称。</summary>
    public string TaskName { get; }

    /// <summary>统一通信异常。</summary>
    public ModbusCommunicationException Exception { get; }
}
