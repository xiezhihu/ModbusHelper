using System.Diagnostics;
using System.IO.Ports;
using System.Net.Sockets;
using ModbusHelper.Communication.Capturing;
using ModbusHelper.Communication.Exceptions;
using ModbusHelper.Communication.Validation;
using ModbusHelper.Core.Configuration;
using ModbusHelper.Core.Enums;
using ModbusHelper.Core.Exceptions;
using ModbusHelper.Core.Models;
using ModbusHelper.Core.Protocol;
using NModbus;
using NModbus.Data;
using NModbus.Message;
using NModbus.Serial;

namespace ModbusHelper.Communication.Masters;

/// <summary>
/// UI 无关的 Modbus 主站通信入口。内部负责参数校验、连接生命周期、串行调用、性能计时、
/// 原始帧通知和异常转换。界面和命令行都不应直接调用 NModbus。
/// </summary>
public sealed class ModbusService : IDisposable
{
    private readonly ConnectionConfiguration _configuration;
    private readonly FrameCaptureLogger _frameLogger;
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private readonly object _resourceLock = new();
    private ModbusFactory? _factory;
    private IModbusMaster? _master;
    private SerialPort? _serialPort;
    private TcpClient? _tcpClient;
    private List<RawFrameRecord>? _activeFrames;
    private ConnectionState _state = ConnectionState.Disconnected;
    private bool _disposed;

