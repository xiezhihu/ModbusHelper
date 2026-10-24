using ModbusHelper.Business.Logging;
using ModbusHelper.Communication.Capturing;
using ModbusHelper.Communication.Slaves;
using ModbusHelper.Communication.Validation;
using ModbusHelper.Core.Configuration;
using ModbusHelper.Core.Enums;
using ModbusHelper.Core.Models;
using ModbusHelper.Core.Protocol;

namespace ModbusHelper.Business.Slaves;

/// <summary>
/// 业务层从站模拟状态事件。
/// </summary>
public sealed class SlaveStatusChangedEventArgs : EventArgs
{
    /// <summary>
    /// 初始化状态事件。
    /// </summary>
    /// <param name="isListening">是否正在监听。</param>
    /// <param name="message">中文说明。</param>
    public SlaveStatusChangedEventArgs(bool isListening, string message)
    {
        IsListening = isListening;
        Message = message;
    }

    /// <summary>是否正在监听。</summary>
    public bool IsListening { get; }

    /// <summary>中文说明。</summary>
    public string Message { get; }
}

/// <summary>
/// 业务层从站模拟错误事件。
/// </summary>
public sealed class SlaveErrorEventArgs : EventArgs
{
    /// <summary>
    /// 初始化错误事件。
    /// </summary>
    /// <param name="message">中文错误信息。</param>
    public SlaveErrorEventArgs(string message)
    {
        Message = message;
    }

    /// <summary>中文错误信息。</summary>
    public string Message { get; }
}

/// <summary>
/// UI 无关的从站模拟管理入口。负责把模拟器帧和错误接入普通日志服务。
/// </summary>
public sealed class SlaveSimulationManager : IAsyncDisposable
{
    private readonly ModbusLogService _logService;
    private readonly ModbusSlaveSimulator _simulator;

    /// <summary>
    /// 初始化从站模拟管理。
    /// </summary>
    /// <param name="logService">日志服务。</param>
    public SlaveSimulationManager(ModbusLogService logService)
    {
        _logService = logService;
        _simulator = new ModbusSlaveSimulator();
        _simulator.FrameCaptured += OnFrameCaptured;
        _simulator.DiagnosticMessage += OnDiagnosticMessage;
        _simulator.StateChanged += OnStateChanged;
        _simulator.ErrorOccurred += OnErrorOccurred;
        _simulator.DataChanged += OnDataChanged;
    }

    /// <summary>状态变化。</summary>
    public event EventHandler<SlaveStatusChangedEventArgs>? StateChanged;

    /// <summary>从站错误。</summary>
    public event EventHandler<SlaveErrorEventArgs>? ErrorOccurred;

    /// <summary>运行数据变化；通知来自写入线程，界面应异步切换到 UI 线程处理。</summary>
    public event EventHandler<SlaveDataChangedEventArgs>? DataChanged;

    /// <summary>当前状态。</summary>
    public SlaveSimulatorState State => _simulator.State;

    /// <summary>是否正在监听。</summary>
    public bool IsListening => _simulator.IsListening;

    /// <summary>
    /// 校验从站模拟配置。
    /// </summary>
    /// <param name="configuration">从站配置。</param>
    /// <returns>参数校验结果。</returns>
    public static ValidationResult ValidateConfiguration(
        SlaveSimulationConfiguration configuration)
    {
        return ConnectionParameterValidator.ValidateSlave(configuration);
    }

    /// <summary>
    /// 启动从站。
    /// </summary>
    /// <param name="configuration">从站配置。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    public Task StartAsync(
        SlaveSimulationConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        return _simulator.StartAsync(configuration, cancellationToken);
    }

    /// <summary>
    /// 停止从站。
    /// </summary>
    public Task StopAsync()
    {
        return _simulator.StopAsync();
    }

    /// <summary>
    /// 更新异常规则。
    /// </summary>
    /// <param name="rules">规则集合。</param>
    public void UpdateExceptionRules(IEnumerable<ExceptionInjectionRule> rules)
    {
        _simulator.UpdateExceptionRules(rules);
    }

    /// <summary>
    /// 手动写入模拟值。
    /// </summary>
    /// <param name="unitId">从站地址。</param>
    /// <param name="area">存储区域。</param>
    /// <param name="startAddress">0 基协议地址。</param>
    /// <param name="values">值集合。</param>
    public void WriteValues(byte unitId, SlaveArea area, ushort startAddress, object values)
    {
        _simulator.WriteValues(unitId, area, startAddress, values);
    }

    /// <summary>
    /// 读取模拟值。
    /// </summary>
    /// <param name="unitId">从站地址。</param>
    /// <param name="area">存储区域。</param>
    /// <param name="startAddress">0 基协议地址。</param>
    /// <param name="count">数量。</param>
    /// <returns>值集合。</returns>
    public object ReadValues(byte unitId, SlaveArea area, ushort startAddress, ushort count)
    {
        return _simulator.ReadValues(unitId, area, startAddress, count);
    }

    /// <summary>读取从站当前数据副本，包含初始、手动、随机和主站写入的地址；不修改启动配置。</summary>
    public SlaveUnitDataSnapshot? GetDataSnapshot(byte unitId)
    {
        return _simulator.GetDataSnapshot(unitId);
    }

    private void OnFrameCaptured(RawFrameRecord frame)
    {
        _logService.AddFrame(frame);
    }

    private void OnDataChanged(object? sender, SlaveDataChangedEventArgs args)
    {
        DataChanged?.Invoke(this, args);
    }

    private void OnDiagnosticMessage(TransportLogMessage message)
    {
        if (message.Level < TransportLogLevel.Warning)
        {
            return;
        }

        ModbusLogLevel level = message.Level >= TransportLogLevel.Error
            ? ModbusLogLevel.Error
            : ModbusLogLevel.Warn;
        _logService.AddMessage(level, message.Message, ModbusRole.Slave);
    }

    private void OnErrorOccurred(object? sender, SlaveSimulatorErrorEventArgs args)
    {
        _logService.AddMessage(
            ModbusLogLevel.Error,
            $"从站模拟器错误：{args.Exception.Message}。{args.Hint}",
            ModbusRole.Slave);
        ErrorOccurred?.Invoke(
            this,
            new SlaveErrorEventArgs($"{args.Exception.Message}。{args.Hint}"));
    }

    private void OnStateChanged(object? sender, SlaveSimulatorStateChangedEventArgs args)
    {
        StateChanged?.Invoke(
            this,
            new SlaveStatusChangedEventArgs(
                args.State == SlaveSimulatorState.Listening,
                args.Message));
    }

    /// <summary>
    /// 停止并释放模拟器。
    /// </summary>
    /// <returns>异步释放任务。</returns>
    public async ValueTask DisposeAsync()
    {
        await _simulator.DisposeAsync().ConfigureAwait(false);
    }
}
