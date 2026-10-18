using ModbusHelper.Core.Enums;

namespace ModbusHelper.Core.Models;

/// <summary>
/// 一次主站读写操作的普通数据结果，可被界面和命令行直接消费。
/// </summary>
public sealed class ModbusOperationResult
{
    /// <summary>完成时间。</summary>
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.Now;

    /// <summary>功能码。</summary>
    public ModbusFunction Function { get; init; }

    /// <summary>从站地址。</summary>
    public byte UnitId { get; init; }

    /// <summary>0 基协议起始地址。</summary>
    public ushort StartAddress { get; init; }

    /// <summary>读取或写入数量。</summary>
    public ushort Count { get; init; }

    /// <summary>耗时，单位毫秒。</summary>
    public long ElapsedMilliseconds { get; init; }

    /// <summary>是否为广播写。</summary>
    public bool IsBroadcast { get; init; }

    /// <summary>线圈或离散输入结果。</summary>
    public bool[]? Coils { get; init; }

    /// <summary>寄存器结果。</summary>
    public ushort[]? Registers { get; init; }

    /// <summary>请求原始报文，多个重试帧以竖线分隔。</summary>
    public string RequestHex { get; init; } = string.Empty;

    /// <summary>响应原始报文，多个帧以竖线分隔。</summary>
    public string ResponseHex { get; init; } = string.Empty;

    /// <summary>解析后的显示文本。</summary>
    public string ParsedValue { get; init; } = string.Empty;
}
