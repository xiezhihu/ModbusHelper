using System.IO.Ports;
using System.Net;
using System.Net.Sockets;

using ModbusHelper.Communication.Capturing;
using ModbusHelper.Communication.Validation;
using ModbusHelper.Core.Configuration;
using ModbusHelper.Core.Enums;
using ModbusHelper.Core.Models;

using NModbus;
using NModbus.Data;
using NModbus.Serial;

namespace ModbusHelper.Communication.Slaves;

/// <summary>
/// NModbus 3.0.x 从站模拟器。一个网络可挂载多个 UnitID，每个 UnitID 使用独立数据存储。
/// RTU 帧边界完全由 NModbus 串口传输层处理，本类不解析串口字节流。
/// </summary>
public sealed class ModbusSlaveSimulator : IAsyncDisposable
{
    private readonly ExceptionRuleProvider _ruleProvider = new();
    private readonly FrameCaptureLogger _frameLogger;
    private readonly object _syncRoot = new();
    private readonly Dictionary<byte, SlaveDataStore> _dataStores = new();
    private ModbusFactory? _factory;
    private IModbusSlaveNetwork? _network;
    private SerialPort? _serialPort;
    private TcpListener? _tcpListener;
    private CancellationTokenSource? _runCancellation;
    private Task? _listenTask;
    private SlaveSimulationConfiguration? _configuration;
    private SlaveSimulatorState _state = SlaveSimulatorState.Stopped;
    private bool _disposed;

    /// <summary>
    /// 初始化从站模拟器。
    /// </summary>
    /// <param name="configuration">初始从站配置。</param>
    public ModbusSlaveSimulator(SlaveSimulationConfiguration? configuration = null)
    {
        _configuration = configuration ?? new SlaveSimulationConfiguration();
        _frameLogger = new FrameCaptureLogger(
            ModbusRole.Slave,
            _configuration.Type == ConnectionType.Rtu);
        _frameLogger.FrameCaptured += frame => FrameCaptured?.Invoke(frame);
        _frameLogger.DiagnosticMessage += message => DiagnosticMessage?.Invoke(message);
    }

    /// <summary>原始帧通知。</summary>
    public event Action<RawFrameRecord>? FrameCaptured;

    /// <summary>传输层诊断消息。</summary>
    public event Action<TransportLogMessage>? DiagnosticMessage;

    /// <summary>状态变化。</summary>
    public event EventHandler<SlaveSimulatorStateChangedEventArgs>? StateChanged;

    /// <summary>监听错误。</summary>
    public event EventHandler<SlaveSimulatorErrorEventArgs>? ErrorOccurred;

    /// <summary>当前状态。</summary>
    public SlaveSimulatorState State => _state;

    /// <summary>是否正在监听。</summary>
    public bool IsListening => _state == SlaveSimulatorState.Listening;

    /// <summary>
    /// 启动 RTU 或 TCP 从站网络。
    /// </summary>
    /// <param name="configuration">从站配置。</param>
    /// <param name="cancellationToken">启动取消令牌。</param>
    /// <exception cref="ArgumentException">配置校验失败。</exception>
    public async Task StartAsync(
        SlaveSimulationConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(configuration);
        Core.Protocol.ValidationResult validation =
            ConnectionParameterValidator.ValidateSlave(configuration);
        if (!validation.IsValid)
        {
            throw new ArgumentException(validation.GetErrorMessage(), nameof(configuration));
        }

        await StopAsync().ConfigureAwait(false);
        lock (_syncRoot)
        {
            _configuration = configuration;
            _frameLogger.SetUsesCrc(configuration.Type == ConnectionType.Rtu);
            _ruleProvider.Update(
                configuration.Units.SelectMany(unit => unit.ExceptionRules));
            _dataStores.Clear();
            _runCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            SetState(SlaveSimulatorState.Starting, "正在启动从站模拟器");

            ModbusFactory baseFactory = new();
            IModbusFunctionService?[] services =
            {
                Wrap(baseFactory, 0x01),
                Wrap(baseFactory, 0x02),
                Wrap(baseFactory, 0x03),
                Wrap(baseFactory, 0x04),
                Wrap(baseFactory, 0x05),
                Wrap(baseFactory, 0x06),
                Wrap(baseFactory, 0x0F),
                Wrap(baseFactory, 0x10)
            };
            _factory = new ModbusFactory(
                services.Where(service => service is not null)!,
                includeBuiltIn: true,
                logger: _frameLogger);

            if (configuration.Type == ConnectionType.Rtu)
            {
                CreateRtuNetwork(configuration.Rtu);
            }
            else
            {
                CreateTcpNetwork(configuration.Tcp);
            }

            foreach (SlaveUnitConfiguration unit in configuration.Units)
            {
                SlaveDataStore store = CreateDataStore(unit);
                _dataStores[unit.UnitId] = store;
                IModbusSlave slave = _factory.CreateSlave(unit.UnitId, store);
                _network!.AddSlave(slave);
            }

            CancellationToken token = _runCancellation.Token;
            IModbusSlaveNetwork network = _network!;
            _listenTask = Task.Run(() => network.ListenAsync(token), CancellationToken.None);
            SetState(SlaveSimulatorState.Listening, "从站模拟器已开始监听");
            _ = ObserveListeningAsync(_listenTask);
        }
    }

