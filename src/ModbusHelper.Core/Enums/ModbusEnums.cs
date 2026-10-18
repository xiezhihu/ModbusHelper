using System.Diagnostics.CodeAnalysis;

namespace ModbusHelper.Core.Enums;

/// <summary>
/// 地址基准。协议原生地址从 0 开始，设备手册地址通常从 1 开始。
/// </summary>
public enum AddressBase
{
    /// <summary>
    /// 0 基协议地址。
    /// </summary>
    ZeroBased,

    /// <summary>
    /// 1 基设备手册地址，例如保持寄存器 40001。
    /// </summary>
    OneBased
}

/// <summary>
/// 连接传输类型。
/// </summary>
public enum ConnectionType
{
    /// <summary>
    /// Modbus RTU 串口连接。
    /// </summary>
    Rtu,

    /// <summary>
    /// Modbus TCP 网络连接。
    /// </summary>
    Tcp
}

/// <summary>
/// 连接运行状态。该类型属于运行时数据，不写入配置文件。
/// </summary>
public enum ConnectionState
{
    /// <summary>
    /// 未连接。
    /// </summary>
    Disconnected,

    /// <summary>
    /// 正在建立连接。
    /// </summary>
    Connecting,

    /// <summary>
    /// 已连接。
    /// </summary>
    Connected,

    /// <summary>
    /// 正在断开。
    /// </summary>
    Disconnecting,

    /// <summary>
    /// 连接发生故障。
    /// </summary>
    Faulted
}

/// <summary>
/// 串口校验方式，用于纯 POCO 配置序列化。
/// </summary>
public enum SerialParity
{
    /// <summary>无校验。</summary>
    None,

    /// <summary>奇校验。</summary>
    Odd,

    /// <summary>偶校验。</summary>
    Even,

    /// <summary>标记校验。</summary>
    Mark,

    /// <summary>空格校验。</summary>
    Space
}

/// <summary>
/// 串口停止位，用于纯 POCO 配置序列化。
/// </summary>
public enum SerialStopBits
{
    /// <summary>1 个停止位。</summary>
    One,

    /// <summary>1.5 个停止位。</summary>
    OnePointFive,

    /// <summary>2 个停止位。</summary>
    Two
}

/// <summary>
/// 工具支持的关键 Modbus 功能码。
/// </summary>
public enum ModbusFunction : byte
{
    /// <summary>01 读线圈。</summary>
    ReadCoils = 0x01,

    /// <summary>02 读离散输入。</summary>
    ReadDiscreteInputs = 0x02,

    /// <summary>03 读保持寄存器。</summary>
    ReadHoldingRegisters = 0x03,

    /// <summary>04 读输入寄存器。</summary>
    ReadInputRegisters = 0x04,

    /// <summary>05 写单线圈。</summary>
    WriteSingleCoil = 0x05,

    /// <summary>06 写单寄存器。</summary>
    WriteSingleRegister = 0x06,

    /// <summary>0F 写多线圈。</summary>
    WriteMultipleCoils = 0x0F,

    /// <summary>10 写多寄存器。</summary>
    WriteMultipleRegisters = 0x10
}

/// <summary>
/// 寄存器解析数据类型。
/// </summary>
[SuppressMessage("Naming", "CA1720:标识符包含类型名称", Justification = "数据类型名称必须与工业调试术语一致。")]
public enum DataValueType
{
    /// <summary>有符号 16 位整数。</summary>
    Int16,

    /// <summary>无符号 16 位整数。</summary>
    UInt16,

    /// <summary>有符号 32 位整数。</summary>
    Int32,

    /// <summary>无符号 32 位整数。</summary>
    UInt32,

    /// <summary>32 位单精度浮点数。</summary>
    Float,

    /// <summary>64 位双精度浮点数。</summary>
    Double,

    /// <summary>ASCII 字符串。</summary>
    Ascii,

    /// <summary>UTF-8 字符串。</summary>
    Utf8
}

/// <summary>
/// 寄存器数据布局。字序表示多个 16 位寄存器之间的顺序，字节序表示单个寄存器内部的顺序。
/// </summary>
public enum RegisterByteOrder
{
    /// <summary>寄存器顺序不变，寄存器内高字节在前。</summary>
    ABCD,

