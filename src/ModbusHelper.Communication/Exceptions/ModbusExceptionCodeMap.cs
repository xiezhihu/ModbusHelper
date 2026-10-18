namespace ModbusHelper.Communication.Exceptions;

/// <summary>
/// Modbus 异常码中文说明。
/// </summary>
public static class ModbusExceptionCodeMap
{
    private static readonly Dictionary<byte, ExceptionCodeDefinition> Definitions =
        new Dictionary<byte, ExceptionCodeDefinition>
        {
            [0x01] = new("非法功能", "从站不支持该功能码，检查设备手册和功能码选择。"),
            [0x02] = new("非法数据地址", "检查地址基准、起始地址和读取数量是否超出设备存储区。"),
            [0x03] = new("非法数据值", "检查写入数量、字段长度和从站允许的数据范围。"),
            [0x04] = new("从站设备故障", "从站执行请求时发生内部故障，检查设备状态和诊断信息。"),
            [0x05] = new("确认", "从站已接收请求，正在处理，需要后续轮询读取结果。"),
            [0x06] = new("从站忙", "从站正在处理其他任务，请稍后重试或增大超时与重试间隔。"),
            [0x08] = new("存储奇偶性差错", "从站存储区校验异常，检查设备存储器和电池状态。"),
            [0x0A] = new("网关路径不可用", "检查网关配置、路由和下游设备网络连接。"),
            [0x0B] = new("网关目标设备无响应", "网关无法得到目标设备响应，检查目标从站地址和链路。")
        };

    /// <summary>
    /// 获取异常码说明。
    /// </summary>
    /// <param name="exceptionCode">Modbus 异常码。</param>
    /// <returns>中文说明。</returns>
    public static ExceptionCodeDefinition Get(byte exceptionCode)
    {
        return Definitions.TryGetValue(exceptionCode, out ExceptionCodeDefinition? definition)
            ? definition
            : new ExceptionCodeDefinition("未知异常码", "查阅设备手册确认该异常码含义。");
    }
}

/// <summary>
/// 异常码名称和建议。
/// </summary>
/// <param name="Meaning">中文含义。</param>
/// <param name="Hint">排查建议。</param>
public sealed record ExceptionCodeDefinition(string Meaning, string Hint);
