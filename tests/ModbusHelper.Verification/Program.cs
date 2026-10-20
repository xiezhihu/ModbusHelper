using System.Collections.Concurrent;
using System.IO.Ports;
using System.Net;
using System.Net.Sockets;

using ModbusHelper.Business.ByteOrder;
using ModbusHelper.Business.Configuration;
using ModbusHelper.Business.Connections;
using ModbusHelper.Business.Data;
using ModbusHelper.Business.Logging;
using ModbusHelper.Business.Polling;
using ModbusHelper.Business.Statistics;
using ModbusHelper.Business.Trends;
using ModbusHelper.Communication.Capturing;
using ModbusHelper.Communication.Exceptions;
using ModbusHelper.Communication.Masters;
using ModbusHelper.Communication.Slaves;
using ModbusHelper.Communication.Validation;
using ModbusHelper.Core.Configuration;
using ModbusHelper.Core.Enums;
using ModbusHelper.Core.Exceptions;
using ModbusHelper.Core.Models;
using ModbusHelper.Core.Protocol;

int passed = 0;
int failed = 0;

await RunAsync("地址基准换算", TestAddressConversion);
await RunAsync("读数量边界", TestReadLimits);
await RunAsync("写数量边界", TestWriteLimits);
await RunAsync("从站地址规则", TestUnitIdRules);
await RunAsync("重试预算提示", TestRetryBudgetWarning);
await RunAsync("异常码中文映射", TestExceptionCodeMap);
await RunAsync("异常转换", TestExceptionTranslation);
await RunAsync("帧日志解析", TestFrameCaptureLogger);
await RunAsync("寄存器四种布局", TestRegisterByteOrders);
await RunAsync("寄存器往返编码", TestRegisterRoundTrip);
await RunAsync("CSV 转义与 BOM", TestCsvExport);
await RunAsync("SPSC 环形缓冲区", TestSpscRingBuffer);
await RunAsync("通信统计", TestStatistics);
await RunAsync("TCP 主从自环", TestTcpSelfLoop);
await RunAsync("两层超时", TestTimeoutLayers);
int rtuArgumentIndex = Array.FindIndex(
    args,
    argument => string.Equals(argument, "--rtu", StringComparison.OrdinalIgnoreCase));
if (rtuArgumentIndex >= 0)
{
    string masterPort = args.Length > rtuArgumentIndex + 2
        ? args[rtuArgumentIndex + 1]
        : "COM1";
    string slavePort = args.Length > rtuArgumentIndex + 2
        ? args[rtuArgumentIndex + 2]
        : "COM2";
    await RunAsync(
        $"RTU 主从自环（{masterPort}/{slavePort}）",
        () => TestRtuSelfLoop(masterPort, slavePort));
}
else
{
    Console.WriteLine("提示：未指定 --rtu COM1 COM2，已跳过 RTU 串口专项验证。");
}
await RunAsync("配置保存与加载", TestConfigurationRoundTrip);

Console.WriteLine();
Console.WriteLine($"验证完成：通过 {passed} 项，失败 {failed} 项。");
return failed == 0 ? 0 : 1;

async Task RunAsync(string name, Func<Task> test)
{
    try
    {
        await test();
        passed++;
        Console.WriteLine($"通过：{name}");
    }
    catch (Exception exception)
    {
        failed++;
        Console.WriteLine($"失败：{name} -> {exception.Message}");
    }
}

static Task TestAddressConversion()
{
    Equal((ushort)0, ModbusAddressConverter.ToProtocolAddress(
        40001,
        AddressBase.OneBased,
        ModbusFunction.ReadHoldingRegisters));
    Equal((ushort)0, ModbusAddressConverter.ToProtocolAddress(
        30001,
        AddressBase.OneBased,
        ModbusFunction.ReadInputRegisters));
    Equal((ushort)0, ModbusAddressConverter.ToProtocolAddress(
        10001,
        AddressBase.OneBased,
        ModbusFunction.ReadDiscreteInputs));
    Equal((ushort)0, ModbusAddressConverter.ToProtocolAddress(
        1,
        AddressBase.OneBased,
        ModbusFunction.ReadCoils));
    Equal((ushort)0, ModbusAddressConverter.ToProtocolAddress(
        1,
        AddressBase.OneBased,
        ModbusFunction.WriteSingleCoil));
    Equal((ushort)0, ModbusAddressConverter.ToProtocolAddress(
        40001,
        AddressBase.OneBased,
        ModbusFunction.WriteSingleRegister));
    Equal((ushort)0, ModbusAddressConverter.ToProtocolAddress(
        40001,
        AddressBase.OneBased,
        ModbusFunction.WriteMultipleRegisters));
    Equal(40001, ModbusAddressConverter.ToDisplayAddress(
        0,
        AddressBase.OneBased,
        ModbusFunction.ReadHoldingRegisters));
    Equal(65535, ModbusAddressConverter.ToProtocolAddress(
        65535,
        AddressBase.ZeroBased,
        ModbusFunction.ReadHoldingRegisters));
    return Task.CompletedTask;
}

