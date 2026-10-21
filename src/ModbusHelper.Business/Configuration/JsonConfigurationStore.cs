using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using ModbusHelper.Core.Configuration;
using ModbusHelper.Core.Enums;
using ModbusHelper.Core.Protocol;

namespace ModbusHelper.Business.Configuration;

/// <summary>
/// JSON 配置存储。配置模型为纯 POCO，可被 WinForms、命令行和脚本共同读取。
/// </summary>
public sealed class JsonConfigurationStore
{
    private readonly JsonSerializerOptions _serializerOptions;

    /// <summary>
    /// 初始化配置存储。
    /// </summary>
    /// <param name="filePath">配置文件路径；为空时使用用户本地应用数据目录。</param>
    public JsonConfigurationStore(string? filePath = null)
    {
        FilePath = string.IsNullOrWhiteSpace(filePath)
            ? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "ModbusHelper",
                "config.json")
            : Path.GetFullPath(filePath);

        _serializerOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            ReadCommentHandling = JsonCommentHandling.Skip,
            AllowTrailingCommas = true
        };
        _serializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, false));
    }

    /// <summary>配置文件完整路径。</summary>
    public string FilePath { get; }

    /// <summary>
    /// 异步加载配置；文件不存在时返回默认配置。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>配置对象。</returns>
    /// <exception cref="ConfigurationException">文件读取、JSON 解析或版本检查失败。</exception>
    public async Task<AppConfiguration> LoadAsync(CancellationToken cancellationToken = default)
    {
        if (!File.Exists(FilePath))
        {
            return CreateDefaultConfiguration();
        }

        try
        {
            await using FileStream stream = new(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            AppConfiguration? configuration =
                await JsonSerializer.DeserializeAsync<AppConfiguration>(
                    stream,
                    _serializerOptions,
                    cancellationToken);

            if (configuration is null)
            {
                throw new ConfigurationException("配置文件内容为空或格式无效。");
            }

            ValidateVersion(configuration.ConfigVersion);
            Normalize(configuration);
            ResetRuntimeState(configuration);
            return configuration;
        }
        catch (ConfigurationException)
        {
            throw;
        }
        catch (JsonException exception)
        {
            throw new ConfigurationException($"配置文件 JSON 解析失败：{exception.Message}", exception);
        }
        catch (IOException exception)
        {
            throw new ConfigurationException($"配置文件读取失败：{exception.Message}", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw new ConfigurationException($"配置文件无读取权限：{exception.Message}", exception);
        }
    }

    /// <summary>
    /// 异步保存配置。先写临时文件再替换，降低写入中断导致文件损坏的风险。
    /// </summary>
    /// <param name="configuration">待保存配置。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <exception cref="ConfigurationException">目录创建或文件写入失败。</exception>
    public async Task SaveAsync(
        AppConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ValidateVersion(configuration.ConfigVersion);
        Normalize(configuration);
        ResetRuntimeState(configuration);

        string? directory = Path.GetDirectoryName(FilePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            throw new ConfigurationException("配置文件目录无效。");
        }

        string temporaryPath = FilePath + ".tmp";
        try
        {
            Directory.CreateDirectory(directory);
            await using (FileStream stream = new(
                             temporaryPath,
                             FileMode.Create,
                             FileAccess.Write,
                             FileShare.None))
            {
                await JsonSerializer.SerializeAsync(
                    stream,
                    configuration,
                    _serializerOptions,
                    cancellationToken);
                await stream.FlushAsync(cancellationToken);
            }

            File.Move(temporaryPath, FilePath, true);
        }
        catch (IOException exception)
        {
            TryDelete(temporaryPath);
            throw new ConfigurationException($"配置文件写入失败：{exception.Message}", exception);
        }
        catch (UnauthorizedAccessException exception)
        {
            TryDelete(temporaryPath);
            throw new ConfigurationException($"配置文件无写入权限：{exception.Message}", exception);
        }
    }

    /// <summary>
    /// 创建适合首次启动的默认配置。
    /// </summary>
    /// <returns>默认配置。</returns>
    public static AppConfiguration CreateDefaultConfiguration()
    {
        AppConfiguration configuration = new();
        configuration.PollingTasks.Add(new PollingTaskConfiguration
        {
            Name = "保持寄存器轮询",
            Function = ModbusFunction.ReadHoldingRegisters,
            UnitId = 1,
            StartAddress = 0,
            Count = 1,
            IntervalMilliseconds = 1000,
            DataType = DataValueType.UInt16,
            ShowInTrend = true
        });
        configuration.SlaveSimulator.Units.Add(new SlaveUnitConfiguration
        {
            UnitId = 1,
            HoldingRegisters =
            {
                new RegisterInitialValue { Address = 0, Value = 0 }
            },
            Coils =
            {
                new CoilInitialValue { Address = 0, Value = false }
            }
        });
        return configuration;
    }

    private static void ValidateVersion(int version)
    {
        if (version < 1)
        {
            throw new ConfigurationException($"配置版本 {version} 无效。");
        }

        if (version > AppConfiguration.CurrentVersion)
        {
            throw new ConfigurationException(
                $"配置版本 {version} 高于当前程序支持的版本 {AppConfiguration.CurrentVersion}，请升级程序。");
        }
    }

    private static void Normalize(AppConfiguration configuration)
    {
        configuration.Connection ??= new ConnectionConfiguration();
        configuration.Connection.Rtu ??= new RtuConnectionConfiguration();
        configuration.Connection.Tcp ??= new TcpConnectionConfiguration();
        configuration.PollingTasks ??= new List<PollingTaskConfiguration>();
        configuration.Points ??= new List<PointConfiguration>();
        configuration.Parsing ??= new DataParsingConfiguration();
        configuration.TrendCapacity = Math.Clamp(configuration.TrendCapacity, 100, 100_000);
        configuration.SlaveSimulator ??= new SlaveSimulationConfiguration();
        configuration.SlaveSimulator.Rtu ??= new RtuConnectionConfiguration();
        configuration.SlaveSimulator.Tcp ??= new TcpSlaveConfiguration();
        configuration.SlaveSimulator.Units ??= new List<SlaveUnitConfiguration>();
        configuration.SlaveSimulator.RandomDataRules ??= new List<SlaveRandomDataRule>();
        foreach (SlaveRandomDataRule rule in configuration.SlaveSimulator.RandomDataRules)
        {
            rule.Count = Math.Max(rule.Count, (ushort)1);
            rule.IntervalMilliseconds = Math.Max(rule.IntervalMilliseconds, 100);
            if (rule.MinimumValue > rule.MaximumValue)
            {
                (rule.MinimumValue, rule.MaximumValue) =
                    (rule.MaximumValue, rule.MinimumValue);
            }
        }

        foreach (PollingTaskConfiguration task in configuration.PollingTasks)
        {
            if (string.IsNullOrWhiteSpace(task.Id))
            {
                task.Id = Guid.NewGuid().ToString("N");
            }

            task.IntervalMilliseconds = Math.Max(
                task.IntervalMilliseconds,
                ProtocolLimits.MinimumPollingIntervalMilliseconds);
            task.Count = Math.Max(task.Count, (ushort)1);
        }

        foreach (SlaveUnitConfiguration unit in configuration.SlaveSimulator.Units)
        {
            unit.HoldingRegisters ??= new List<RegisterInitialValue>();
            unit.InputRegisters ??= new List<RegisterInitialValue>();
            unit.Coils ??= new List<CoilInitialValue>();
            unit.DiscreteInputs ??= new List<CoilInitialValue>();
            unit.ExceptionRules ??= new List<ExceptionInjectionRule>();
        }
    }

    private static void ResetRuntimeState(AppConfiguration configuration)
    {
        configuration.Connection.RuntimeState = ConnectionState.Disconnected;
        configuration.Connection.IsConnected = false;

        foreach (PollingTaskConfiguration task in configuration.PollingTasks)
        {
            task.IsRunning = false;
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
            // 临时文件清理失败不覆盖原始异常。
        }
        catch (UnauthorizedAccessException)
        {
            // 临时文件清理失败不覆盖原始异常。
        }
    }
}
