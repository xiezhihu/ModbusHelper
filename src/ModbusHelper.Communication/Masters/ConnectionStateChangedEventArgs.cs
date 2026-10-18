using ModbusHelper.Core.Enums;

namespace ModbusHelper.Communication.Masters;

/// <summary>
/// 连接状态变化事件参数。
/// </summary>
public sealed class ConnectionStateChangedEventArgs : EventArgs
{
    /// <summary>
    /// 初始化事件参数。
    /// </summary>
    /// <param name="state">新状态。</param>
    /// <param name="message">中文说明。</param>
    public ConnectionStateChangedEventArgs(ConnectionState state, string message)
    {
        State = state;
        Message = message;
    }

    /// <summary>新状态。</summary>
    public ConnectionState State { get; }

    /// <summary>中文状态说明。</summary>
    public string Message { get; }
}