static Task TestReadLimits()
{
    True(ModbusRequestValidator.ValidateReadCount(
        ModbusFunction.ReadHoldingRegisters,
        125).IsValid);
    False(ModbusRequestValidator.ValidateReadCount(
        ModbusFunction.ReadHoldingRegisters,
        126).IsValid);
    True(ModbusRequestValidator.ValidateReadCount(
        ModbusFunction.ReadCoils,
        2000).IsValid);
    False(ModbusRequestValidator.ValidateReadCount(
        ModbusFunction.ReadCoils,
        2001).IsValid);
    return Task.CompletedTask;
}

static Task TestWriteLimits()
{
    True(ModbusRequestValidator.ValidateWriteCount(
        ModbusFunction.WriteMultipleRegisters,
        123).IsValid);
    False(ModbusRequestValidator.ValidateWriteCount(
        ModbusFunction.WriteMultipleRegisters,
        124).IsValid);
    True(ModbusRequestValidator.ValidateWriteCount(
        ModbusFunction.WriteMultipleCoils,
        1968).IsValid);
    False(ModbusRequestValidator.ValidateWriteCount(
        ModbusFunction.WriteMultipleCoils,
        1969).IsValid);
    return Task.CompletedTask;
}

static Task TestUnitIdRules()
{
    False(ModbusRequestValidator.ValidateReadUnitId(0).IsValid);
    True(ModbusRequestValidator.ValidateReadUnitId(247).IsValid);
    False(ModbusRequestValidator.ValidateReadUnitId(248).IsValid);
    True(ModbusRequestValidator.ValidateWriteUnitId(0, ConnectionType.Rtu).HasWarnings);
    True(ModbusRequestValidator.ValidateWriteUnitId(248, ConnectionType.Rtu).HasWarnings);
    True(ModbusRequestValidator.ValidateWriteUnitId(255, ConnectionType.Tcp).HasWarnings);
    return Task.CompletedTask;
}

static Task TestRetryBudgetWarning()
{
    ConnectionParameterValidatorResult(
        transportTimeout: 1000,
        applicationTimeout: 1500,
        retryCount: 3,
        retryDelay: 100,
        expectedWarning: true);
    ConnectionParameterValidatorResult(
        transportTimeout: 1000,
        applicationTimeout: 4300,
        retryCount: 3,
        retryDelay: 100,
        expectedWarning: false);
    return Task.CompletedTask;
}

static void ConnectionParameterValidatorResult(
    int transportTimeout,
    int applicationTimeout,
    int retryCount,
    int retryDelay,
    bool expectedWarning)
{
    ValidationResult result = ConnectionParameterValidator.Validate(
        new ConnectionConfiguration
        {
            Type = ConnectionType.Tcp,
            TransportTimeoutMilliseconds = transportTimeout,
            ApplicationResponseTimeoutMilliseconds = applicationTimeout,
            RetryCount = retryCount,
            RetryDelayMilliseconds = retryDelay,
            Tcp = new TcpConnectionConfiguration
            {
                Host = "127.0.0.1",
                Port = 502
            }
        });
    Equal(expectedWarning, result.Warnings.Any(
        warning => warning.Contains("全部重试", StringComparison.Ordinal)));
}

static Task TestExceptionCodeMap()
{
    Equal("非法数据地址", ModbusExceptionCodeMap.Get(0x02).Meaning);
    True(ModbusExceptionCodeMap.Get(0x05).Hint.Contains("后续轮询", StringComparison.Ordinal));
    Equal("网关目标设备无响应", ModbusExceptionCodeMap.Get(0x0B).Meaning);
    return Task.CompletedTask;
}

static Task TestExceptionTranslation()
{
    ModbusCommunicationException exception = ModbusExceptionTranslator.Translate(
        new TimeoutException("模拟超时"),
        "读取保持寄存器",
        1,
        3);
    Equal(CommunicationErrorKind.Timeout, exception.ErrorKind);
    True(exception.GetDisplayMessage().Contains("超时", StringComparison.Ordinal));
    return Task.CompletedTask;
}

static Task TestFrameCaptureLogger()
{
    FrameCaptureLogger logger = new(ModbusRole.Slave, false);
    RawFrameRecord? captured = null;
    logger.FrameCaptured += frame => captured = frame;
    logger.Log(
        NModbus.LoggingLevel.Information,
        "TX to Master at 127.0.0.1:502: 0, 1, 0, 0, 0, 5, 1, 3, 2, 0, 1");
    True(captured is not null, "TCP 从站发送帧未被解析。");
    Equal("00 01 00 00 00 05 01 03 02 00 01", captured!.Hex);
    return Task.CompletedTask;
}

