using System.Net.Sockets;
using ModbusHelper.Core.Enums;
using ModbusHelper.Core.Exceptions;
using NModbus;

namespace ModbusHelper.Communication.Exceptions;

/// <summary>
/// 将 NModbus 和系统异常转换为统一通信异常。
/// </summary>
public static class ModbusExceptionTranslator
{
    /// <summary>
    /// 转换异常。
    /// </summary>
    /// <param name="exception">原始异常。</param>
    /// <param name="operation">中文操作名称。</param>
    /// <param name="unitId">从站地址。</param>
    /// <param name="functionCode">功能码。</param>
    /// <returns>统一通信异常。</returns>
    public static ModbusCommunicationException Translate(
        Exception exception,
        string operation,
        byte? unitId = null,
        byte? functionCode = null)
    {
        Exception unwrapped = Unwrap(exception);

        if (unwrapped is ModbusCommunicationException communicationException)
        {
            return communicationException;
        }

        if (unwrapped is SlaveException slaveException)
        {
            byte normalizedFunction = (byte)(slaveException.FunctionCode & 0x7F);
            ExceptionCodeDefinition definition = ModbusExceptionCodeMap.Get(slaveException.SlaveExceptionCode);
            return new ModbusCommunicationException(
                $"{operation}失败：从站返回异常 0x{slaveException.SlaveExceptionCode:X2} {definition.Meaning}",
                CommunicationErrorKind.SlaveException,
                slaveException.SlaveAddress == 0 ? unitId : slaveException.SlaveAddress,
                normalizedFunction == 0 ? functionCode : normalizedFunction,
                slaveException.SlaveExceptionCode,
                definition.Meaning,
                definition.Hint,
                slaveException);
        }

        if (unwrapped is TimeoutException)
        {
            return new ModbusCommunicationException(
                $"{operation}超时",
                CommunicationErrorKind.Timeout,
                unitId,
                functionCode,
                null,
                null,
                "检查从站地址、串口参数、网络连通性和从站响应时间；必要时分别调整传输层超时和应用层应答超时。",
                unwrapped);
        }

        if (unwrapped is SocketException socketException)
        {
            if (socketException.SocketErrorCode == SocketError.ConnectionRefused)
            {
                return new ModbusCommunicationException(
                    $"{operation}失败：连接被拒绝",
                    CommunicationErrorKind.Connection,
                    unitId,
                    functionCode,
                    null,
                    null,
                    "确认目标 IP、端口和从站服务是否已启动。",
                    unwrapped);
            }

            return new ModbusCommunicationException(
                $"{operation}失败：网络错误 {socketException.SocketErrorCode}",
                CommunicationErrorKind.Io,
                unitId,
                functionCode,
                null,
                null,
                "检查网线、防火墙、IP 地址和路由配置。",
                unwrapped);
        }

        if (unwrapped is UnauthorizedAccessException || IsPortBusyMessage(unwrapped.Message))
        {
            return new ModbusCommunicationException(
                $"{operation}失败：串口不可用",
                CommunicationErrorKind.PortBusy,
                unitId,
                functionCode,
                null,
                null,
                "串口可能被其他程序占用或当前用户无访问权限；关闭占用程序后重新连接。",
                unwrapped);
        }

        if (unwrapped is ArgumentOutOfRangeException || unwrapped is ArgumentException)
        {
            return new ModbusCommunicationException(
                $"{operation}参数错误：{unwrapped.Message}",
                CommunicationErrorKind.Validation,
                unitId,
                functionCode,
                null,
                null,
                "检查从站地址、功能码、起始地址、数量和写入数据。",
                unwrapped);
        }

        if (unwrapped is IOException)
        {
            return new ModbusCommunicationException(
                $"{operation}发生输入输出错误",
                CommunicationErrorKind.Io,
                unitId,
                functionCode,
                null,
                null,
                "检查连接是否中断、串口参数是否匹配以及从站是否在线。",
                unwrapped);
        }

        if (unwrapped is OperationCanceledException)
        {
            return new ModbusCommunicationException(
                $"{operation}已取消",
                CommunicationErrorKind.Cancelled,
                unitId,
                functionCode,
                null,
                null,
                "操作被用户或程序取消。",
                unwrapped);
        }

        return new ModbusCommunicationException(
            $"{operation}失败：{unwrapped.Message}",
            CommunicationErrorKind.Unknown,
            unitId,
            functionCode,
            null,
            null,
            "查看原始异常和报文日志进一步定位。",
            unwrapped);
    }

    private static Exception Unwrap(Exception exception)
    {
        if (exception is AggregateException aggregateException)
        {
            return aggregateException.Flatten().InnerExceptions.Count == 1
                ? Unwrap(aggregateException.Flatten().InnerExceptions[0])
                : exception;
        }

        return exception.InnerException is not null
               && exception is not SlaveException
               && exception is not ModbusCommunicationException
            ? Unwrap(exception.InnerException)
            : exception;
    }

    private static bool IsPortBusyMessage(string message)
    {
        return message.Contains("access", StringComparison.OrdinalIgnoreCase)
               || message.Contains("denied", StringComparison.OrdinalIgnoreCase)
               || message.Contains("busy", StringComparison.OrdinalIgnoreCase)
               || message.Contains("占用", StringComparison.OrdinalIgnoreCase)
               || message.Contains("拒绝", StringComparison.OrdinalIgnoreCase);
    }
}
