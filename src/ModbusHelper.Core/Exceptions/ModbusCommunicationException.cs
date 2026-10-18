using ModbusHelper.Core.Enums;

namespace ModbusHelper.Core.Exceptions;

/// <summary>
/// 统一的 Modbus 通信异常。UI 和命令行可以直接读取中文错误信息及异常码。
/// </summary>
public sealed class ModbusCommunicationException : Exception
{
    /// <summary>
    /// 初始化通信异常。
    /// </summary>
    /// <param name="message">中文错误信息。</param>
    /// <param name="errorKind">错误分类。</param>
    /// <param name="unitId">从站地址。</param>
    /// <param name="functionCode">功能码。</param>
    /// <param name="modbusExceptionCode">Modbus 异常码。</param>
    /// <param name="modbusExceptionMeaning">Modbus 异常码中文含义。</param>
    /// <param name="hint">可能原因和排查建议。</param>
    /// <param name="innerException">原始异常。</param>
    public ModbusCommunicationException(
        string message,
        CommunicationErrorKind errorKind,
        byte? unitId,
        byte? functionCode,
        byte? modbusExceptionCode,
        string? modbusExceptionMeaning,
        string hint,
        Exception? innerException = null)
        : base(message, innerException)
    {
        ErrorKind = errorKind;
        UnitId = unitId;
        FunctionCode = functionCode;
        ModbusExceptionCode = modbusExceptionCode;
        ModbusExceptionMeaning = modbusExceptionMeaning;
        Hint = hint;
    }

    /// <summary>错误分类。</summary>
    public CommunicationErrorKind ErrorKind { get; }

    /// <summary>从站地址。</summary>
    public byte? UnitId { get; }

    /// <summary>功能码。</summary>
    public byte? FunctionCode { get; }

    /// <summary>Modbus 异常码。</summary>
    public byte? ModbusExceptionCode { get; }

    /// <summary>Modbus 异常码中文含义。</summary>
    public string? ModbusExceptionMeaning { get; }

    /// <summary>可能原因和处理建议。</summary>
    public string Hint { get; }

    /// <summary>本次请求报文，可能为空。</summary>
    public string? RequestHex { get; set; }

    /// <summary>本次响应报文，可能为空。</summary>
    public string? ResponseHex { get; set; }

    /// <summary>
    /// 获取适合直接显示的中文摘要。
    /// </summary>
    /// <returns>中文错误摘要。</returns>
    public string GetDisplayMessage()
    {
        if (ModbusExceptionCode.HasValue)
        {
            return $"Modbus 异常 0x{ModbusExceptionCode.Value:X2}：{ModbusExceptionMeaning}。{Hint}";
        }

        return string.IsNullOrWhiteSpace(Hint) ? Message : $"{Message}。{Hint}";
    }
}