static Task TestRegisterByteOrders()
{
    ushort[] registers = { 0x1234, 0x5678 };
    Equal(0x12345678u, (uint)RegisterCodec.Decode(
        registers,
        DataValueType.UInt32,
        RegisterByteOrder.ABCD));
    Equal(0x56781234u, (uint)RegisterCodec.Decode(
        registers,
        DataValueType.UInt32,
        RegisterByteOrder.CDAB));
    Equal(0x34127856u, (uint)RegisterCodec.Decode(
        registers,
        DataValueType.UInt32,
        RegisterByteOrder.BADC));
    Equal(0x78563412u, (uint)RegisterCodec.Decode(
        registers,
        DataValueType.UInt32,
        RegisterByteOrder.DCBA));
    return Task.CompletedTask;
}

static Task TestRegisterRoundTrip()
{
    foreach (RegisterByteOrder order in Enum.GetValues<RegisterByteOrder>())
    {
        float expected = 123.456f;
        ushort[] registers = RegisterCodec.Encode(expected, DataValueType.Float, order);
        float actual = (float)RegisterCodec.Decode(registers, DataValueType.Float, order);
        Equal(expected, actual);
    }

    ushort[] textRegisters = RegisterCodec.Encode(
        "工业调试",
        DataValueType.Utf8,
        RegisterByteOrder.BADC);
    Equal(
        "工业调试",
        (string)RegisterCodec.Decode(
            textRegisters,
            DataValueType.Utf8,
            RegisterByteOrder.BADC));
    return Task.CompletedTask;
}

static async Task TestCsvExport()
{
    Equal("\"a,b\"", ModbusLogService.EscapeCsvField("a,b"));
    Equal("\"a\"\"b\"", ModbusLogService.EscapeCsvField("a\"b"));
    Equal("\"第一行\r\n第二行\"", ModbusLogService.EscapeCsvField("第一行\r\n第二行"));

    string path = Path.Combine(Path.GetTempPath(), $"modbus-log-{Guid.NewGuid():N}.csv");
    try
    {
        ModbusLogService log = new();
        log.AddMessage(ModbusLogLevel.Info, "含逗号,和\"引号\"");
        await log.ExportCsvAsync(path);
        byte[] bytes = await File.ReadAllBytesAsync(path);
        True(bytes.Length > 3);
        Equal((byte)0xEF, bytes[0]);
        Equal((byte)0xBB, bytes[1]);
        Equal((byte)0xBF, bytes[2]);
    }
    finally
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}

static async Task TestSpscRingBuffer()
{
    SpscRingBuffer<TestSample> buffer = new(128);
    const int total = 50_000;
    long cursor = 0;
    int observed = 0;

    Task producer = Task.Run(() =>
    {
        for (int index = 1; index <= total; index++)
        {
            buffer.Write(new TestSample(index, index * 1.5));
        }
    });

    Task consumer = Task.Run(() =>
    {
        while (!producer.IsCompleted)
        {
            cursor = buffer.ReadAfter(cursor, out TestSample[] values);
            foreach (TestSample value in values)
            {
                True(value.Value == value.Sequence * 1.5, "读取到撕裂数据。");
                observed++;
            }
        }
    });

    await Task.WhenAll(producer, consumer);
    cursor = buffer.ReadAfter(cursor, out TestSample[] remaining);
    foreach (TestSample value in remaining)
    {
        True(value.Value == value.Sequence * 1.5, "最终快照存在撕裂数据。");
        observed++;
    }

    True(observed > 0);
    Equal((long)total, buffer.LatestSequence);
}

static Task TestStatistics()
{
    CommunicationStatistics statistics = new();
    statistics.RecordFrame(new RawFrameRecord(
        DateTimeOffset.Now,
        ModbusRole.Master,
        DataDirection.Tx,
        "01 03 00 00 00 01",
        "主站发送",
        true));
    statistics.RecordFrame(new RawFrameRecord(
        DateTimeOffset.Now,
        ModbusRole.Master,
        DataDirection.Rx,
        "01 03 02 00 01",
        "主站接收",
        true));
    statistics.RecordOperation(12, null);
    statistics.RecordOperation(20, CommunicationErrorKind.Timeout);
    CommunicationStatisticsSnapshot snapshot = statistics.GetSnapshot();
    Equal(1L, snapshot.Requests);
    Equal(1L, snapshot.Responses);
    Equal(2L, snapshot.Operations);
    Equal(1L, snapshot.Errors);
    Equal(1L, snapshot.Timeouts);
    Equal(16d, snapshot.AverageDurationMilliseconds);
    return Task.CompletedTask;
}