    /// <summary>
    /// 初始化主站通信服务。
    /// </summary>
    /// <param name="configuration">连接配置。</param>
    public ModbusService(ConnectionConfiguration configuration)
    {
        _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));
        _frameLogger = new FrameCaptureLogger(
            ModbusRole.Master,
            configuration.Type == ConnectionType.Rtu);
        _frameLogger.FrameCaptured += OnFrameCaptured;
        _frameLogger.DiagnosticMessage += message => DiagnosticMessage?.Invoke(message);
    }

    /// <summary>连接状态变化。</summary>
    public event EventHandler<ConnectionStateChangedEventArgs>? StateChanged;

    /// <summary>收到完整原始帧。</summary>
    public event Action<RawFrameRecord>? FrameCaptured;

    /// <summary>收到传输层诊断消息。</summary>
    public event Action<TransportLogMessage>? DiagnosticMessage;

    /// <summary>当前连接状态。</summary>
    public ConnectionState State => _state;

    /// <summary>是否已经连接。</summary>
    public bool IsConnected => _state == ConnectionState.Connected && _master is not null;

    /// <summary>
    /// 建立连接。相同实例断开后可重复连接，连接配置保持不变。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <exception cref="ModbusCommunicationException">参数、端口、网络或超时错误。</exception>
    public async Task ConnectAsync(CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        if (IsConnected)
        {
            return;
        }

        ValidationResult validation = ConnectionParameterValidator.Validate(_configuration);
        if (!validation.IsValid)
        {
            throw new ModbusCommunicationException(
                validation.GetErrorMessage(),
                CommunicationErrorKind.Validation,
                null,
                null,
                null,
                null,
                "修正连接参数后重试。");
        }

        ChangeState(ConnectionState.Connecting, "正在建立连接");
        try
        {
            DisposeTransportResources();
            _factory = new ModbusFactory(logger: _frameLogger);
            if (_configuration.Type == ConnectionType.Rtu)
            {
                await ConnectRtuAsync(cancellationToken);
            }
            else
            {
                await ConnectTcpAsync(cancellationToken);
            }

            ConfigureTransport();
            _configuration.RuntimeState = ConnectionState.Connected;
            _configuration.IsConnected = true;
            ChangeState(ConnectionState.Connected, "连接成功");
        }
        catch (ModbusCommunicationException)
        {
            DisposeTransportResources();
            _configuration.RuntimeState = ConnectionState.Faulted;
            _configuration.IsConnected = false;
            ChangeState(ConnectionState.Faulted, "连接失败");
            throw;
        }
        catch (Exception exception)
        {
            DisposeTransportResources();
            _configuration.RuntimeState = ConnectionState.Faulted;
            _configuration.IsConnected = false;
            ChangeState(ConnectionState.Faulted, "连接失败");
            throw ModbusExceptionTranslator.Translate(exception, "建立连接");
        }
    }

    /// <summary>
    /// 断开连接并释放串口或 Socket。
    /// </summary>
    public async Task DisconnectAsync()
    {
        if (_disposed && _master is null && _serialPort is null && _tcpClient is null)
        {
            return;
        }

        ChangeState(ConnectionState.Disconnecting, "正在断开连接");
        bool entered = await _operationLock.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        try
        {
            DisposeTransportResources();
            _configuration.RuntimeState = ConnectionState.Disconnected;
            _configuration.IsConnected = false;
            ChangeState(ConnectionState.Disconnected, "已断开连接");
        }
        finally
        {
            if (entered)
            {
                _operationLock.Release();
            }
        }
    }

    /// <summary>
    /// 读取保持寄存器。
    /// </summary>
    /// <param name="unitId">从站地址。</param>
    /// <param name="startAddress">0 基协议地址。</param>
    /// <param name="count">读取数量。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>操作结果。</returns>
    public Task<ModbusOperationResult> ReadHoldingRegistersAsync(
        byte unitId,
        ushort startAddress,
        ushort count,
        CancellationToken cancellationToken = default)
    {
        return ReadAsync(
            ModbusFunction.ReadHoldingRegisters,
            unitId,
            startAddress,
            count,
            cancellationToken);
    }

    /// <summary>
    /// 读取输入寄存器。
    /// </summary>
    /// <param name="unitId">从站地址。</param>
    /// <param name="startAddress">0 基协议地址。</param>
    /// <param name="count">读取数量。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>操作结果。</returns>
    public Task<ModbusOperationResult> ReadInputRegistersAsync(
        byte unitId,
        ushort startAddress,
        ushort count,
        CancellationToken cancellationToken = default)
    {
        return ReadAsync(
            ModbusFunction.ReadInputRegisters,
            unitId,
            startAddress,
            count,
            cancellationToken);
    }

    /// <summary>
    /// 读取线圈。
    /// </summary>
    /// <param name="unitId">从站地址。</param>
    /// <param name="startAddress">0 基协议地址。</param>
    /// <param name="count">读取数量。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>操作结果。</returns>
    public Task<ModbusOperationResult> ReadCoilsAsync(
        byte unitId,
        ushort startAddress,
        ushort count,
        CancellationToken cancellationToken = default)
    {
        return ReadAsync(ModbusFunction.ReadCoils, unitId, startAddress, count, cancellationToken);
    }

    /// <summary>
    /// 读取离散输入。
    /// </summary>
    /// <param name="unitId">从站地址。</param>
    /// <param name="startAddress">0 基协议地址。</param>
    /// <param name="count">读取数量。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>操作结果。</returns>
    public Task<ModbusOperationResult> ReadDiscreteInputsAsync(
        byte unitId,
        ushort startAddress,
        ushort count,
        CancellationToken cancellationToken = default)
    {
        return ReadAsync(
            ModbusFunction.ReadDiscreteInputs,
            unitId,
            startAddress,
            count,
            cancellationToken);
    }

    /// <summary>
    /// 按读取功能码执行一次读取。
    /// </summary>
    /// <param name="function">读取功能码。</param>
    /// <param name="unitId">从站地址。</param>
    /// <param name="startAddress">0 基协议地址。</param>
    /// <param name="count">读取数量。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>操作结果。</returns>
    /// <exception cref="ModbusCommunicationException">参数或通信失败。</exception>
    public async Task<ModbusOperationResult> ReadAsync(
        ModbusFunction function,
        byte unitId,
        ushort startAddress,
        ushort count,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ValidateRead(function, unitId, startAddress, count);

        OperationExecution<object?> execution = await ExecuteSerializedAsync<object?>(
            unitId,
            function,
            master => (object?)function switch
            {
                ModbusFunction.ReadCoils => master.ReadCoils(unitId, startAddress, count),
                ModbusFunction.ReadDiscreteInputs => master.ReadInputs(unitId, startAddress, count),
                ModbusFunction.ReadHoldingRegisters =>
                    master.ReadHoldingRegisters(unitId, startAddress, count),
                ModbusFunction.ReadInputRegisters =>
                    master.ReadInputRegisters(unitId, startAddress, count),
                _ => throw new ArgumentOutOfRangeException(nameof(function), "功能码不是读取功能码。")
            },
            false,
            cancellationToken).ConfigureAwait(false);

        bool[]? coils = execution.Value as bool[];
        ushort[]? registers = execution.Value as ushort[];
        return new ModbusOperationResult
        {
            Function = function,
            UnitId = unitId,
            StartAddress = startAddress,
            Count = count,
            ElapsedMilliseconds = execution.ElapsedMilliseconds,
            Coils = coils,
            Registers = registers,
            RequestHex = execution.RequestHex,
            ResponseHex = execution.ResponseHex,
            ParsedValue = coils is not null
                ? string.Join(", ", coils.Select(value => value ? "1" : "0"))
                : string.Join(", ", registers?.Select(value => $"0x{value:X4}") ?? Array.Empty<string>())
        };
    }

    /// <summary>
    /// 写入单线圈。
    /// </summary>
    /// <param name="unitId">从站地址，0 表示广播。</param>
    /// <param name="address">0 基协议地址。</param>
    /// <param name="value">线圈值。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>操作结果。</returns>
    public Task<ModbusOperationResult> WriteSingleCoilAsync(
        byte unitId,
        ushort address,
        bool value,
        CancellationToken cancellationToken = default)
    {
        return WriteAsync(
            ModbusFunction.WriteSingleCoil,
            unitId,
            address,
            new[] { value },
            Array.Empty<ushort>(),
            cancellationToken);
    }

    /// <summary>
    /// 写入单寄存器。
    /// </summary>
    /// <param name="unitId">从站地址，0 表示广播。</param>
    /// <param name="address">0 基协议地址。</param>
    /// <param name="value">寄存器值。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>操作结果。</returns>
    public Task<ModbusOperationResult> WriteSingleRegisterAsync(
        byte unitId,
        ushort address,
        ushort value,
        CancellationToken cancellationToken = default)
    {
        return WriteAsync(
            ModbusFunction.WriteSingleRegister,
            unitId,
            address,
            Array.Empty<bool>(),
            new[] { value },
            cancellationToken);
    }

    /// <summary>
    /// 写入多线圈。
    /// </summary>
    /// <param name="unitId">从站地址，0 表示广播。</param>
    /// <param name="startAddress">0 基协议地址。</param>
    /// <param name="values">线圈值。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>操作结果。</returns>
    public Task<ModbusOperationResult> WriteMultipleCoilsAsync(
        byte unitId,
        ushort startAddress,
        bool[] values,
        CancellationToken cancellationToken = default)
    {
        return WriteAsync(
            ModbusFunction.WriteMultipleCoils,
            unitId,
            startAddress,
            values,
            Array.Empty<ushort>(),
            cancellationToken);
    }

    /// <summary>
    /// 写入多寄存器。
    /// </summary>
    /// <param name="unitId">从站地址，0 表示广播。</param>
    /// <param name="startAddress">0 基协议地址。</param>
    /// <param name="values">寄存器值。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>操作结果。</returns>
    public Task<ModbusOperationResult> WriteMultipleRegistersAsync(
        byte unitId,
        ushort startAddress,
        ushort[] values,
        CancellationToken cancellationToken = default)
    {
        return WriteAsync(
            ModbusFunction.WriteMultipleRegisters,
            unitId,
            startAddress,
            Array.Empty<bool>(),
            values,
            cancellationToken);
    }

    /// <summary>
    /// 按写入功能码执行一次写入。
    /// </summary>
    /// <param name="function">写入功能码。</param>
    /// <param name="unitId">从站地址，0 表示广播。</param>
    /// <param name="startAddress">0 基协议地址。</param>
    /// <param name="coils">线圈数据。</param>
    /// <param name="registers">寄存器数据。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>操作结果。</returns>
    /// <exception cref="ModbusCommunicationException">参数或通信失败。</exception>
    public async Task<ModbusOperationResult> WriteAsync(
        ModbusFunction function,
        byte unitId,
        ushort startAddress,
        bool[] coils,
        ushort[] registers,
        CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(coils);
        ArgumentNullException.ThrowIfNull(registers);

        int count = function switch
        {
            ModbusFunction.WriteSingleCoil or ModbusFunction.WriteMultipleCoils => coils.Length,
            ModbusFunction.WriteSingleRegister or ModbusFunction.WriteMultipleRegisters =>
                registers.Length,
            _ => 0
        };
        ValidateWrite(function, unitId, startAddress, count);
        bool broadcast = unitId == ProtocolLimits.BroadcastUnitId;

        OperationExecution<object?> execution = await ExecuteSerializedAsync<object?>(
            unitId,
            function,
            master =>
            {
                switch (function)
                {
                    case ModbusFunction.WriteSingleCoil when broadcast:
                        master.Transport.BroadcastWrite(
                            new WriteSingleCoilRequestResponse(0, startAddress, coils[0]));
                        break;
                    case ModbusFunction.WriteSingleCoil:
                        master.WriteSingleCoil(unitId, startAddress, coils[0]);
                        break;
                    case ModbusFunction.WriteSingleRegister when broadcast:
                        master.BroadcastWriteSingleRegister(startAddress, registers[0]);
                        break;
                    case ModbusFunction.WriteSingleRegister:
                        master.WriteSingleRegister(unitId, startAddress, registers[0]);
                        break;
                    case ModbusFunction.WriteMultipleCoils when broadcast:
                        master.Transport.BroadcastWrite(
                            new WriteMultipleCoilsRequest(
                                0,
                                startAddress,
                                new DiscreteCollection(coils)));
                        break;
                    case ModbusFunction.WriteMultipleCoils:
                        master.WriteMultipleCoils(unitId, startAddress, coils);
                        break;
                    case ModbusFunction.WriteMultipleRegisters when broadcast:
                        master.Transport.BroadcastWrite(
                            new WriteMultipleRegistersRequest(
                                0,
                                startAddress,
                                new RegisterCollection(registers)));
                        break;
                    case ModbusFunction.WriteMultipleRegisters:
                        master.WriteMultipleRegisters(unitId, startAddress, registers);
                        break;
                    default:
                        throw new ArgumentOutOfRangeException(nameof(function), "功能码不是写入功能码。");
                }

                return null;
            },
            broadcast,
            cancellationToken).ConfigureAwait(false);

        return new ModbusOperationResult
        {
            Function = function,
            UnitId = unitId,
            StartAddress = startAddress,
            Count = checked((ushort)count),
            ElapsedMilliseconds = execution.ElapsedMilliseconds,
            IsBroadcast = broadcast,
            RequestHex = execution.RequestHex,
            ResponseHex = execution.ResponseHex,
            ParsedValue = broadcast ? "广播写已发送，未等待响应" : "写入成功"
        };
    }

    private async Task ConnectRtuAsync(CancellationToken cancellationToken)
    {
        RtuConnectionConfiguration rtu = _configuration.Rtu;
        SerialPort serialPort = new(
            rtu.PortName,
            rtu.BaudRate,
            MapParity(rtu.Parity),
            rtu.DataBits,
            MapStopBits(rtu.StopBits))
        {
            ReadTimeout = _configuration.TransportTimeoutMilliseconds,
            WriteTimeout = _configuration.TransportTimeoutMilliseconds
        };

        await Task.Run(serialPort.Open, cancellationToken).ConfigureAwait(false);
        _serialPort = serialPort;
        _master = _factory!.CreateRtuMaster(serialPort);
    }

    private async Task ConnectTcpAsync(CancellationToken cancellationToken)
    {
        TcpClient client = new()
        {
            NoDelay = true,
            ReceiveTimeout = _configuration.TransportTimeoutMilliseconds,
            SendTimeout = _configuration.TransportTimeoutMilliseconds
        };

        using CancellationTokenSource timeoutSource =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(_configuration.Tcp.ConnectTimeoutMilliseconds);
        try
        {
            await client.ConnectAsync(
                _configuration.Tcp.Host,
                _configuration.Tcp.Port,
                timeoutSource.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            client.Dispose();
            throw new TimeoutException("TCP 建立连接超时。");
        }
        catch
        {
            client.Dispose();
            throw;
        }

        _tcpClient = client;
        _master = _factory!.CreateMaster(client);
    }

    private void ConfigureTransport()
    {
        if (_master is null)
        {
            throw new InvalidOperationException("主站实例尚未创建。");
        }

        _master.Transport.Retries = _configuration.RetryCount;
        _master.Transport.WaitToRetryMilliseconds = _configuration.RetryDelayMilliseconds;
        _master.Transport.ReadTimeout = _configuration.TransportTimeoutMilliseconds;
        _master.Transport.WriteTimeout = _configuration.TransportTimeoutMilliseconds;
    }

    private async Task<OperationExecution<T>> ExecuteSerializedAsync<T>(
        byte unitId,
        ModbusFunction function,
        Func<IModbusMaster, T> operation,
        bool broadcast,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        if (!IsConnected)
        {
            throw new ModbusCommunicationException(
                "尚未连接从站",
                CommunicationErrorKind.Connection,
                unitId,
                (byte)function,
                null,
                null,
                "请先建立连接，再执行读写或轮询。");
        }

        await _operationLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        List<RawFrameRecord> frames = new();
        _activeFrames = frames;
        Stopwatch stopwatch = Stopwatch.StartNew();
        try
        {
            IModbusMaster master = _master!;
            Task<T> invocationTask = Task.Run(
                () =>
                {
                    try
                    {
                        return operation(master);
                    }
                    catch (Exception exception)
                    {
                        throw ModbusExceptionTranslator.Translate(
                            exception,
                            function.GetChineseName(),
                            unitId,
                            (byte)function);
                    }
                },
                CancellationToken.None);

            T value;
            try
            {
                value = broadcast
                    ? await invocationTask.ConfigureAwait(false)
                    : await invocationTask.WaitAsync(
                        TimeSpan.FromMilliseconds(
                            _configuration.ApplicationResponseTimeoutMilliseconds),
                        cancellationToken).ConfigureAwait(false);
            }
            catch (TimeoutException exception) when (!invocationTask.IsCompleted)
            {
                ObserveFault(invocationTask);
                MarkFault("应用层应答超时");
                ModbusCommunicationException timeoutException = new(
                    $"{function.GetChineseName()}应用层应答超时",
                    CommunicationErrorKind.Timeout,
                    unitId,
                    (byte)function,
                    null,
                    null,
                    "从站在应用层应答超时内未返回完整响应；检查从站处理时间，并分别核对传输层超时和应用层应答超时。",
                    exception);
                AttachFrames(timeoutException, frames);
                throw timeoutException;
            }

            stopwatch.Stop();
            return new OperationExecution<T>(
                value,
                stopwatch.ElapsedMilliseconds,
                JoinFrames(frames, DataDirection.Tx),
                JoinFrames(frames, DataDirection.Rx));
        }
        catch (ModbusCommunicationException exception)
        {
            stopwatch.Stop();
            AttachFrames(exception, frames);
            if (exception.ErrorKind is CommunicationErrorKind.Timeout
                or CommunicationErrorKind.Io
                or CommunicationErrorKind.Connection
                or CommunicationErrorKind.PortBusy)
            {
                MarkFault(exception.Message);
            }

            throw;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            ModbusCommunicationException translated = ModbusExceptionTranslator.Translate(
                exception,
                function.GetChineseName(),
                unitId,
                (byte)function);
            AttachFrames(translated, frames);
            throw translated;
        }
        finally
        {
            _activeFrames = null;
            _operationLock.Release();
        }
    }

    private static void ValidateRead(
        ModbusFunction function,
        byte unitId,
        ushort startAddress,
        int count)
    {
        ValidationResult result = new();
        Merge(result, ModbusRequestValidator.ValidateReadUnitId(unitId));
        Merge(result, ModbusRequestValidator.ValidateReadCount(function, count));
        Merge(result, ModbusRequestValidator.ValidateAddressRange(startAddress, count));
        ThrowIfInvalid(result, unitId, function);
    }

    private void ValidateWrite(
        ModbusFunction function,
        byte unitId,
        ushort startAddress,
        int count)
    {
        ValidationResult result = new();
        Merge(result, ModbusRequestValidator.ValidateWriteUnitId(unitId, _configuration.Type));
        Merge(result, ModbusRequestValidator.ValidateWriteCount(function, count));
        Merge(result, ModbusRequestValidator.ValidateAddressRange(startAddress, count));
        ThrowIfInvalid(result, unitId, function);
    }

    private static void Merge(ValidationResult target, ValidationResult source)
    {
        foreach (string error in source.Errors)
        {
            target.AddError(error);
        }

        foreach (string warning in source.Warnings)
        {
            target.AddWarning(warning);
        }
    }

    private static void ThrowIfInvalid(
        ValidationResult result,
        byte unitId,
        ModbusFunction function)
    {
        if (result.IsValid)
        {
            return;
        }

        throw new ModbusCommunicationException(
            result.GetErrorMessage(),
            CommunicationErrorKind.Validation,
            unitId,
            (byte)function,
            null,
            null,
            "修正从站地址、功能码、起始地址或数量后重试。");
    }

    private void OnFrameCaptured(RawFrameRecord frame)
    {
        if (_activeFrames is { } frames)
        {
            lock (frames)
            {
                frames.Add(frame);
            }
        }

        FrameCaptured?.Invoke(frame);
    }

    private static void AttachFrames(
        ModbusCommunicationException exception,
        IReadOnlyList<RawFrameRecord> frames)
    {
        exception.RequestHex = JoinFrames(frames, DataDirection.Tx);
        exception.ResponseHex = JoinFrames(frames, DataDirection.Rx);
    }

    private static string JoinFrames(
        IReadOnlyList<RawFrameRecord> frames,
        DataDirection direction)
    {
        lock (frames)
        {
            return string.Join(
                " | ",
                frames.Where(frame => frame.Direction == direction).Select(frame => frame.Hex));
        }
    }

    private void MarkFault(string message)
    {
        _configuration.IsConnected = false;
        _configuration.RuntimeState = ConnectionState.Faulted;
        ChangeState(
            ConnectionState.Faulted,
            string.IsNullOrWhiteSpace(message) ? "通信故障" : message);
        lock (_resourceLock)
        {
            DisposeTransportResources();
        }
    }

    private void DisposeTransportResources()
    {
        lock (_resourceLock)
        {
            try
            {
                _master?.Dispose();
            }
            catch (Exception)
            {
                // 释放主站时忽略底层资源已断开的异常。
            }
            finally
            {
                _master = null;
            }

            try
            {
                _serialPort?.Dispose();
            }
            catch (Exception)
            {
                // 重复释放串口必须安全。
            }
            finally
            {
                _serialPort = null;
            }

            try
            {
                _tcpClient?.Dispose();
            }
            catch (Exception)
            {
                // 重复释放 Socket 必须安全。
            }
            finally
            {
                _tcpClient = null;
            }

            _factory = null;
        }
    }

    private void ChangeState(ConnectionState state, string message)
    {
        _state = state;
        StateChanged?.Invoke(this, new ConnectionStateChangedEventArgs(state, message));
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

    private static void ObserveFault(Task task)
    {
        _ = task.ContinueWith(
            completed =>
            {
                _ = completed.Exception;
            },
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private void ThrowIfDisposed()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
    }

    /// <summary>
    /// 释放连接、主站和设备资源。
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        DisposeTransportResources();
        _frameLogger.FrameCaptured -= OnFrameCaptured;
        _operationLock.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed record OperationExecution<T>(
        T Value,
        long ElapsedMilliseconds,
        string RequestHex,
        string ResponseHex);
}
