using ModbusHelper.Core.Configuration;

namespace ModbusHelper.Core.Models;

/// <summary>单个从站当前内存的数据副本，不作为启动配置保存。</summary>
public sealed class SlaveUnitDataSnapshot
{
    /// <summary>从站地址。</summary>
    public byte UnitId { get; init; }

    /// <summary>当前保持寄存器值。</summary>
    public IReadOnlyList<RegisterInitialValue> HoldingRegisters { get; init; } = Array.Empty<RegisterInitialValue>();

    /// <summary>当前输入寄存器值。</summary>
    public IReadOnlyList<RegisterInitialValue> InputRegisters { get; init; } = Array.Empty<RegisterInitialValue>();

    /// <summary>当前线圈值。</summary>
    public IReadOnlyList<CoilInitialValue> Coils { get; init; } = Array.Empty<CoilInitialValue>();

    /// <summary>当前离散输入值。</summary>
    public IReadOnlyList<CoilInitialValue> DiscreteInputs { get; init; } = Array.Empty<CoilInitialValue>();
}