static async Task TestTcpSelfLoop()
{
    int port = GetFreeTcpPort();
    string id = Guid.NewGuid().ToString("N")[..8];
    SlaveSimulationConfiguration slaveConfiguration = new()
    {
        Type = ConnectionType.Tcp,
        Tcp = new TcpSlaveConfiguration
        {
            ListenAddress = "127.0.0.1",
            Port = port
        },
        Units =
        {
            new SlaveUnitConfiguration
            {
                UnitId = 1,
                HoldingRegisters = Enumerable.Range(0, 125)
                    .Select(index => new RegisterInitialValue
                    {
                        Address = (ushort)index,
                        Value = (ushort)(1000 + index)
                    })
                    .ToList(),
                InputRegisters =
                {
                    new RegisterInitialValue { Address = 0, Value = 2222 }
                },
                Coils =
                {
                    new CoilInitialValue { Address = 0, Value = true },
                    new CoilInitialValue { Address = 1, Value = false },
                    new CoilInitialValue { Address = 2, Value = true }
                },
                DiscreteInputs =
                {
                    new CoilInitialValue { Address = 0, Value = true }
                }
            },
            new SlaveUnitConfiguration
            {
                UnitId = 2,
                HoldingRegisters =
                {
                    new RegisterInitialValue { Address = 0, Value = 7777 }
                }
            }
        }
    };

    await using ModbusSlaveSimulator simulator = new(slaveConfiguration);
    int slaveFrames = 0;
    simulator.FrameCaptured += _ => Interlocked.Increment(ref slaveFrames);
    await simulator.StartAsync(slaveConfiguration);
    await Task.Delay(150);

    ConnectionConfiguration masterConfiguration = new()
    {
        Name = $"自环-{id}",
        Type = ConnectionType.Tcp,
        Tcp = new TcpConnectionConfiguration
        {
            Host = "127.0.0.1",
            Port = port,
            ConnectTimeoutMilliseconds = 3000
        },
        TransportTimeoutMilliseconds = 1000,
        ApplicationResponseTimeoutMilliseconds = 1500,
        RetryCount = 0
    };

    using ModbusService service = new(masterConfiguration);
    int masterFrames = 0;
    service.FrameCaptured += _ => Interlocked.Increment(ref masterFrames);
    await service.ConnectAsync();

    ModbusOperationResult registers = await service.ReadHoldingRegistersAsync(1, 0, 125);
    Equal(125, registers.Registers?.Length ?? -1);
    Equal((ushort)1000, registers.Registers![0]);
    Equal((ushort)1124, registers.Registers[124]);
    True(registers.RequestHex.Length > 0, "主站请求报文未采集。");
    True(registers.ResponseHex.Length > 0, "主站响应报文未采集。");

    try
    {
        await service.ReadHoldingRegistersAsync(1, 0, 126);
        throw new InvalidOperationException("126 个寄存器应被参数校验拦截。");
    }
    catch (ModbusCommunicationException exception)
    {
        Equal(CommunicationErrorKind.Validation, exception.ErrorKind);
    }

    ModbusOperationResult coils = await service.ReadCoilsAsync(1, 0, 3);
    Equal(3, coils.Coils?.Length ?? -1);
    True(coils.Coils![0], "线圈地址 0 应为真。");
    False(coils.Coils[1], "线圈地址 1 应为假。");
    True(coils.Coils[2], "线圈地址 2 应为真。");

    await service.WriteSingleRegisterAsync(1, 10, 4321);
    ModbusOperationResult writtenRegister = await service.ReadHoldingRegistersAsync(1, 10, 1);
    Equal((ushort)4321, writtenRegister.Registers![0]);

    ushort[] expectedBlock = { 11, 22, 33 };
    await service.WriteMultipleRegistersAsync(1, 20, expectedBlock);
    ModbusOperationResult writtenBlock = await service.ReadHoldingRegistersAsync(1, 20, 3);
    True(writtenBlock.Registers!.SequenceEqual(expectedBlock), "多寄存器写入结果不一致。");

    bool[] expectedCoils = { true, false, true, true };
    await service.WriteMultipleCoilsAsync(1, 5, expectedCoils);
    ModbusOperationResult writtenCoils = await service.ReadCoilsAsync(1, 5, 4);
    True(writtenCoils.Coils!.SequenceEqual(expectedCoils), "多线圈写入结果不一致。");

    ModbusOperationResult unit2 = await service.ReadHoldingRegistersAsync(2, 0, 1);
    Equal((ushort)7777, unit2.Registers![0]);

    slaveConfiguration.Units[0].ExceptionRules.Add(new ExceptionInjectionRule
    {
        UnitId = 1,
        Function = ModbusFunction.ReadHoldingRegisters,
        StartAddress = 50,
        Count = 3,
        ExceptionCode = 2
    });
    simulator.UpdateExceptionRules(slaveConfiguration.Units[0].ExceptionRules);
    try
    {
        await service.ReadHoldingRegistersAsync(1, 49, 2);
        throw new InvalidOperationException("异常注入规则未生效。");
    }
    catch (ModbusCommunicationException exception)
    {
        Equal(CommunicationErrorKind.SlaveException, exception.ErrorKind);
        True(exception.ModbusExceptionCode == 2);
        True(string.Equals(
            "非法数据地址",
            exception.ModbusExceptionMeaning,
            StringComparison.Ordinal));
        True(exception.GetDisplayMessage().Contains("非法数据地址", StringComparison.Ordinal));
    }

    True(masterFrames >= 10, $"主站报文数不足，实际 {masterFrames}。");
    True(slaveFrames >= 10, $"从站报文数不足，实际 {slaveFrames}。");
    await service.DisconnectAsync();
    Equal(ConnectionState.Disconnected, service.State);
    await service.ConnectAsync();
    ModbusOperationResult reconnectResult = await service.ReadHoldingRegistersAsync(1, 0, 1);
    Equal((ushort)1000, reconnectResult.Registers![0]);
    await service.DisconnectAsync();
    service.Dispose();

    ModbusLogService logService = new();
    await using ConnectionManager connectionManager = new(logService);
    await connectionManager.ConnectAsync(masterConfiguration);
    TrendDataStore trendStore = new();
    await using PollingScheduler scheduler = new(connectionManager, trendStore, logService);
    ConcurrentDictionary<string, int> sampleCounts = new();
    scheduler.SampleReceived += (_, args) =>
        sampleCounts.AddOrUpdate(args.Task.Id, 1, (_, count) => count + 1);

    PollingTaskConfiguration task1 = new()
    {
        Id = $"task-{id}-1",
        Name = "寄存器轮询",
        Function = ModbusFunction.ReadHoldingRegisters,
        UnitId = 1,
        StartAddress = 0,
        Count = 1,
        IntervalMilliseconds = 100,
        DataType = DataValueType.UInt16,
        ShowInTrend = true
    };
    PollingTaskConfiguration task2 = new()
    {
        Id = $"task-{id}-2",
        Name = "线圈轮询",
        Function = ModbusFunction.ReadCoils,
        UnitId = 1,
        StartAddress = 0,
        Count = 3,
        IntervalMilliseconds = 100,
        ShowInTrend = true
    };
    PollingTaskConfiguration task3 = new()
    {
        Id = $"task-{id}-3",
        Name = "寄存器轮询 3",
        Function = ModbusFunction.ReadHoldingRegisters,
        UnitId = 1,
        StartAddress = 1,
        Count = 1,
        IntervalMilliseconds = 100,
        DataType = DataValueType.UInt16,
        ShowInTrend = true
    };
    PollingTaskConfiguration task4 = new()
    {
        Id = $"task-{id}-4",
        Name = "寄存器轮询 4",
        Function = ModbusFunction.ReadHoldingRegisters,
        UnitId = 1,
        StartAddress = 2,
        Count = 1,
        IntervalMilliseconds = 100,
        DataType = DataValueType.UInt16,
        ShowInTrend = true
    };
    PollingTaskConfiguration task5 = new()
    {
        Id = $"task-{id}-5",
        Name = "寄存器轮询 5",
        Function = ModbusFunction.ReadHoldingRegisters,
        UnitId = 1,
        StartAddress = 3,
        Count = 1,
        IntervalMilliseconds = 100,
        DataType = DataValueType.UInt16,
        ShowInTrend = true
    };
    PollingTaskConfiguration[] pollingTasks =
    {
        task1,
        task2,
        task3,
        task4,
        task5
    };
    await scheduler.StartEnabledAsync(pollingTasks);
    await Task.Delay(650);
    await scheduler.StopAllAsync();
    foreach (PollingTaskConfiguration task in pollingTasks)
    {
        True(
            sampleCounts.GetValueOrDefault(task.Id) >= 2,
            $"五任务并行轮询“{task.Name}”采样不足。");
        True(
            trendStore.Get(task.Id)!.LatestSequence >= 2,
            $"趋势任务“{task.Name}”未收到数据。");
    }

    await connectionManager.WriteAsync(
        ModbusFunction.WriteMultipleRegisters,
        1,
        100,
        Array.Empty<bool>(),
        new ushort[] { 0x1234, 0x5678 });
    ByteOrderProbeService probeService = new(connectionManager);
    const uint expectedCdab = 0x56781234;
    ByteOrderProbeReport probeReport = await probeService.ProbeAsync(
        1,
        100,
        DataValueType.UInt32,
        expectedCdab,
        expectedCdab);
    True(probeReport.RecommendedByteOrder == RegisterByteOrder.CDAB);
    AppConfiguration probeConfiguration = new();
    ByteOrderProbeService.Apply(
        probeConfiguration,
        probeReport,
        RegisterByteOrder.ABCD);
    Equal(RegisterByteOrder.CDAB, probeConfiguration.Parsing.ByteOrder);
    await connectionManager.DisconnectAsync();
}