    /// <summary>寄存器顺序反转，寄存器内高字节在前。</summary>
    CDAB,

    /// <summary>寄存器顺序不变，寄存器内低字节在前。</summary>
    BADC,

    /// <summary>寄存器顺序反转，寄存器内低字节在前。</summary>
    DCBA
}

/// <summary>
/// 日志级别。
/// </summary>
public enum ModbusLogLevel
{
    /// <summary>调试信息。</summary>
    Debug,

    /// <summary>普通信息。</summary>
    Info,

    /// <summary>警告。</summary>
    Warn,

    /// <summary>错误。</summary>
    Error
}

/// <summary>
/// 报文角色，用于区分主站和从站模拟器产生的日志。
/// </summary>
public enum ModbusRole
{
    /// <summary>主站。</summary>
    Master,

    /// <summary>从站。</summary>
    Slave
}

/// <summary>
/// 报文方向。
/// </summary>
public enum DataDirection
{
    /// <summary>发送。</summary>
    Tx,

    /// <summary>接收。</summary>
    Rx
}

/// <summary>
/// 通信错误分类。
/// </summary>
public enum CommunicationErrorKind
{
    /// <summary>未知错误。</summary>
    Unknown,

    /// <summary>参数校验错误。</summary>
    Validation,

    /// <summary>连接被拒绝或目标不可达。</summary>
    Connection,

    /// <summary>串口或端口被占用。</summary>
    PortBusy,

    /// <summary>传输层或应用层超时。</summary>
    Timeout,

    /// <summary>从站返回异常码。</summary>
    SlaveException,

    /// <summary>输入输出错误。</summary>
    Io,

    /// <summary>CRC 或帧校验错误。</summary>
    Checksum,

    /// <summary>操作已取消。</summary>
    Cancelled
}

/// <summary>
/// 从站模拟存储区域。
/// </summary>
public enum SlaveArea
{
    /// <summary>线圈。</summary>
    Coils,

    /// <summary>离散输入。</summary>
    DiscreteInputs,

    /// <summary>保持寄存器。</summary>
    HoldingRegisters,

    /// <summary>输入寄存器。</summary>
    InputRegisters
}

/// <summary>
/// 功能码辅助方法。
/// </summary>
public static class ModbusFunctionExtensions
{
    /// <summary>
    /// 获取功能码中文名称。
    /// </summary>
    /// <param name="function">功能码。</param>
    /// <returns>中文名称。</returns>
    public static string GetChineseName(this ModbusFunction function)
    {
        return function switch
        {
            ModbusFunction.ReadCoils => "01 读线圈",
            ModbusFunction.ReadDiscreteInputs => "02 读离散输入",
            ModbusFunction.ReadHoldingRegisters => "03 读保持寄存器",
            ModbusFunction.ReadInputRegisters => "04 读输入寄存器",
            ModbusFunction.WriteSingleCoil => "05 写单线圈",
            ModbusFunction.WriteSingleRegister => "06 写单寄存器",
            ModbusFunction.WriteMultipleCoils => "0F 写多线圈",
            ModbusFunction.WriteMultipleRegisters => "10 写多寄存器",
            _ => $"未知功能码 {(byte)function:X2}"
        };
    }

    /// <summary>
    /// 判断功能码是否为写操作。
    /// </summary>
    /// <param name="function">功能码。</param>
    /// <returns>写操作返回真。</returns>
    public static bool IsWrite(this ModbusFunction function)
    {
        return function is ModbusFunction.WriteSingleCoil
            or ModbusFunction.WriteSingleRegister
            or ModbusFunction.WriteMultipleCoils
            or ModbusFunction.WriteMultipleRegisters;
    }

    /// <summary>
    /// 判断功能码是否访问寄存器区域。
    /// </summary>
    /// <param name="function">功能码。</param>
    /// <returns>访问寄存器返回真。</returns>
    public static bool UsesRegisters(this ModbusFunction function)
    {
        return function is ModbusFunction.ReadHoldingRegisters
            or ModbusFunction.ReadInputRegisters
            or ModbusFunction.WriteSingleRegister
            or ModbusFunction.WriteMultipleRegisters;
    }
}
