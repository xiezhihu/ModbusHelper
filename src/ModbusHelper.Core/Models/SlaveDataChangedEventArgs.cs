using ModbusHelper.Core.Enums;

namespace ModbusHelper.Core.Models;

/// <summary>从站存储写入成功且内容变化的通知；地址始终为 0 基协议地址。</summary>
public sealed class SlaveDataChangedEventArgs(
    byte unitId,
    SlaveArea area,
    ushort startAddress,
    int count) : EventArgs
{
    /// <summary>从站地址。</summary>
    public byte UnitId { get; } = unitId;

    /// <summary>发生变化的存储区。</summary>
    public SlaveArea Area { get; } = area;

    /// <summary>本次写入的起始协议地址。</summary>
    public ushort StartAddress { get; } = startAddress;

    /// <summary>本次写入的连续地址数量。</summary>
    public int Count { get; } = count;
}
