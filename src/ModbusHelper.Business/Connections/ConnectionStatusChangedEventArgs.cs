using ModbusHelper.Core.Enums;

namespace ModbusHelper.Business.Connections;

/// <summary>
/// 业务层连接状态事件。
/// </summary>
public sealed class ConnectionStatusChangedEventArgs : EventArgs
{
    /// <summary>
    /// 初始化事件。
    /// </summary>
    /// <param name="state">连接状态。</param>
    /// <param name="message">中文说明。</param>
    public ConnectionStatusChangedEventArgs(ConnectionState state, string message)
    {
        State = state;
        Message = message;
    }

    /// <summary>连接状态。</summary>
    public ConnectionState State { get; }

    /// <summary>中文说明。</summary>
    public string Message { get; }
}