    /// <summary>
    /// 停止监听并释放串口或 Socket。
    /// </summary>
    public async Task StopAsync()
    {
        CancellationTokenSource? cancellation;
        Task? listenTask;
        IModbusSlaveNetwork? network;
        SerialPort? serialPort;
        TcpListener? tcpListener;
        lock (_syncRoot)
        {
            cancellation = _runCancellation;
            listenTask = _listenTask;
            network = _network;
            serialPort = _serialPort;
            tcpListener = _tcpListener;
            _runCancellation = null;
            _listenTask = null;
            _network = null;
            _serialPort = null;
            _tcpListener = null;
        }

        cancellation?.Cancel();
        try
        {
            network?.Dispose();
        }
        catch (Exception)
        {
            // 网络可能已因监听错误提前释放。
        }

        tcpListener?.Stop();
        serialPort?.Dispose();

        if (listenTask is not null)
        {
            try
            {
                await listenTask.WaitAsync(TimeSpan.FromSeconds(3)).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                ErrorOccurred?.Invoke(
                    this,
                    new SlaveSimulatorErrorEventArgs(
                        new TimeoutException("从站监听任务停止超时。"),
                        "检查串口或网络连接是否被底层阻塞。"));
            }
            catch (Exception) when (cancellation?.IsCancellationRequested == true)
            {
                // 停止过程中的取消属于正常路径。
            }
        }

        cancellation?.Dispose();
        _factory = null;
        _dataStores.Clear();
        SetState(SlaveSimulatorState.Stopped, "从站模拟器已停止");
    }

    /// <summary>
    /// 更新异常注入规则，不需要重启监听。
    /// </summary>
    /// <param name="rules">完整规则集合。</param>
    public void UpdateExceptionRules(IEnumerable<ExceptionInjectionRule> rules)
    {
        ArgumentNullException.ThrowIfNull(rules);
        _ruleProvider.Update(rules);
    }

