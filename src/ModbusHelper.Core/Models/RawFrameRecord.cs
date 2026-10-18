using ModbusHelper.Core.Enums;

namespace ModbusHelper.Core.Models;

/// <summary>
/// 一条完整的 Modbus 原始帧记录。
/// </summary>
/// <param name="Timestamp">本机时间戳，保留毫秒精度。</param>
/// <param name="Role">产生报文的角色。</param>
/// <param name="Direction">报文方向。</param>
/// <param name="Hex">空格分隔的十六进制原始帧。</param>
/// <param name="Meaning">根据角色、方向和功能码生成的中文含义。</param>
/// <param name="ChecksumStatus">CRC 校验状态；TCP 或未知时为 null。</param>
public sealed record RawFrameRecord(
    DateTimeOffset Timestamp,
    ModbusRole Role,
    DataDirection Direction,
    string Hex,
    string Meaning,
    bool? ChecksumStatus);