static int GetFreeTcpPort()
{
    TcpListener listener = new(IPAddress.Loopback, 0);
    listener.Start();
    int port = ((IPEndPoint)listener.LocalEndpoint).Port;
    listener.Stop();
    return port;
}

static async Task TestTimeoutLayers()
{
    await VerifyTimeoutAsync(
        transportTimeoutMilliseconds: 200,
        applicationTimeoutMilliseconds: 1500,
        expectedMessage: "传输层超时");
    await VerifyTimeoutAsync(
        transportTimeoutMilliseconds: 1000,
        applicationTimeoutMilliseconds: 250,
        expectedMessage: "应用层应答超时");
}

static async Task VerifyTimeoutAsync(
    int transportTimeoutMilliseconds,
    int applicationTimeoutMilliseconds,
    string expectedMessage)
{
    int port = GetFreeTcpPort();
    using TcpListener listener = new(IPAddress.Loopback, port);
    listener.Start();
    using CancellationTokenSource serverCancellation = new();
    TcpClient? acceptedClient = null;
    Task serverTask = Task.Run(async () =>
    {
        acceptedClient = await listener.AcceptTcpClientAsync(serverCancellation.Token);
        await Task.Delay(5000, serverCancellation.Token);
    });

    ConnectionConfiguration configuration = new()
    {
        Type = ConnectionType.Tcp,
        Tcp = new TcpConnectionConfiguration
        {
            Host = "127.0.0.1",
            Port = port,
            ConnectTimeoutMilliseconds = 1000
        },
        TransportTimeoutMilliseconds = transportTimeoutMilliseconds,
        ApplicationResponseTimeoutMilliseconds = applicationTimeoutMilliseconds,
        RetryCount = 0
    };

    using ModbusService service = new(configuration);
    await service.ConnectAsync();
    try
    {
        await service.ReadHoldingRegistersAsync(1, 0, 1);
        throw new InvalidOperationException("无响应服务器不应返回读取结果。");
    }
    catch (ModbusCommunicationException exception)
    {
        Equal(CommunicationErrorKind.Timeout, exception.ErrorKind);
        True(
            exception.Message.Contains(expectedMessage, StringComparison.Ordinal),
            $"超时类型不正确：{exception.Message}");
        Equal(ConnectionState.Faulted, service.State);
    }
    finally
    {
        serverCancellation.Cancel();
        acceptedClient?.Dispose();
        listener.Stop();
        try
        {
            await serverTask;
        }
        catch (OperationCanceledException)
        {
            // 测试服务器正常取消。
        }
    }
}

