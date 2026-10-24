using ModbusHelper.Core.Configuration;
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

    public SnapshotSlaveDataStore(object syncRoot)
    {
        _syncRoot = syncRoot;
        SlaveDataStore inner = new();
        _holdingRegisters = new(inner.HoldingRegisters, syncRoot);
        _inputRegisters = new(inner.InputRegisters, syncRoot);
        _coils = new(inner.CoilDiscretes, syncRoot);
        _discreteInputs = new(inner.CoilInputs, syncRoot);
    }

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

    private sealed class SnapshotPointSource<T>(IPointSource<T> inner, object syncRoot) : IPointSource<T>
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

            lock (syncRoot)
            {
                inner.WritePoints(startAddress, points);
                for (int index = 0; index < points.Length; index++)
                {
                    Values[(ushort)(startAddress + index)] = points[index];
                }
            }
        }
    }
}