    /// <summary>
    /// 手动写入从站存储。
    /// </summary>
    /// <param name="unitId">从站地址。</param>
    /// <param name="area">存储区域。</param>
    /// <param name="startAddress">0 基协议地址。</param>
    /// <param name="values">值集合。</param>
    public void WriteValues(byte unitId, SlaveArea area, ushort startAddress, object values)
    {
        ArgumentNullException.ThrowIfNull(values);
        lock (_syncRoot)
        {
            if (!_dataStores.TryGetValue(unitId, out SlaveDataStore? store))
            {
                throw new ArgumentException($"从站 {unitId} 不存在。", nameof(unitId));
            }

            switch (area)
            {
                case SlaveArea.HoldingRegisters:
                    store.HoldingRegisters.WritePoints(startAddress, (ushort[])values);
                    break;
                case SlaveArea.InputRegisters:
                    store.InputRegisters.WritePoints(startAddress, (ushort[])values);
                    break;
                case SlaveArea.Coils:
                    store.CoilDiscretes.WritePoints(startAddress, (bool[])values);
                    break;
                case SlaveArea.DiscreteInputs:
                    store.CoilInputs.WritePoints(startAddress, (bool[])values);
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(area));
            }
        }
    }

    /// <summary>
    /// 读取从站存储快照。
    /// </summary>
    /// <param name="unitId">从站地址。</param>
    /// <param name="area">存储区域。</param>
    /// <param name="startAddress">0 基协议地址。</param>
    /// <param name="count">读取数量。</param>
    /// <returns>寄存器或布尔值数组。</returns>
    public object ReadValues(byte unitId, SlaveArea area, ushort startAddress, ushort count)
    {
        lock (_syncRoot)
        {
            if (!_dataStores.TryGetValue(unitId, out SlaveDataStore? store))
            {
                throw new ArgumentException($"从站 {unitId} 不存在。", nameof(unitId));
            }

            return area switch
            {
                SlaveArea.HoldingRegisters => store.HoldingRegisters.ReadPoints(startAddress, count),
                SlaveArea.InputRegisters => store.InputRegisters.ReadPoints(startAddress, count),
                SlaveArea.Coils => store.CoilDiscretes.ReadPoints(startAddress, count),
                SlaveArea.DiscreteInputs => store.CoilInputs.ReadPoints(startAddress, count),
                _ => throw new ArgumentOutOfRangeException(nameof(area))
            };
        }
    }

    private ExceptionInjectionFunctionService? Wrap(ModbusFactory factory, byte functionCode)
    {
        IModbusFunctionService? inner = factory.GetFunctionService(functionCode);
        return inner is null
            ? null
            : new ExceptionInjectionFunctionService(inner, _ruleProvider);
    }

    private void CreateTcpNetwork(TcpSlaveConfiguration tcp)
    {
        IPAddress address = IPAddress.Parse(tcp.ListenAddress);
        _tcpListener = new TcpListener(address, tcp.Port);
        _network = _factory!.CreateSlaveNetwork(_tcpListener);
    }

    private void CreateRtuNetwork(RtuConnectionConfiguration rtu)
    {
        SerialPort serialPort = new(
            rtu.PortName,
            rtu.BaudRate,
            MapParity(rtu.Parity),
            rtu.DataBits,
            MapStopBits(rtu.StopBits))
        {
            ReadTimeout = 500,
            WriteTimeout = 500
        };
        serialPort.Open();
        _serialPort = serialPort;
        _network = _factory!.CreateRtuSlaveNetwork(serialPort);
    }

    private static SlaveDataStore CreateDataStore(SlaveUnitConfiguration unit)
    {
        SlaveDataStore store = new();
        foreach (RegisterInitialValue value in unit.HoldingRegisters)
        {
            store.HoldingRegisters.WritePoints(value.Address, new[] { value.Value });
        }

        foreach (RegisterInitialValue value in unit.InputRegisters)
        {
            store.InputRegisters.WritePoints(value.Address, new[] { value.Value });
        }

        foreach (CoilInitialValue value in unit.Coils)
        {
            store.CoilDiscretes.WritePoints(value.Address, new[] { value.Value });
        }

        foreach (CoilInitialValue value in unit.DiscreteInputs)
        {
            store.CoilInputs.WritePoints(value.Address, new[] { value.Value });
        }

        return store;
    }

    private async Task ObserveListeningAsync(Task listenTask)
    {
        try
        {
            await listenTask.ConfigureAwait(false);
            if (_state == SlaveSimulatorState.Listening)
            {
                SetState(SlaveSimulatorState.Stopped, "从站监听任务已结束");
            }
        }
        catch (OperationCanceledException)
        {
            // 正常停止。
        }
        catch (Exception exception)
        {
            SetState(SlaveSimulatorState.Faulted, "从站监听发生错误");
            ErrorOccurred?.Invoke(
                this,
                new SlaveSimulatorErrorEventArgs(
                    exception,
                    "检查监听端口或串口是否被占用，以及监听地址是否有效。"));
        }
    }

    private void SetState(SlaveSimulatorState state, string message)
    {
        _state = state;
        StateChanged?.Invoke(this, new SlaveSimulatorStateChangedEventArgs(state, message));
    }

    private static Parity MapParity(SerialParity parity)
    {
        return parity switch
        {
            SerialParity.None => Parity.None,
            SerialParity.Odd => Parity.Odd,
            SerialParity.Even => Parity.Even,
            SerialParity.Mark => Parity.Mark,
            SerialParity.Space => Parity.Space,
            _ => throw new ArgumentOutOfRangeException(nameof(parity))
        };
    }

    private static StopBits MapStopBits(SerialStopBits stopBits)
    {
        return stopBits switch
        {
            SerialStopBits.One => StopBits.One,
            SerialStopBits.OnePointFive => StopBits.OnePointFive,
            SerialStopBits.Two => StopBits.Two,
            _ => throw new ArgumentOutOfRangeException(nameof(stopBits))
        };
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    /// <summary>
    /// 停止监听并释放所有资源。
    /// </summary>
    /// <returns>异步释放任务。</returns>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        await StopAsync().ConfigureAwait(false);
        GC.SuppressFinalize(this);
    }
}