static async Task TestRtuSelfLoop(string masterPortName, string slavePortName)
{
    string[] availablePorts = SerialPort.GetPortNames();
    True(
        availablePorts.Contains(masterPortName, StringComparer.OrdinalIgnoreCase),
        $"未找到主站串口 {masterPortName}。");
    True(
        availablePorts.Contains(slavePortName, StringComparer.OrdinalIgnoreCase),
        $"未找到从站串口 {slavePortName}。");

    SlaveSimulationConfiguration slaveConfiguration = new()
    {
        Type = ConnectionType.Rtu,
        Rtu = new RtuConnectionConfiguration
        {
            PortName = slavePortName,
            BaudRate = 9600,
            DataBits = 8,
            Parity = SerialParity.None,
            StopBits = SerialStopBits.One
        },
        Units =
        {
            new SlaveUnitConfiguration
            {
                UnitId = 1,
                HoldingRegisters = Enumerable.Range(0, 125)
                    .Select(index => new RegisterInitialValue
                    {
                        Address = (ushort)index,
                        Value = (ushort)(2000 + index)
                    })
                    .ToList(),
                Coils =
                {
                    new CoilInitialValue { Address = 0, Value = true },
                    new CoilInitialValue { Address = 1, Value = false },
                    new CoilInitialValue { Address = 2, Value = true }
                }
            },
            new SlaveUnitConfiguration
            {
                UnitId = 2,
                HoldingRegisters =
                {
                    new RegisterInitialValue { Address = 0, Value = 8888 }
                }
            }
        }
    };

    await using ModbusSlaveSimulator simulator = new(slaveConfiguration);
    ConcurrentBag<RawFrameRecord> slaveFrames = new();
    simulator.FrameCaptured += slaveFrames.Add;
    await simulator.StartAsync(slaveConfiguration);
    await Task.Delay(250);

    ConnectionConfiguration masterConfiguration = new()
    {
        Type = ConnectionType.Rtu,
        Rtu = new RtuConnectionConfiguration
        {
            PortName = masterPortName,
            BaudRate = 9600,
            DataBits = 8,
            Parity = SerialParity.None,
            StopBits = SerialStopBits.One
        },
        TransportTimeoutMilliseconds = 1000,
        ApplicationResponseTimeoutMilliseconds = 2500,
        RetryCount = 0
    };

    using ModbusService service = new(masterConfiguration);
    ConcurrentBag<RawFrameRecord> masterFrames = new();
    service.FrameCaptured += masterFrames.Add;
    await service.ConnectAsync();

    ModbusOperationResult registerResult = await service.ReadHoldingRegistersAsync(1, 0, 125);
    Equal(125, registerResult.Registers?.Length ?? -1);
    Equal((ushort)2000, registerResult.Registers![0]);
    Equal((ushort)2124, registerResult.Registers[124]);

    try
    {
        await service.ReadHoldingRegistersAsync(1, 0, 126);
        throw new InvalidOperationException("RTU 读取 126 个寄存器应被参数校验拦截。");
    }
    catch (ModbusCommunicationException exception)
    {
        Equal(CommunicationErrorKind.Validation, exception.ErrorKind);
    }

    await service.WriteSingleRegisterAsync(1, 10, 4321);
    ModbusOperationResult singleRegister = await service.ReadHoldingRegistersAsync(1, 10, 1);
    Equal((ushort)4321, singleRegister.Registers![0]);

    ushort[] expectedRegisters = { 11, 22, 33 };
    await service.WriteMultipleRegistersAsync(1, 20, expectedRegisters);
    ModbusOperationResult multipleRegisters = await service.ReadHoldingRegistersAsync(1, 20, 3);
    True(multipleRegisters.Registers!.SequenceEqual(expectedRegisters));

    ModbusOperationResult coils = await service.ReadCoilsAsync(1, 0, 3);
    True(coils.Coils!.SequenceEqual(new[] { true, false, true }));

    ModbusOperationResult unit2 = await service.ReadHoldingRegistersAsync(2, 0, 1);
    Equal((ushort)8888, unit2.Registers![0]);

    slaveConfiguration.Units[0].ExceptionRules.Add(new ExceptionInjectionRule
    {
        UnitId = 1,
        Function = ModbusFunction.ReadHoldingRegisters,
        StartAddress = 50,
        Count = 3,
        ExceptionCode = 2
    });
    simulator.UpdateExceptionRules(slaveConfiguration.Units[0].ExceptionRules);
    try
    {
        await service.ReadHoldingRegistersAsync(1, 49, 2);
        throw new InvalidOperationException("RTU 异常注入规则未生效。");
    }
    catch (ModbusCommunicationException exception)
    {
        Equal(CommunicationErrorKind.SlaveException, exception.ErrorKind);
        True(exception.ModbusExceptionCode == 2);
        True(string.Equals(
            "非法数据地址",
            exception.ModbusExceptionMeaning,
            StringComparison.Ordinal));
    }

    True(masterFrames.Count > 10);
    True(slaveFrames.Count > 10);
    True(masterFrames.All(frame => frame.ChecksumStatus != false), "主站捕获到 CRC 错误帧。");
    True(slaveFrames.All(frame => frame.ChecksumStatus != false), "从站捕获到 CRC 错误帧。");
    True(
        masterFrames.All(frame =>
            !frame.Meaning.Contains("短帧", StringComparison.Ordinal)
            && !frame.Meaning.Contains("功能码 0x", StringComparison.Ordinal)),
        "主站 RTU 帧含义解析错误。");
    True(
        slaveFrames.All(frame =>
            !frame.Meaning.Contains("短帧", StringComparison.Ordinal)
            && !frame.Meaning.Contains("功能码 0x", StringComparison.Ordinal)),
        "从站 RTU 帧含义解析错误。");

    await service.DisconnectAsync();
    Equal(ConnectionState.Disconnected, service.State);
    await AssertSerialPortReusableAsync(masterPortName);
    await service.ConnectAsync();
    ModbusOperationResult reconnectResult = await service.ReadHoldingRegistersAsync(1, 0, 1);
    Equal((ushort)2000, reconnectResult.Registers![0]);
    await service.DisconnectAsync();
    service.Dispose();
    await AssertSerialPortReusableAsync(masterPortName);

    await simulator.StopAsync();
    await AssertSerialPortReusableAsync(slavePortName);

    await simulator.StartAsync(slaveConfiguration);
    await Task.Delay(250);
    ModbusLogService logService = new();
    await using ConnectionManager connectionManager = new(logService);
    ConnectionConfiguration pollingConfiguration = new()
    {
        Type = ConnectionType.Rtu,
        Rtu = masterConfiguration.Rtu,
        TransportTimeoutMilliseconds = 400,
        ApplicationResponseTimeoutMilliseconds = 900,
        RetryCount = 0
    };
    await connectionManager.ConnectAsync(pollingConfiguration);
    TrendDataStore trendStore = new();
    await using PollingScheduler scheduler = new(connectionManager, trendStore, logService);
    TaskCompletionSource sampleReceived = new(TaskCreationOptions.RunContinuationsAsynchronously);
    TaskCompletionSource<PollingTaskState> terminalState =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    TaskCompletionSource<ConnectionState> connectionTerminal =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    scheduler.SampleReceived += (_, _) => sampleReceived.TrySetResult();
    scheduler.TaskStateChanged += (_, args) =>
    {
        if (args.State != PollingTaskState.Running)
        {
            terminalState.TrySetResult(args.State);
        }
    };
    connectionManager.StateChanged += (_, args) =>
    {
        if (args.State == ConnectionState.Faulted)
        {
            connectionTerminal.TrySetResult(args.State);
        }
    };

    PollingTaskConfiguration pollingTask = new()
    {
        Name = "RTU 断线测试",
        Function = ModbusFunction.ReadHoldingRegisters,
        UnitId = 1,
        StartAddress = 0,
        Count = 1,
        IntervalMilliseconds = 100,
        ShowInTrend = true
    };
    scheduler.StartTask(pollingTask);
    await sampleReceived.Task.WaitAsync(TimeSpan.FromSeconds(5));
    await simulator.StopAsync();
    await connectionTerminal.Task.WaitAsync(TimeSpan.FromSeconds(5));
    PollingTaskState stoppedState = await terminalState.Task.WaitAsync(TimeSpan.FromSeconds(5));
    True(stoppedState is PollingTaskState.Faulted or PollingTaskState.Stopped);
    False(pollingTask.IsRunning);

    await scheduler.DisposeAsync();
    await connectionManager.DisposeAsync();
    await AssertSerialPortReusableAsync(masterPortName);
    await AssertSerialPortReusableAsync(slavePortName);
}

