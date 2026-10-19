using System.Net;

using ModbusHelper.Core.Configuration;
using ModbusHelper.Core.Enums;
using ModbusHelper.Core.Protocol;

namespace ModbusHelper.Communication.Validation;

/// <summary>
/// 连接配置参数校验。
/// </summary>
public static class ConnectionParameterValidator
{
    /// <summary>
    /// 校验主站连接配置。
    /// </summary>
    /// <param name="configuration">连接配置。</param>
    /// <returns>校验结果。</returns>
    public static ValidationResult Validate(ConnectionConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ValidationResult result = new();

        if (configuration.TransportTimeoutMilliseconds <= 0)
        {
            result.AddError("传输层超时必须大于 0 毫秒。");
        }

        if (configuration.ApplicationResponseTimeoutMilliseconds <= 0)
        {
            result.AddError("应用层应答超时必须大于 0 毫秒。");
        }

        if (configuration.RetryCount is < 0 or > 10)
        {
            result.AddError("重试次数必须为 0 至 10。");
        }

        if (configuration.RetryDelayMilliseconds < 0)
        {
            result.AddError("重试等待时间不能为负数。");
        }

        if (configuration.TransportTimeoutMilliseconds > 0
            && configuration.ApplicationResponseTimeoutMilliseconds > 0
            && configuration.RetryCount > 0
            && configuration.RetryDelayMilliseconds >= 0)
        {
            long retryBudget = (long)(configuration.RetryCount + 1)
                * configuration.TransportTimeoutMilliseconds
                + (long)configuration.RetryCount * configuration.RetryDelayMilliseconds;
            if (configuration.ApplicationResponseTimeoutMilliseconds < retryBudget)
            {
                result.AddWarning(
                    $"应用层应答超时为 {configuration.ApplicationResponseTimeoutMilliseconds} 毫秒，"
                    + $"小于完成 {configuration.RetryCount} 次重试所需的理论等待时间约 {retryBudget} 毫秒；"
                    + "应用层超时可能在全部重试完成前结束。");
            }
        }

        if (configuration.Type == ConnectionType.Rtu)
        {
            ValidateRtu(configuration.Rtu, result);
        }
        else
        {
            ValidateTcp(configuration.Tcp, result);
        }

        return result;
    }

    /// <summary>
    /// 校验从站模拟器监听配置。
    /// </summary>
    /// <param name="configuration">从站模拟配置。</param>
    /// <returns>校验结果。</returns>
    public static ValidationResult ValidateSlave(SlaveSimulationConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ValidationResult result = new();

        if (configuration.Units.Count == 0)
        {
            result.AddError("至少需要配置一个从站单元。");
        }

        List<byte> duplicateIds = configuration.Units
            .GroupBy(unit => unit.UnitId)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToList();

        foreach (byte duplicateId in duplicateIds)
        {
            result.AddError($"从站地址 {duplicateId} 重复配置。");
        }

        foreach (SlaveUnitConfiguration unit in configuration.Units)
        {
            ValidationResult unitResult = ModbusRequestValidator.ValidateSlaveUnitId(unit.UnitId);
            foreach (string error in unitResult.Errors)
            {
                result.AddError($"从站 {unit.UnitId}：{error}");
            }

            ValidateInitialValues(unit.HoldingRegisters, $"从站 {unit.UnitId} 保持寄存器", result);
            ValidateInitialValues(unit.InputRegisters, $"从站 {unit.UnitId} 输入寄存器", result);
            ValidateInitialValues(unit.Coils, $"从站 {unit.UnitId} 线圈", result);
            ValidateInitialValues(unit.DiscreteInputs, $"从站 {unit.UnitId} 离散输入", result);

            foreach (ExceptionInjectionRule rule in unit.ExceptionRules)
            {
                if (rule.UnitId != unit.UnitId)
                {
                    result.AddError(
                        $"从站 {unit.UnitId} 的异常规则使用了不匹配的从站地址 {rule.UnitId}。");
                }

                if (!IsSupportedExceptionCode(rule.ExceptionCode))
                {
                    result.AddError(
                        $"从站 {unit.UnitId} 的异常码 0x{rule.ExceptionCode:X2} 不受支持。");
                }

                if (rule.Count == 0)
                {
                    result.AddError($"从站 {unit.UnitId} 的异常规则数量必须大于 0。");
                }
                else if (!ModbusAddressConverter.IsRangeValid(rule.StartAddress, rule.Count))
                {
                    result.AddError($"从站 {unit.UnitId} 的异常规则地址范围越界。");
                }
            }
        }

        if (configuration.Type == ConnectionType.Rtu)
        {
            ValidateRtu(configuration.Rtu, result);
        }
        else
        {
            if (!IPAddress.TryParse(configuration.Tcp.ListenAddress, out _))
            {
                result.AddError("TCP 从站监听地址格式无效。");
            }

            ValidatePort(configuration.Tcp.Port, result);
        }

        return result;
    }

    private static void ValidateInitialValues<T>(
        IReadOnlyCollection<T> values,
        string areaName,
        ValidationResult result)
        where T : notnull
    {
        if (values.Count > ushort.MaxValue + 1)
        {
            result.AddError($"{areaName}初始值数量超过协议地址空间。");
        }
    }

    private static bool IsSupportedExceptionCode(byte exceptionCode)
    {
        return exceptionCode is 0x01 or 0x02 or 0x03 or 0x04 or 0x05 or 0x06 or 0x08 or 0x0A
            or 0x0B;
    }

    private static void ValidateRtu(RtuConnectionConfiguration rtu, ValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(rtu.PortName))
        {
            result.AddError("串口名称不能为空。");
        }

        if (rtu.BaudRate <= 0)
        {
            result.AddError("波特率必须大于 0。");
        }

        if (rtu.DataBits is < 5 or > 8)
        {
            result.AddError("数据位必须为 5 至 8。");
        }

        if (rtu.StopBits == SerialStopBits.OnePointFive && rtu.DataBits != 5)
        {
            result.AddError("1.5 个停止位仅适用于 5 个数据位。");
        }
    }

    private static void ValidateTcp(TcpConnectionConfiguration tcp, ValidationResult result)
    {
        if (string.IsNullOrWhiteSpace(tcp.Host))
        {
            result.AddError("TCP 主机地址不能为空。");
        }
        else if (!IPAddress.TryParse(tcp.Host, out _)
                 && Uri.CheckHostName(tcp.Host) == UriHostNameType.Unknown)
        {
            result.AddError("TCP 主机地址格式无效。");
        }

        ValidatePort(tcp.Port, result);
        if (tcp.ConnectTimeoutMilliseconds <= 0)
        {
            result.AddError("TCP 建立连接超时必须大于 0 毫秒。");
        }
    }

    private static void ValidatePort(int port, ValidationResult result)
    {
        if (port is < 1 or > 65535)
        {
            result.AddError("TCP 端口必须为 1 至 65535。");
        }
    }
}
