using ModbusHelper.Business.Configuration;
using ModbusHelper.Communication.Exceptions;
using ModbusHelper.Communication.Validation;
using ModbusHelper.Core.Configuration;
using ModbusHelper.Core.Enums;
using ModbusHelper.Core.Exceptions;
using ModbusHelper.Core.Protocol;

int passed = 0;
int failed = 0;

await RunAsync("地址基准换算", TestAddressConversion);
await RunAsync("读数量边界", TestReadLimits);
await RunAsync("写数量边界", TestWriteLimits);
await RunAsync("从站地址规则", TestUnitIdRules);
await RunAsync("异常码中文映射", TestExceptionCodeMap);
await RunAsync("异常转换", TestExceptionTranslation);
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

static async Task TestConfigurationRoundTrip()
{
    string directory = Path.Combine(Path.GetTempPath(), "ModbusHelperVerification");
    Directory.CreateDirectory(directory);
    string path = Path.Combine(directory, $"config-{Guid.NewGuid():N}.json");

    try
    {
        JsonConfigurationStore store = new(path);
        AppConfiguration configuration = JsonConfigurationStore.CreateDefaultConfiguration();
        configuration.AddressBase = AddressBase.OneBased;
        configuration.Connection.Type = ConnectionType.Rtu;
        configuration.Connection.IsConnected = true;
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
        Equal(1, loaded.SlaveSimulator.Units[0].ExceptionRules.Count);
    }
    finally
    {
        if (File.Exists(path))
        {
            File.Delete(path);
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
