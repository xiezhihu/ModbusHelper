namespace ModbusHelper.Business.Trends;

/// <summary>
/// 固定容量、无锁单生产者单消费者环形缓冲区。写入只由轮询任务执行，读取只由 UI Timer 执行。
/// 每个槽位使用序号护栏，消费者不会读到正在被覆盖的半成品数据。
/// </summary>
/// <typeparam name="T">值类型数据。</typeparam>
public sealed class SpscRingBuffer<T>
    where T : struct
{
    private readonly Slot[] _slots;
    private long _writeSequence;

    /// <summary>
    /// 初始化环形缓冲区。
    /// </summary>
    /// <param name="capacity">固定容量。</param>
    public SpscRingBuffer(int capacity)
    {
        if (capacity < 2)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "容量至少为 2。");
        }

        Capacity = capacity;
        _slots = new Slot[capacity];
    }

    /// <summary>固定容量。</summary>
    public int Capacity { get; }

    /// <summary>当前最新写入序号。</summary>
    public long LatestSequence => Volatile.Read(ref _writeSequence);

    /// <summary>
    /// 写入一个元素；容量满后覆盖最旧元素。
    /// </summary>
    /// <param name="value">待写入值。</param>
    public void Write(T value)
    {
        long sequence = Interlocked.Increment(ref _writeSequence);
        int index = (int)((sequence - 1) % Capacity);
        ref Slot slot = ref _slots[index];
        Volatile.Write(ref slot.Sequence, 0);
        slot.Value = value;
        Volatile.Write(ref slot.Sequence, sequence);
    }

    /// <summary>
    /// 读取指定序号之后仍保留的数据，并返回新的消费者游标。
    /// </summary>
    /// <param name="consumerSequence">上次已消费的序号。</param>
    /// <param name="values">新数据。</param>
    /// <returns>新的消费者序号。</returns>
    public long ReadAfter(long consumerSequence, out T[] values)
    {
        long newest = Volatile.Read(ref _writeSequence);
        if (newest <= consumerSequence)
        {
            values = Array.Empty<T>();
            return consumerSequence;
        }

        long oldest = Math.Max(1, newest - Capacity + 1);
        long start = Math.Max(consumerSequence + 1, oldest);
        int count = checked((int)(newest - start + 1));
        List<T> result = new(count);

        for (long sequence = start; sequence <= newest; sequence++)
        {
            ref Slot slot = ref _slots[(int)((sequence - 1) % Capacity)];
            if (Volatile.Read(ref slot.Sequence) != sequence)
            {
                continue;
            }

            T value = slot.Value;
            if (Volatile.Read(ref slot.Sequence) == sequence)
            {
                result.Add(value);
            }
        }

        values = result.ToArray();
        return newest;
    }

    private struct Slot
    {
        public long Sequence;
        public T Value;
    }
}
