using ModbusHelper.Core.Enums;

namespace ModbusHelper.Communication.Slaves;

/// <summary>
/// 从站模拟器状态。
/// </summary>
public enum SlaveSimulatorState
{
    /// <summary>已停止。</summary>
    Stopped,

    /// <summary>正在启动。</summary>
    Starting,

    /// <summary>正在监听。</summary>
    Listening,

    /// <summary>发生故障。</summary>
    Faulted
}

/// <summary>
/// 从站模拟器状态事件。
/// </summary>
public sealed class SlaveSimulatorStateChangedEventArgs : EventArgs
{
    /// <summary>
    /// 初始化状态事件。
    /// </summary>
    /// <param name="state">新状态。</param>
    /// <param name="message">中文说明。</param>
    public SlaveSimulatorStateChangedEventArgs(SlaveSimulatorState state, string message)
    {
        State = state;
        Message = message;
    }

    /// <summary>新状态。</summary>
    public SlaveSimulatorState State { get; }

    /// <summary>中文说明。</summary>
    public string Message { get; }
}

/// <summary>
/// 从站模拟器错误事件。
/// </summary>
public sealed class SlaveSimulatorErrorEventArgs : EventArgs
{
    /// <summary>
    /// 初始化错误事件。
    /// </summary>
    /// <param name="exception">原始异常。</param>
    /// <param name="hint">中文排查建议。</param>
    public SlaveSimulatorErrorEventArgs(Exception exception, string hint)
    {
        Exception = exception;
        Hint = hint;
    }

    /// <summary>原始异常。</summary>
    public Exception Exception { get; }

    /// <summary>中文排查建议。</summary>
    public string Hint { get; }
}
