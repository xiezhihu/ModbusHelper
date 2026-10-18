using ModbusHelper.Core.Enums;

namespace ModbusHelper.Core.Configuration;

/// <summary>
/// 工具持久化配置根对象。该类型不依赖任何 UI 控件类型。
/// </summary>
public sealed class AppConfiguration
{
    /// <summary>当前配置版本。</summary>
    public const int CurrentVersion = 1;

    /// <summary>配置版本，用于后续升级兼容。</summary>
    public int ConfigVersion { get; set; } = CurrentVersion;

    /// <summary>界面当前地址基准。</summary>
    public AddressBase AddressBase { get; set; } = AddressBase.ZeroBased;

    /// <summary>主站连接配置。</summary>
    public ConnectionConfiguration Connection { get; set; } = new();

    /// <summary>轮询任务配置。</summary>
    public List<PollingTaskConfiguration> PollingTasks { get; set; } = new();

    /// <summary>点位表配置。</summary>
    public List<PointConfiguration> Points { get; set; } = new();

    /// <summary>数据解析配置。</summary>
    public DataParsingConfiguration Parsing { get; set; } = new();

    /// <summary>每条趋势曲线保留的最大点数。</summary>
    public int TrendCapacity { get; set; } = 5000;

    /// <summary>从站模拟配置。</summary>
    public SlaveSimulationConfiguration SlaveSimulator { get; set; } = new();
}
