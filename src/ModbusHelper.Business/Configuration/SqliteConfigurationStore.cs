using System.Text.Json;
using System.Text.Json.Serialization;

using Microsoft.Data.Sqlite;

using ModbusHelper.Core.Configuration;
using ModbusHelper.Core.Enums;
using ModbusHelper.Core.Protocol;

namespace ModbusHelper.Business.Configuration;

/// <summary>
/// 使用 SQLite 持久化应用配置和点位表；不保存运行时状态或趋势采样。
/// </summary>
public sealed class SqliteConfigurationStore
{
    private readonly JsonSerializerOptions _serializerOptions;
    private readonly string? _legacyJsonPath;
    private readonly string _connectionString;

    /// <summary>
    /// 初始化 SQLite 配置存储。
    /// </summary>
    /// <param name="databasePath">数据库文件路径，桌面应用应传入 EXE 同目录路径。</param>
    /// <param name="legacyJsonPath">可选的旧 JSON 配置路径，用于首次启动迁移。</param>
    public SqliteConfigurationStore(string databasePath, string? legacyJsonPath = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        DatabasePath = Path.GetFullPath(databasePath);
        _legacyJsonPath = string.IsNullOrWhiteSpace(legacyJsonPath)
            ? null
            : Path.GetFullPath(legacyJsonPath);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = DatabasePath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false
        }.ToString();
        _serializerOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };
        _serializerOptions.Converters.Add(
            new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, false));
    }

    /// <summary>SQLite 数据库完整路径。</summary>
    public string DatabasePath { get; }

    /// <summary>
    /// 加载配置；首次启动时优先迁移旧 JSON，否则保存并返回默认配置。
    /// </summary>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>已加载的配置，运行时状态重置为停止。</returns>
    /// <exception cref="ConfigurationException">数据库损坏、配置无效或迁移失败。</exception>
    public Task<AppConfiguration> LoadAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            string? configurationJson;
            AppConfiguration? configuration;
            using (SqliteConnection connection = OpenConnection())
            {
                EnsureSchema(connection);
                configurationJson = ReadConfigurationJson(connection);
                configuration = configurationJson is null
                    ? null
                    : JsonSerializer.Deserialize<AppConfiguration>(
                        configurationJson,
                        _serializerOptions);
                if (configuration is not null)
                {
                    ValidateVersion(configuration.ConfigVersion);
                    NormalizeConfiguration(configuration);
                    configuration.Points = ReadPoints(connection);
                    NormalizeReadFunctions(configuration);
                    ResetRuntimeState(configuration);
                }
            }

            if (configuration is null)
            {
                configuration = LoadLegacyOrDefaultAsync(cancellationToken)
                    .GetAwaiter()
                    .GetResult();
                NormalizeConfiguration(configuration);
                NormalizeReadFunctions(configuration);
                SaveAsync(configuration, cancellationToken).GetAwaiter().GetResult();
            }

            return Task.FromResult(configuration);
        }
        catch (ConfigurationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is SqliteException
            or IOException
            or UnauthorizedAccessException
            or JsonException)
        {
            throw new ConfigurationException(
                $"SQLite 配置读取失败（{DatabasePath}）：{exception.Message}",
                exception);
        }
    }

    /// <summary>
    /// 以事务方式保存配置和点位表，避免只保存部分配置。
    /// </summary>
    /// <param name="configuration">待保存的纯 POCO 配置。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <exception cref="ConfigurationException">数据库写入失败或配置版本无效。</exception>
    public Task SaveAsync(
        AppConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateVersion(configuration.ConfigVersion);
        NormalizeConfiguration(configuration);
        try
        {
            string? directory = Path.GetDirectoryName(DatabasePath);
            if (string.IsNullOrWhiteSpace(directory))
            {
                throw new ConfigurationException("SQLite 数据库目录无效。");
            }

            Directory.CreateDirectory(directory);
            using SqliteConnection connection = OpenConnection();
            EnsureSchema(connection);
            using SqliteTransaction transaction = connection.BeginTransaction();
            AppConfiguration snapshot = CloneConfiguration(configuration);
            snapshot.Points.Clear();

            using (SqliteCommand command = connection.CreateCommand())
            {
                command.Transaction = transaction;
                command.CommandText = """
                    INSERT INTO AppConfiguration (Id, ConfigVersion, ConfigurationJson, SavedAt)
                    VALUES (1, $version, $json, $savedAt)
                    ON CONFLICT(Id) DO UPDATE SET
                        ConfigVersion = excluded.ConfigVersion,
                        ConfigurationJson = excluded.ConfigurationJson,
                        SavedAt = excluded.SavedAt;
                    """;
                command.Parameters.AddWithValue("$version", configuration.ConfigVersion);
                command.Parameters.AddWithValue(
                    "$json",
                    JsonSerializer.Serialize(snapshot, _serializerOptions));
                command.Parameters.AddWithValue(
                    "$savedAt",
                    DateTimeOffset.Now.ToString("O", System.Globalization.CultureInfo.InvariantCulture));
                command.ExecuteNonQuery();
            }

            using (SqliteCommand delete = connection.CreateCommand())
            {
                delete.Transaction = transaction;
                delete.CommandText = "DELETE FROM Points;";
                delete.ExecuteNonQuery();
            }

            for (int index = 0; index < configuration.Points.Count; index++)
            {
                PointConfiguration point = configuration.Points[index];
                if (string.IsNullOrWhiteSpace(point.Id))
                {
                    point.Id = Guid.NewGuid().ToString("N");
                }

                using SqliteCommand insert = connection.CreateCommand();
                insert.Transaction = transaction;
                insert.CommandText = """
                    INSERT INTO Points (
                        Id, SortOrder, Name, UnitId, Function, StartAddress, Count,
                        DataType, ByteOrder, TrendColorArgb, ShowInTrend)
                    VALUES (
                        $id, $sortOrder, $name, $unitId, $function, $startAddress, $count,
                        $dataType, $byteOrder, $trendColorArgb, $showInTrend);
                    """;
                insert.Parameters.AddWithValue("$id", point.Id);
                insert.Parameters.AddWithValue("$sortOrder", index);
                insert.Parameters.AddWithValue("$name", point.Name);
                insert.Parameters.AddWithValue("$unitId", point.UnitId);
                insert.Parameters.AddWithValue("$function", ToDatabaseName(point.Function));
                insert.Parameters.AddWithValue("$startAddress", point.StartAddress);
                insert.Parameters.AddWithValue("$count", point.Count);
                insert.Parameters.AddWithValue("$dataType", ToDatabaseName(point.DataType));
                insert.Parameters.AddWithValue("$byteOrder", ToDatabaseName(point.ByteOrder));
                insert.Parameters.AddWithValue("$trendColorArgb", point.TrendColorArgb);
                insert.Parameters.AddWithValue("$showInTrend", point.ShowInTrend ? 1 : 0);
                insert.ExecuteNonQuery();
            }

            transaction.Commit();
            ResetRuntimeState(configuration);
            return Task.CompletedTask;
        }
        catch (ConfigurationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is SqliteException
            or IOException
            or UnauthorizedAccessException
            or JsonException)
        {
            throw new ConfigurationException(
                $"SQLite 配置写入失败（{DatabasePath}）：{exception.Message}",
                exception);
        }
    }

    private SqliteConnection OpenConnection()
    {
        SqliteConnection connection = new(_connectionString);
        connection.Open();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON; PRAGMA busy_timeout = 5000;";
        command.ExecuteNonQuery();
        return connection;
    }

    private static void EnsureSchema(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS AppConfiguration (
                Id INTEGER PRIMARY KEY CHECK (Id = 1),
                ConfigVersion INTEGER NOT NULL,
                ConfigurationJson TEXT NOT NULL,
                SavedAt TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS Points (
                Id TEXT PRIMARY KEY,
                SortOrder INTEGER NOT NULL,
                Name TEXT NOT NULL,
                UnitId INTEGER NOT NULL,
                Function TEXT NOT NULL,
                StartAddress INTEGER NOT NULL,
                Count INTEGER NOT NULL,
                DataType TEXT NOT NULL,
                ByteOrder TEXT NOT NULL,
                TrendColorArgb INTEGER NOT NULL,
                ShowInTrend INTEGER NOT NULL
            );
            """;
        command.ExecuteNonQuery();
    }

    private static string? ReadConfigurationJson(SqliteConnection connection)
    {
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "SELECT ConfigurationJson FROM AppConfiguration WHERE Id = 1;";
        return command.ExecuteScalar() as string;
    }

    private static List<PointConfiguration> ReadPoints(SqliteConnection connection)
    {
        List<PointConfiguration> points = new();
        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT Id, Name, UnitId, Function, StartAddress, Count,
                   DataType, ByteOrder, TrendColorArgb, ShowInTrend
            FROM Points
            ORDER BY SortOrder, Id;
            """;
        using SqliteDataReader reader = command.ExecuteReader();
        while (reader.Read())
        {
            points.Add(new PointConfiguration
            {
                Id = reader.GetString(0),
                Name = reader.GetString(1),
                UnitId = checked((byte)reader.GetInt32(2)),
                Function = ParseDatabaseName<ModbusFunction>(reader.GetString(3)),
                StartAddress = checked((ushort)reader.GetInt32(4)),
                Count = checked((ushort)reader.GetInt32(5)),
                DataType = ParseDatabaseName<DataValueType>(reader.GetString(6)),
                ByteOrder = ParseDatabaseName<RegisterByteOrder>(reader.GetString(7)),
                TrendColorArgb = reader.GetInt32(8),
                ShowInTrend = reader.GetInt32(9) != 0
            });
        }

        return points;
    }

    private async Task<AppConfiguration> LoadLegacyOrDefaultAsync(
        CancellationToken cancellationToken)
    {
        if (_legacyJsonPath is not null && File.Exists(_legacyJsonPath))
        {
            return await new JsonConfigurationStore(_legacyJsonPath)
                .LoadAsync(cancellationToken)
                .ConfigureAwait(false);
        }

        return JsonConfigurationStore.CreateDefaultConfiguration();
    }

    private static AppConfiguration CloneConfiguration(AppConfiguration configuration)
    {
        string json = JsonSerializer.Serialize(configuration);
        return JsonSerializer.Deserialize<AppConfiguration>(json)
            ?? throw new ConfigurationException("无法生成配置快照。");
    }

    private static string ToDatabaseName<TEnum>(TEnum value)
        where TEnum : struct, Enum
    {
        return JsonNamingPolicy.CamelCase.ConvertName(value.ToString());
    }

    private static TEnum ParseDatabaseName<TEnum>(string value)
        where TEnum : struct, Enum
    {
        return Enum.TryParse(value, ignoreCase: true, out TEnum result)
            ? result
            : throw new ConfigurationException($"SQLite 配置中包含未知枚举值“{value}”。");
    }

    private static void ValidateVersion(int version)
    {
        if (version < 1 || version > AppConfiguration.CurrentVersion)
        {
            throw new ConfigurationException(
                $"配置版本 {version} 无效或高于当前程序支持的版本 {AppConfiguration.CurrentVersion}。");
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

    private static void NormalizeConfiguration(AppConfiguration configuration)
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

    private static void NormalizeReadFunctions(AppConfiguration configuration)
    {
        foreach (PollingTaskConfiguration task in configuration.PollingTasks)
        {
            if (task.Function.IsWrite())
            {
                task.Function = ModbusFunction.ReadHoldingRegisters;
            }
        }

        foreach (PointConfiguration point in configuration.Points)
        {
            if (point.Function.IsWrite())
            {
                point.Function = ModbusFunction.ReadHoldingRegisters;
            }
        }
    }
}
