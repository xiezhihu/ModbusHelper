using ModbusHelper.Core.Configuration;

using NModbus;
using NModbus.Message;

namespace ModbusHelper.Communication.Slaves;

/// <summary>
/// 从站功能服务装饰器。在真正执行请求前匹配异常规则，并借助 NModbus 的
/// InvalidModbusRequestException 生成标准异常响应帧。
/// </summary>
internal sealed class ExceptionInjectionFunctionService : IModbusFunctionService
{
    private readonly IModbusFunctionService _inner;
    private readonly ExceptionRuleProvider _ruleProvider;

    public ExceptionInjectionFunctionService(
        IModbusFunctionService inner,
        ExceptionRuleProvider ruleProvider)
    {
        _inner = inner;
        _ruleProvider = ruleProvider;
    }

    public byte FunctionCode => _inner.FunctionCode;

    public IModbusMessage CreateRequest(byte[] frame)
    {
        return _inner.CreateRequest(frame);
    }

    public IModbusMessage HandleSlaveRequest(
        IModbusMessage request,
        ISlaveDataStore dataStore)
    {
        ushort requestAddress = ExtractStartAddress(request.MessageFrame);
        ExceptionInjectionRule? rule = _ruleProvider.Find(
            request.SlaveAddress,
            FunctionCode,
            requestAddress);
        if (rule is not null)
        {
            throw new InvalidModbusRequestException(
                $"已命中地址 {requestAddress} 的异常注入规则。",
                rule.ExceptionCode);
        }

        return _inner.HandleSlaveRequest(request, dataStore);
    }

    public int GetRtuRequestBytesToRead(byte[] frameStart)
    {
        return _inner.GetRtuRequestBytesToRead(frameStart);
    }

    public int GetRtuResponseBytesToRead(byte[] frameStart)
    {
        return _inner.GetRtuResponseBytesToRead(frameStart);
    }

    private static ushort ExtractStartAddress(byte[] messageFrame)
    {
        if (messageFrame.Length < 4)
        {
            return 0;
        }

        return (ushort)((messageFrame[2] << 8) | messageFrame[3]);
    }
}

/// <summary>
/// 线程安全的异常注入规则提供器。
/// </summary>
internal sealed class ExceptionRuleProvider
{
    private readonly object _syncRoot = new();
    private ExceptionInjectionRule[] _rules = Array.Empty<ExceptionInjectionRule>();

    public void Update(IEnumerable<ExceptionInjectionRule> rules)
    {
        lock (_syncRoot)
        {
            _rules = rules
                .Where(rule => rule.Enabled)
                .Select(Clone)
                .ToArray();
        }
    }

    public ExceptionInjectionRule? Find(byte unitId, byte functionCode, ushort address)
    {
        lock (_syncRoot)
        {
            return _rules.FirstOrDefault(rule =>
                rule.UnitId == unitId
                && (byte)rule.Function == functionCode
                && address >= rule.StartAddress
                && address < rule.StartAddress + Math.Max(rule.Count, (ushort)1));
        }
    }

    private static ExceptionInjectionRule Clone(ExceptionInjectionRule rule)
    {
        return new ExceptionInjectionRule
        {
            Enabled = rule.Enabled,
            UnitId = rule.UnitId,
            Function = rule.Function,
            StartAddress = rule.StartAddress,
            Count = Math.Max(rule.Count, (ushort)1),
            ExceptionCode = rule.ExceptionCode
        };
    }
}
