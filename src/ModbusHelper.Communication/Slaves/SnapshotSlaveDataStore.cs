using ModbusHelper.Core.Configuration;
using ModbusHelper.Core.Enums;
using ModbusHelper.Core.Models;

using NModbus;
using NModbus.Data;

namespace ModbusHelper.Communication.Slaves;

/// <summary>保留 NModbus 存储行为，同时记录实际写入过的地址，供界面查看当前数据。</summary>
internal sealed class SnapshotSlaveDataStore : ISlaveDataStore
{
    private readonly object _syncRoot;
    private readonly SnapshotPointSource<ushort> _holdingRegisters;
    private readonly SnapshotPointSource<ushort> _inputRegisters;
    private readonly SnapshotPointSource<bool> _coils;
    private readonly SnapshotPointSource<bool> _discreteInputs;

    public SnapshotSlaveDataStore(object syncRoot, byte unitId)
    {
        _syncRoot = syncRoot;
        SlaveDataStore inner = new();
        _holdingRegisters = new(inner.HoldingRegisters, syncRoot,
            (address, count) => Notify(SlaveArea.HoldingRegisters, address, count));
        _inputRegisters = new(inner.InputRegisters, syncRoot,
            (address, count) => Notify(SlaveArea.InputRegisters, address, count));
        _coils = new(inner.CoilDiscretes, syncRoot,
            (address, count) => Notify(SlaveArea.Coils, address, count));
        _discreteInputs = new(inner.CoilInputs, syncRoot,
            (address, count) => Notify(SlaveArea.DiscreteInputs, address, count));

        void Notify(SlaveArea area, ushort address, int count) =>
            DataChanged?.Invoke(this, new SlaveDataChangedEventArgs(unitId, area, address, count));
    }

    public event EventHandler<SlaveDataChangedEventArgs>? DataChanged;

    public IPointSource<ushort> HoldingRegisters => _holdingRegisters;
    public IPointSource<ushort> InputRegisters => _inputRegisters;
    public IPointSource<bool> CoilDiscretes => _coils;
    public IPointSource<bool> CoilInputs => _discreteInputs;

    public SlaveUnitDataSnapshot GetSnapshot(byte unitId)
    {
        lock (_syncRoot)
        {
            return new SlaveUnitDataSnapshot
            {
                UnitId = unitId,
                HoldingRegisters = _holdingRegisters.Values
                    .Select(pair => new RegisterInitialValue { Address = pair.Key, Value = pair.Value }).ToArray(),
                InputRegisters = _inputRegisters.Values
                    .Select(pair => new RegisterInitialValue { Address = pair.Key, Value = pair.Value }).ToArray(),
                Coils = _coils.Values
                    .Select(pair => new CoilInitialValue { Address = pair.Key, Value = pair.Value }).ToArray(),
                DiscreteInputs = _discreteInputs.Values
                    .Select(pair => new CoilInitialValue { Address = pair.Key, Value = pair.Value }).ToArray()
            };
        }
    }

    private sealed class SnapshotPointSource<T>(
        IPointSource<T> inner,
        object syncRoot,
        Action<ushort, int> notifyChanged) : IPointSource<T>
    {
        public SortedDictionary<ushort, T> Values { get; } = new();

        public T[] ReadPoints(ushort startAddress, ushort numberOfPoints)
        {
            lock (syncRoot)
            {
                return inner.ReadPoints(startAddress, numberOfPoints);
            }
        }

        public void WritePoints(ushort startAddress, T[] points)
        {
            ArgumentNullException.ThrowIfNull(points);
            if (startAddress + points.Length > ushort.MaxValue + 1)
            {
                throw new ArgumentOutOfRangeException(nameof(points), "写入范围超出 0 至 65535 的协议地址空间。");
            }

            bool changed = false;
            lock (syncRoot)
            {
                inner.WritePoints(startAddress, points);
                for (int index = 0; index < points.Length; index++)
                {
                    ushort address = (ushort)(startAddress + index);
                    changed |= !Values.TryGetValue(address, out T? previous)
                        || !EqualityComparer<T>.Default.Equals(previous, points[index]);
                    Values[address] = points[index];
                }
            }

            // 每批写入只通知一次；新地址即使写入默认值，也属于可见数据的变化。
            if (changed)
            {
                notifyChanged(startAddress, points.Length);
            }
        }
    }
}
