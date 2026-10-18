using ModbusHelper.Core.Configuration;

namespace ModbusHelper.Business.Trends;

/// <summary>
/// 趋势采样点。
/// </summary>
/// <param name="Timestamp">采样时间。</param>
/// <param name="Value">用于绘图的数值。</param>
/// <param name="DisplayValue">原始显示值。</param>
public readonly record struct TrendSample(
    DateTimeOffset Timestamp,
    double Value,
    string DisplayValue);

/// <summary>
/// 单条趋势曲线。
/// </summary>
public sealed class TrendSeries
{
    private readonly SpscRingBuffer<TrendSample> _buffer;

    /// <summary>
    /// 初始化趋势曲线。
    /// </summary>
    /// <param name="id">曲线标识。</param>
    /// <param name="name">曲线名称。</param>
    /// <param name="colorArgb">ARGB 颜色。</param>
    /// <param name="capacity">最大点数。</param>
    public TrendSeries(string id, string name, int colorArgb, int capacity)
    {
        Id = id;
        Name = name;
        ColorArgb = colorArgb;
        _buffer = new SpscRingBuffer<TrendSample>(capacity);
    }

    /// <summary>曲线标识。</summary>
    public string Id { get; }

    /// <summary>曲线名称。</summary>
    public string Name { get; }

    /// <summary>ARGB 颜色。</summary>
    public int ColorArgb { get; }

    /// <summary>是否在图表中显示。</summary>
    public bool Visible { get; set; } = true;

    /// <summary>固定容量。</summary>
    public int Capacity => _buffer.Capacity;

    /// <summary>最新写入序号。</summary>
    public long LatestSequence => _buffer.LatestSequence;

    /// <summary>
    /// 后台轮询线程写入采样点。
    /// </summary>
    /// <param name="sample">采样点。</param>
    public void Append(TrendSample sample)
    {
        _buffer.Write(sample);
    }

    /// <summary>
    /// UI 线程读取新采样点。
    /// </summary>
    /// <param name="cursor">上次消费序号。</param>
    /// <param name="samples">新增采样点。</param>
    /// <returns>新游标。</returns>
    public long ReadAfter(long cursor, out TrendSample[] samples)
    {
        return _buffer.ReadAfter(cursor, out samples);
    }
}

/// <summary>
/// 趋势曲线集合。曲线对象可被后台写入和 UI 读取，缓冲区本身线程安全。
/// </summary>
public sealed class TrendDataStore
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<string, TrendSeries> _series = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// 根据轮询任务创建或更新曲线。
    /// </summary>
    /// <param name="task">轮询任务配置。</param>
    /// <param name="capacity">默认容量。</param>
    /// <returns>趋势曲线。</returns>
    public TrendSeries GetOrCreate(PollingTaskConfiguration task, int capacity = 5000)
    {
        ArgumentNullException.ThrowIfNull(task);
        lock (_syncRoot)
        {
            if (_series.TryGetValue(task.Id, out TrendSeries? existing))
            {
                return existing;
            }

            TrendSeries created = new(
                task.Id,
                task.Name,
                task.TrendColorArgb,
                capacity);
            _series[task.Id] = created;
            return created;
        }
    }

    /// <summary>
    /// 获取曲线。
    /// </summary>
    /// <param name="id">曲线标识。</param>
    /// <returns>曲线；不存在时返回 null。</returns>
    public TrendSeries? Get(string id)
    {
        lock (_syncRoot)
        {
            return _series.GetValueOrDefault(id);
        }
    }

    /// <summary>
    /// 获取全部曲线快照。
    /// </summary>
    /// <returns>曲线快照。</returns>
    public IReadOnlyList<TrendSeries> GetAll()
    {
        lock (_syncRoot)
        {
            return _series.Values.ToArray();
        }
    }

    /// <summary>
    /// 删除曲线。
    /// </summary>
    /// <param name="id">曲线标识。</param>
    public void Remove(string id)
    {
        lock (_syncRoot)
        {
            _series.Remove(id);
        }
    }

    /// <summary>
    /// 清空全部曲线数据。
    /// </summary>
    public void Clear()
    {
        lock (_syncRoot)
        {
            _series.Clear();
        }
    }
}
