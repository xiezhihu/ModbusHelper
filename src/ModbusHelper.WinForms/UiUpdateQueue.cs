using System.Collections.Concurrent;

namespace ModbusHelper.WinForms;

/// <summary>将后台变化合并到下一次 UI 消息处理，同一对象只读取一次最新状态。</summary>
internal sealed class UiUpdateQueue<T> : IDisposable where T : notnull
{
    private readonly Control _owner;
    private readonly Action<T> _update;
    private readonly ConcurrentDictionary<T, byte> _pending = new();
    private int _scheduled;
    private volatile bool _disposed;

    public UiUpdateQueue(Control owner, Action<T> update)
    {
        _owner = owner;
        _update = update;
        _owner.HandleCreated += OnHandleCreated;
        _owner.HandleDestroyed += OnHandleDestroyed;
    }

    public void Enqueue(T key)
    {
        if (_disposed)
        {
            return;
        }

        _pending[key] = 0;
        Schedule();
    }

    private void OnHandleCreated(object? sender, EventArgs args) => Schedule();

    private void OnHandleDestroyed(object? sender, EventArgs args) =>
        Interlocked.Exchange(ref _scheduled, 0);

    private void Schedule()
    {
        if (_disposed || !_owner.IsHandleCreated || _pending.IsEmpty
            || Interlocked.CompareExchange(ref _scheduled, 1, 0) != 0)
        {
            return;
        }

        try
        {
            _owner.BeginInvoke(new Action(Drain));
        }
        catch (InvalidOperationException) when (_disposed || !_owner.IsHandleCreated || _owner.Disposing)
        {
            // 窗口关闭可能与后台通知同时发生，不把句柄失效传播到通信线程。
            Interlocked.Exchange(ref _scheduled, 0);
        }
    }

    private void Drain()
    {
        try
        {
            foreach (T key in _pending.Keys)
            {
                if (!_disposed && _pending.TryRemove(key, out _))
                {
                    _update(key);
                }
            }
        }
        finally
        {
            Interlocked.Exchange(ref _scheduled, 0);
            Schedule();
        }
    }

    public void Dispose()
    {
        _disposed = true;
        _owner.HandleCreated -= OnHandleCreated;
        _owner.HandleDestroyed -= OnHandleDestroyed;
        _pending.Clear();
    }
}