static async Task AssertSerialPortReusableAsync(string portName)
{
    Exception? lastException = null;
    for (int attempt = 0; attempt < 10; attempt++)
    {
        try
        {
            using SerialPort port = new(portName);
            port.Open();
            return;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            lastException = exception;
            await Task.Delay(100);
        }
    }

    throw new InvalidOperationException($"{portName} 未被释放。", lastException);
}

static async Task TestConfigurationRoundTrip()
{
    string directory = Path.Combine(Path.GetTempPath(), "ModbusHelperVerification");
    Directory.CreateDirectory(directory);
    string name = $"config-{Guid.NewGuid():N}";
    string path = Path.Combine(directory, $"{name}.json");
    string databasePath = Path.Combine(directory, $"{name}.db");

    try
    {
        JsonConfigurationStore store = new(path);
        AppConfiguration configuration = JsonConfigurationStore.CreateDefaultConfiguration();
        configuration.AddressBase = AddressBase.OneBased;
        configuration.Connection.Type = ConnectionType.Rtu;
        configuration.Connection.IsConnected = true;
        configuration.TrendCapacity = 1234;
        configuration.Points.Add(new PointConfiguration
        {
            Name = "温度",
            UnitId = 1,
            Function = ModbusFunction.ReadHoldingRegisters,
            StartAddress = 10,
            Count = 2,
            DataType = DataValueType.Float,
            ShowInTrend = true
        });
        configuration.PollingTasks[0].IsRunning = true;
        configuration.SlaveSimulator.Units[0].ExceptionRules.Add(new ExceptionInjectionRule
        {
            UnitId = 1,
            Function = ModbusFunction.ReadHoldingRegisters,
            StartAddress = 0,
            ExceptionCode = 2
        });

        await store.SaveAsync(configuration);
        string json = await File.ReadAllTextAsync(path);
        False(json.Contains("isConnected", StringComparison.OrdinalIgnoreCase));
        False(json.Contains("isRunning", StringComparison.OrdinalIgnoreCase));
        True(json.Contains("\"rtu\"", StringComparison.Ordinal));

        AppConfiguration loaded = await store.LoadAsync();
        Equal(AddressBase.OneBased, loaded.AddressBase);
        Equal(ConnectionType.Rtu, loaded.Connection.Type);
        False(loaded.Connection.IsConnected);
        False(loaded.PollingTasks[0].IsRunning);
        Equal(1234, loaded.TrendCapacity);
        Equal("温度", loaded.Points[0].Name);
        Equal(1, loaded.SlaveSimulator.Units[0].ExceptionRules.Count);

        SqliteConfigurationStore sqliteStore = new(databasePath, path);
        AppConfiguration migrated = await sqliteStore.LoadAsync();
        Equal(AddressBase.OneBased, migrated.AddressBase);
        Equal(ConnectionType.Rtu, migrated.Connection.Type);
        Equal("温度", migrated.Points[0].Name);
        False(migrated.Connection.IsConnected);
        False(migrated.PollingTasks[0].IsRunning);

        migrated.Connection.IsConnected = true;
        migrated.PollingTasks[0].IsRunning = true;
        migrated.Points[0].Name = "压力";
        await sqliteStore.SaveAsync(migrated);
        AppConfiguration sqliteLoaded = await sqliteStore.LoadAsync();
        Equal("压力", sqliteLoaded.Points[0].Name);
        Equal(AddressBase.OneBased, sqliteLoaded.AddressBase);
        False(sqliteLoaded.Connection.IsConnected);
        False(sqliteLoaded.PollingTasks[0].IsRunning);
    }
    finally
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }

        if (File.Exists(databasePath))
        {
            File.Delete(databasePath);
        }
    }
}

static void True(bool value, string? message = null)
{
    if (!value)
    {
        throw new InvalidOperationException(message ?? "期望条件为真。");
    }
}

static void False(bool value, string? message = null)
{
    True(!value, message ?? "期望条件为假。");
}

static void Equal<T>(T expected, T actual)
    where T : notnull
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"期望 {expected}，实际 {actual}。");
    }
}

internal readonly record struct TestSample(long Sequence, double Value);
