using System.Diagnostics;

using ModbusHelper.Business.Connections;
using ModbusHelper.Business.Data;
using ModbusHelper.Business.Logging;
using ModbusHelper.Business.Trends;
using ModbusHelper.Communication.Validation;
using ModbusHelper.Core.Configuration;
using ModbusHelper.Core.Enums;
using ModbusHelper.Core.Exceptions;
using ModbusHelper.Core.Models;
using ModbusHelper.Core.Protocol;

namespace ModbusHelper.Business.Polling;

/// <summary>
/// 多轮询任务调度器。每个任务运行在独立循环中，但所有读写都通过同一连接管理器进入唯一串行上下文。
/// 调度器及其事件完全 UI 无关，可由命令行宿主直接复用。
/// </summary>
public sealed class PollingScheduler : IAsyncDisposable
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<string, RuntimeTask> _tasks = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConnectionManager _connectionManager;
    private readonly TrendDataStore _trendStore;
    private readonly ModbusLogService _logService;
    private bool _disposed;

    /// <summary>
    /// 初始化轮询调度器。
    /// </summary>
    /// <param name="connectionManager">连接管理器。</param>
    /// <param name="trendStore">趋势数据存储。</param>
    /// <param name="logService">日志服务。</param>
    public PollingScheduler(
        ConnectionManager connectionManager,
        TrendDataStore trendStore,
        ModbusLogService logService)
    {
        _connectionManager = connectionManager;
        _trendStore = trendStore;
        _logService = logService;
        _connectionManager.StateChanged += OnConnectionStateChanged;
    }

    /// <summary>任务状态变化。事件可能在后台线程触发。</summary>
    public event EventHandler<PollingTaskStateChangedEventArgs>? TaskStateChanged;

    /// <summary>成功采样。事件在后台线程触发，UI 必须通过队列或 Timer 消费。</summary>
    public event EventHandler<PollingSampleEventArgs>? SampleReceived;

    /// <summary>任务错误。事件在后台线程触发。</summary>
    public event EventHandler<PollingTaskErrorEventArgs>? TaskFailed;

    /// <summary>
    /// 启动配置中所有已启用的任务。
    /// </summary>
    /// <param name="tasks">任务配置集合。</param>
    public Task StartEnabledAsync(IEnumerable<PollingTaskConfiguration> tasks)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(tasks);
        foreach (PollingTaskConfiguration task in tasks.Where(task => task.Enabled))
        {
            StartTask(task);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// 启动单个任务。
    /// </summary>
    /// <param name="configuration">任务配置。</param>
    public void StartTask(PollingTaskConfiguration configuration)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(configuration);
        Validate(configuration);

        PollingTaskConfiguration snapshot = Clone(configuration);
        CancellationTokenSource cancellationSource = new();
        RuntimeTask runtime = new(snapshot, cancellationSource);
        lock (_syncRoot)
        {
            if (_tasks.TryGetValue(snapshot.Id, out RuntimeTask? existing))
            {
                if (!existing.Completion.IsCompleted)
                {
                    return;
                }

                existing.Cancellation.Dispose();
            }

            _tasks[snapshot.Id] = runtime;
            snapshot.IsRunning = true;
        }

        _trendStore.GetOrCreate(snapshot);
        runtime.Execution = Task.Run(() => RunTaskAsync(runtime), CancellationToken.None);
        TaskStateChanged?.Invoke(
            this,
            new PollingTaskStateChangedEventArgs(
                snapshot.Id,
                snapshot.Name,
                PollingTaskState.Running,
                "轮询任务已启动"));
    }

    /// <summary>
    /// 停止单个任务。
    /// </summary>
    /// <param name="taskId">任务标识。</param>
    /// <returns>异步停止任务。</returns>
    public async Task StopTaskAsync(string taskId)
    {
        RuntimeTask? runtime;
        lock (_syncRoot)
        {
            _tasks.TryGetValue(taskId, out runtime);
        }

        if (runtime is null)
        {
            return;
        }

        await StopRuntimeAsync(runtime).ConfigureAwait(false);
    }

    /// <summary>
    /// 停止全部任务。
    /// </summary>
    public async Task StopAllAsync()
    {
        RuntimeTask[] runtimes;
        lock (_syncRoot)
        {
            runtimes = _tasks.Values.ToArray();
        }

        await Task.WhenAll(runtimes.Select(StopRuntimeAsync)).ConfigureAwait(false);
    }

    private async Task RunTaskAsync(RuntimeTask runtime)
    {
        CancellationToken cancellationToken = runtime.Cancellation.Token;
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (!_connectionManager.IsConnected)
                {
                    PublishState(runtime, PollingTaskState.Stopped, "连接已断开，轮询任务自动停止");
                    return;
                }

                Stopwatch stopwatch = Stopwatch.StartNew();
                try
                {
                    ModbusOperationResult result = await _connectionManager.ReadAsync(
                        runtime.Configuration.Function,
                        runtime.Configuration.UnitId,
                        runtime.Configuration.StartAddress,
                        runtime.Configuration.Count,
                        cancellationToken).ConfigureAwait(false);
                    stopwatch.Stop();
                    PublishSample(runtime.Configuration, result);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (ModbusCommunicationException exception)
                {
                    TaskFailed?.Invoke(
                        this,
                        new PollingTaskErrorEventArgs(
                            runtime.Configuration.Id,
                            runtime.Configuration.Name,
                            exception));
                    _logService.AddMessage(
                        ModbusLogLevel.Warn,
                        $"轮询任务“{runtime.Configuration.Name}”失败：{exception.GetDisplayMessage()}",
                        ModbusRole.Master);
                    if (exception.ErrorKind is not CommunicationErrorKind.SlaveException)
                    {
                        PublishState(runtime, PollingTaskState.Faulted, "通信错误，轮询任务已停止");
                        return;
                    }
                }

                int remaining = runtime.Configuration.IntervalMilliseconds
                                - checked((int)Math.Min(stopwatch.ElapsedMilliseconds, int.MaxValue));
                if (remaining > 0)
                {
                    await Task.Delay(remaining, cancellationToken).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // 正常停止。
        }
        finally
        {
            runtime.Configuration.IsRunning = false;
            if (!cancellationToken.IsCancellationRequested)
            {
                PublishState(runtime, PollingTaskState.Stopped, "轮询任务已停止");
            }
        }
    }

    private void PublishSample(
        PollingTaskConfiguration task,
        ModbusOperationResult result)
    {
        string parsedValue;
        double numericValue;
        if (result.Coils is { } coils)
        {
            parsedValue = string.Join(", ", coils.Select(value => value ? "1" : "0"));
            numericValue = coils.Length > 0 && coils[0] ? 1 : 0;
        }
        else
        {
            ushort[] registers = result.Registers ?? Array.Empty<ushort>();
            try
            {
                object decoded = RegisterCodec.Decode(
                    registers,
                    task.DataType,
                    task.ByteOrder);
                parsedValue = RegisterCodec.Format(decoded);
                numericValue = Convert.ToDouble(decoded, System.Globalization.CultureInfo.InvariantCulture);
            }
            catch (Exception)
            {
                parsedValue = string.Join(", ", registers.Select(value => $"0x{value:X4}"));
                numericValue = registers.Length > 0 ? registers[0] : double.NaN;
            }
        }

        DateTimeOffset timestamp = result.Timestamp;
        if (task.ShowInTrend)
        {
            _trendStore.GetOrCreate(task).Append(new TrendSample(
                timestamp,
                numericValue,
                parsedValue));
        }

        SampleReceived?.Invoke(
            this,
            new PollingSampleEventArgs(
                task,
                timestamp,
                parsedValue,
                numericValue,
                result.Coils,
                result.Registers,
                result.ElapsedMilliseconds));
    }

    private static void Validate(PollingTaskConfiguration task)
    {
        ValidationResult result = new();
        Merge(result, ModbusRequestValidator.ValidateReadUnitId(task.UnitId));
        Merge(result, ModbusRequestValidator.ValidateReadCount(task.Function, task.Count));
        Merge(result, ModbusRequestValidator.ValidateAddressRange(task.StartAddress, task.Count));
        Merge(result, ModbusRequestValidator.ValidatePollingInterval(task.IntervalMilliseconds));
        if (!ModbusDataShapeRules.IsCompatible(task.Function, task.DataType, task.Count))
        {
            int required = ModbusDataShapeRules.GetRequiredRegisterCount(task.DataType);
            result.AddError(
                $"功能码 {task.Function.GetChineseName()} 使用 {task.DataType} 时，读取数量必须为 {required} 个寄存器。");
        }

        if (!result.IsValid)
        {
            throw new ArgumentException(result.GetErrorMessage(), nameof(task));
        }
    }

    private static void Merge(ValidationResult target, ValidationResult source)
    {
        foreach (string error in source.Errors)
        {
            target.AddError(error);
        }
    }

    private async Task StopRuntimeAsync(RuntimeTask runtime)
    {
        runtime.Cancellation.Cancel();
        try
        {
            await runtime.Completion.WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            _logService.AddMessage(
                ModbusLogLevel.Warn,
                $"轮询任务“{runtime.Configuration.Name}”停止超时。",
                ModbusRole.Master);
        }

        runtime.Configuration.IsRunning = false;
        PublishState(runtime, PollingTaskState.Stopped, "轮询任务已停止");
    }

    private void PublishState(
        RuntimeTask runtime,
        PollingTaskState state,
        string message)
    {
        if (state != PollingTaskState.Running)
        {
            if (!runtime.TryMarkTerminalStatePublished())
            {
                return;
            }

            runtime.Configuration.IsRunning = false;
            lock (_syncRoot)
            {
                if (_tasks.TryGetValue(runtime.Configuration.Id, out RuntimeTask? current)
                    && ReferenceEquals(current, runtime))
                {
                    _tasks.Remove(runtime.Configuration.Id);
                }
            }
        }

        TaskStateChanged?.Invoke(
            this,
            new PollingTaskStateChangedEventArgs(
                runtime.Configuration.Id,
                runtime.Configuration.Name,
                state,
                message));
    }

    private void OnConnectionStateChanged(object? sender, ConnectionStatusChangedEventArgs args)
    {
        if (args.State is ConnectionState.Faulted or ConnectionState.Disconnected)
        {
            _ = StopAllQuietlyAsync();
        }
    }

    private async Task StopAllQuietlyAsync()
    {
        try
        {
            await StopAllAsync().ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            _logService.AddMessage(
                ModbusLogLevel.Warn,
                $"停止轮询任务时发生异常：{exception.Message}",
                ModbusRole.Master);
        }
    }

    private static PollingTaskConfiguration Clone(PollingTaskConfiguration source)
    {
        return new PollingTaskConfiguration
        {
            Id = source.Id,
            Name = source.Name,
            Enabled = source.Enabled,
            Function = source.Function,
            UnitId = source.UnitId,
            StartAddress = source.StartAddress,
            Count = source.Count,
            IntervalMilliseconds = Math.Max(
                source.IntervalMilliseconds,
                ProtocolLimits.MinimumPollingIntervalMilliseconds),
            DataType = source.DataType,
            ByteOrder = source.ByteOrder,
            ShowInTrend = source.ShowInTrend,
            TrendVisible = source.TrendVisible,
            TrendColorArgb = source.TrendColorArgb,
            PointId = source.PointId
        };
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    /// <summary>
    /// 停止并释放所有轮询任务。
    /// </summary>
    /// <returns>异步释放任务。</returns>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _connectionManager.StateChanged -= OnConnectionStateChanged;
        await StopAllAsync().ConfigureAwait(false);
        lock (_syncRoot)
        {
            foreach (RuntimeTask runtime in _tasks.Values)
            {
                runtime.Cancellation.Dispose();
            }

            _tasks.Clear();
        }
    }

    private sealed class RuntimeTask
    {
        public RuntimeTask(
            PollingTaskConfiguration configuration,
            CancellationTokenSource cancellation)
        {
            Configuration = configuration;
            Cancellation = cancellation;
        }

        public PollingTaskConfiguration Configuration { get; }

        public CancellationTokenSource Cancellation { get; }

        public Task Execution { get; set; } = Task.CompletedTask;

        public Task Completion => Execution;

        private int _terminalStatePublished;

        public bool TryMarkTerminalStatePublished()
        {
            return Interlocked.Exchange(ref _terminalStatePublished, 1) == 0;
        }
    }
}
