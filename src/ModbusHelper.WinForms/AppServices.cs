using ModbusHelper.Business.ByteOrder;
using ModbusHelper.Business.Configuration;
using ModbusHelper.Business.Connections;
using ModbusHelper.Business.Logging;
using ModbusHelper.Business.Polling;
using ModbusHelper.Business.Slaves;
using ModbusHelper.Business.Trends;
using ModbusHelper.Core.Configuration;

namespace ModbusHelper.WinForms;

/// <summary>
/// 仅 UI 使用的依赖容器。业务对象均在 WinForms 之外创建，命令行宿主可复用同一组服务。
/// </summary>
internal sealed class AppServices : IAsyncDisposable
{
    private AppServices(
        AppConfiguration configuration,
        SqliteConfigurationStore configurationStore,
        ModbusLogService logService,
        ConnectionManager connectionManager,
        TrendDataStore trendDataStore,
        PollingScheduler pollingScheduler,
        SlaveSimulationManager slaveSimulationManager,
        ByteOrderProbeService byteOrderProbeService,
        string? configurationWarning)
    {
        Configuration = configuration;
        ConfigurationStore = configurationStore;
        LogService = logService;
        ConnectionManager = connectionManager;
        TrendDataStore = trendDataStore;
        PollingScheduler = pollingScheduler;
        SlaveSimulationManager = slaveSimulationManager;
        ByteOrderProbeService = byteOrderProbeService;
        ConfigurationWarning = configurationWarning;
    }

    public AppConfiguration Configuration { get; }

    public SqliteConfigurationStore ConfigurationStore { get; }

    public ModbusLogService LogService { get; }

    public ConnectionManager ConnectionManager { get; }

    public TrendDataStore TrendDataStore { get; }

    public PollingScheduler PollingScheduler { get; }

    public SlaveSimulationManager SlaveSimulationManager { get; }

    public ByteOrderProbeService ByteOrderProbeService { get; }

    public string? ConfigurationWarning { get; }

    public static async Task<AppServices> CreateAsync(CancellationToken cancellationToken = default)
    {
        string? databasePathOverride =
            Environment.GetEnvironmentVariable("MODBUSHELPER_DATABASE_PATH");
        string databasePath = string.IsNullOrWhiteSpace(databasePathOverride)
            ? Path.Combine(AppContext.BaseDirectory, "ModbusHelper.db")
            : Path.GetFullPath(databasePathOverride);
        string legacyConfigurationPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ModbusHelper",
            "config.json");
        SqliteConfigurationStore store = new(databasePath, legacyConfigurationPath);
        AppConfiguration configuration;
        string? warning = null;
        try
        {
            configuration = await store.LoadAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (ConfigurationException exception)
        {
            configuration = JsonConfigurationStore.CreateDefaultConfiguration();
            warning = $"配置加载失败，已使用默认配置：{exception.Message}";
        }

        ModbusLogService logService = new();
        ConnectionManager connectionManager = new(logService);
        TrendDataStore trendDataStore = new();
        PollingScheduler pollingScheduler = new(
            connectionManager,
            trendDataStore,
            logService);
        SlaveSimulationManager slaveSimulationManager = new(logService);
        ByteOrderProbeService byteOrderProbeService = new(connectionManager);

        return new AppServices(
            configuration,
            store,
            logService,
            connectionManager,
            trendDataStore,
            pollingScheduler,
            slaveSimulationManager,
            byteOrderProbeService,
            warning);
    }

    public Task SaveConfigurationAsync(CancellationToken cancellationToken = default)
    {
        return ConfigurationStore.SaveAsync(Configuration, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await PollingScheduler.DisposeAsync().ConfigureAwait(false);
        await SlaveSimulationManager.DisposeAsync().ConfigureAwait(false);
        await ConnectionManager.DisposeAsync().ConfigureAwait(false);
    }
}
