using ModbusHelper.Business.Logging;
using ModbusHelper.Business.Statistics;
using ModbusHelper.Communication.Capturing;
using ModbusHelper.Communication.Exceptions;
using ModbusHelper.Communication.Masters;
using ModbusHelper.Core.Configuration;
using ModbusHelper.Core.Enums;
using ModbusHelper.Core.Exceptions;
using ModbusHelper.Core.Models;

namespace ModbusHelper.Business.Connections;

/// <summary>
/// UI 无关的主站连接管理与操作封装。每个实例只拥有一个主站连接，多个实例可以并行。
/// </summary>
public sealed class ConnectionManager : IAsyncDisposable
{
    private readonly ModbusLogService _logService;
    private readonly CommunicationStatistics _statistics = new();
    private readonly SemaphoreSlim _lifecycleLock = new(1, 1);
    private ModbusService? _service;
    private bool _disposed;

    /// <summary>
    /// 初始化连接管理器。
    /// </summary>
    /// <param name="logService">日志服务。</param>
    public ConnectionManager(ModbusLogService logService)
    {
        _logService = logService ?? throw new ArgumentNullException(nameof(logService));
    }

    /// <summary>连接状态变化。</summary>
    public event EventHandler<ConnectionStateChangedEventArgs>? StateChanged;

    /// <summary>完整原始帧通知。</summary>
    public event Action<RawFrameRecord>? FrameCaptured;

    /// <summary>操作成功完成。</summary>
    public event Action<ModbusOperationResult>? OperationCompleted;

    /// <summary>通信异常通知。</summary>
    public event Action<ModbusCommunicationException>? CommunicationError;

    /// <summary>当前状态。</summary>
    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;

    /// <summary>是否已连接。</summary>
    public bool IsConnected => _service?.IsConnected == true;

    /// <summary>当前连接配置。</summary>
    public ConnectionConfiguration? Configuration { get; private set; }

    /// <summary>通信统计。</summary>
    public CommunicationStatistics Statistics => _statistics;

    /// <summary>
    /// 建立连接；已有连接会先安全断开。
    /// </summary>
    /// <param name="configuration">连接配置。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public async Task ConnectAsync(
        ConnectionConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(configuration);
        await _lifecycleLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await DisconnectCoreAsync().ConfigureAwait(false);
            ConnectionConfiguration capturedConfiguration = configuration;
            Configuration = capturedConfiguration;
            ModbusService service = new(capturedConfiguration);
            service.StateChanged += OnStateChanged;
            service.FrameCaptured += OnFrameCaptured;
            service.DiagnosticMessage += OnDiagnosticMessage;
            _service = service;
            await service.ConnectAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    /// <summary>
    /// 断开当前连接。
    /// </summary>
    public async Task DisconnectAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _lifecycleLock.WaitAsync().ConfigureAwait(false);
        try
        {
            await DisconnectCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _lifecycleLock.Release();
        }
    }

    /// <summary>
    /// 执行读取。
    /// </summary>
    /// <param name="function">读取功能码。</param>
    /// <param name="unitId">从站地址。</param>
    /// <param name="startAddress">0 基协议地址。</param>
    /// <param name="count">读取数量。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>操作结果。</returns>
    public async Task<ModbusOperationResult> ReadAsync(
        ModbusFunction function,
        byte unitId,
        ushort startAddress,
        ushort count,
        CancellationToken cancellationToken = default)
    {
        ModbusService service = GetConnectedService();
        try
        {
            ModbusOperationResult result = await service.ReadAsync(
                function,
                unitId,
                startAddress,
                count,
                cancellationToken).ConfigureAwait(false);
            _statistics.RecordOperation(result.ElapsedMilliseconds, null);
            OperationCompleted?.Invoke(result);
            return result;
        }
        catch (ModbusCommunicationException exception)
        {
            HandleCommunicationError(exception);
            throw;
        }
    }

    /// <summary>
    /// 执行写入。
    /// </summary>
    /// <param name="function">写入功能码。</param>
    /// <param name="unitId">从站地址，0 表示广播。</param>
    /// <param name="startAddress">0 基协议地址。</param>
    /// <param name="coils">线圈数据。</param>
    /// <param name="registers">寄存器数据。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>操作结果。</returns>
    public async Task<ModbusOperationResult> WriteAsync(
        ModbusFunction function,
        byte unitId,
        ushort startAddress,
        bool[] coils,
        ushort[] registers,
        CancellationToken cancellationToken = default)
    {
        ModbusService service = GetConnectedService();
        try
        {
            ModbusOperationResult result = await service.WriteAsync(
                function,
                unitId,
                startAddress,
                coils,
                registers,
                cancellationToken).ConfigureAwait(false);
            _statistics.RecordOperation(result.ElapsedMilliseconds, null);
            OperationCompleted?.Invoke(result);
            return result;
        }
        catch (ModbusCommunicationException exception)
        {
            HandleCommunicationError(exception);
            throw;
        }
    }

    private ModbusService GetConnectedService()
    {
        ThrowIfDisposed();
        if (_service?.IsConnected != true)
        {
            throw new ModbusCommunicationException(
                "尚未建立主站连接",
                CommunicationErrorKind.Connection,
                null,
                null,
                null,
                null,
                "请先在连接页面建立连接。");
        }

        return _service;
    }

    private async Task DisconnectCoreAsync()
    {
        if (_service is null)
        {
            State = ConnectionState.Disconnected;
            return;
        }

        ModbusService service = _service;
        _service = null;
        try
        {
            await service.DisconnectAsync().ConfigureAwait(false);
        }
        finally
        {
            service.StateChanged -= OnStateChanged;
            service.FrameCaptured -= OnFrameCaptured;
            service.DiagnosticMessage -= OnDiagnosticMessage;
            service.Dispose();
        }
    }

    private void OnStateChanged(object? sender, ConnectionStateChangedEventArgs args)
    {
        State = args.State;
        StateChanged?.Invoke(this, args);
    }

    private void OnFrameCaptured(RawFrameRecord frame)
    {
        _logService.AddFrame(frame);
        _statistics.RecordFrame(frame);
        FrameCaptured?.Invoke(frame);
    }

    private void OnDiagnosticMessage(TransportLogMessage message)
    {
        ModbusLogLevel level = message.Level switch
        {
            NModbus.LoggingLevel.Warning => ModbusLogLevel.Warn,
            NModbus.LoggingLevel.Error or NModbus.LoggingLevel.Critical => ModbusLogLevel.Error,
            _ => ModbusLogLevel.Debug
        };
        _logService.AddMessage(level, message.Message, ModbusRole.Master);
    }

    private void HandleCommunicationError(ModbusCommunicationException exception)
    {
        _statistics.RecordOperation(0, exception.ErrorKind);
        _logService.AddCommunicationError(exception);
        CommunicationError?.Invoke(exception);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    /// <summary>
    /// 释放连接和内部资源。
    /// </summary>
    /// <returns>异步释放任务。</returns>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await DisconnectCoreAsync().ConfigureAwait(false);
        _lifecycleLock.Dispose();
    }
}
