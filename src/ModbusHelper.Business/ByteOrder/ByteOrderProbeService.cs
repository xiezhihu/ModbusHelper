using System.Globalization;
using ModbusHelper.Business.Connections;
using ModbusHelper.Business.Data;
using ModbusHelper.Core.Configuration;
using ModbusHelper.Core.Enums;

namespace ModbusHelper.Business.ByteOrder;

/// <summary>
/// 字节序探测结果项。
/// </summary>
/// <param name="ByteOrder">布局名称。</param>
/// <param name="DisplayValue">解析显示值。</param>
/// <param name="NumericValue">可比较数值；非数值时为 null。</param>
/// <param name="InRange">是否落在合理范围。</param>
public sealed record ByteOrderProbeItem(
    RegisterByteOrder ByteOrder,
    string DisplayValue,
    double? NumericValue,
    bool InRange);

/// <summary>
/// 字节序探测报告。
/// </summary>
/// <param name="UnitId">从站地址。</param>
/// <param name="StartAddress">0 基协议地址。</param>
/// <param name="DataType">数据类型。</param>
/// <param name="Registers">原始寄存器。</param>
/// <param name="Items">四种布局结果。</param>
/// <param name="RecommendedByteOrder">推荐布局；无法唯一推荐时为 null。</param>
/// <param name="RecommendationMessage">中文推荐说明。</param>
public sealed record ByteOrderProbeReport(
    byte UnitId,
    ushort StartAddress,
    DataValueType DataType,
    ushort[] Registers,
    IReadOnlyList<ByteOrderProbeItem> Items,
    RegisterByteOrder? RecommendedByteOrder,
    string RecommendationMessage);

/// <summary>
/// 读取连续寄存器并按四种布局同时解析。自动探测只作为辅助，最终以设备手册为准。
/// </summary>
public sealed class ByteOrderProbeService
{
    private readonly ConnectionManager _connectionManager;

    /// <summary>
    /// 初始化探测服务。
    /// </summary>
    /// <param name="connectionManager">已连接的主站管理器。</param>
    public ByteOrderProbeService(ConnectionManager connectionManager)
    {
        _connectionManager = connectionManager;
    }

    /// <summary>
    /// 执行 32 位或 64 位字节序探测。
    /// </summary>
    /// <param name="unitId">从站地址。</param>
    /// <param name="startAddress">0 基起始地址。</param>
    /// <param name="dataType">用于判断的数值类型。</param>
    /// <param name="minimum">合理范围下限。</param>
    /// <param name="maximum">合理范围上限。</param>
    /// <param name="cancellationToken">取消令牌。</param>
    /// <returns>探测报告。</returns>
    public async Task<ByteOrderProbeReport> ProbeAsync(
        byte unitId,
        ushort startAddress,
        DataValueType dataType,
        double minimum,
        double maximum,
        CancellationToken cancellationToken = default)
    {
        int registerCount = dataType switch
        {
            DataValueType.Int32 or DataValueType.UInt32 or DataValueType.Float => 2,
            DataValueType.Double => 4,
            _ => throw new ArgumentException("字节序探测仅支持 32 位或 64 位数值类型。", nameof(dataType))
        };

        if (minimum > maximum)
        {
            throw new ArgumentException("合理范围下限不能大于上限。");
        }

        Core.Models.ModbusOperationResult read = await _connectionManager.ReadAsync(
            ModbusFunction.ReadHoldingRegisters,
            unitId,
            startAddress,
            checked((ushort)registerCount),
            cancellationToken).ConfigureAwait(false);

        ushort[] registers = read.Registers ?? Array.Empty<ushort>();
        List<ByteOrderProbeItem> items = new();
        foreach (RegisterByteOrder order in Enum.GetValues<RegisterByteOrder>())
        {
            object decoded = RegisterCodec.Decode(registers, dataType, order);
            double? numeric = TryGetNumeric(decoded);
            bool inRange = numeric.HasValue
                           && double.IsFinite(numeric.Value)
                           && numeric.Value >= minimum
                           && numeric.Value <= maximum;
            items.Add(new ByteOrderProbeItem(
                order,
                RegisterCodec.Format(decoded),
                numeric,
                inRange));
        }

        ByteOrderProbeItem[] candidates = items.Where(item => item.InRange).ToArray();
        RegisterByteOrder? recommended = candidates.Length == 1
            ? candidates[0].ByteOrder
            : null;
        string message = candidates.Length switch
        {
            0 => "四种结果均未落入合理范围，请核对地址、数据类型和范围。",
            1 => $"推荐 {candidates[0].ByteOrder}，但最终仍应以设备手册为准。",
            _ => "多个结果同时落入范围，无法唯一推荐，请结合设备手册判断。"
        };

        return new ByteOrderProbeReport(
            unitId,
            startAddress,
            dataType,
            registers,
            items,
            recommended,
            message);
    }

    /// <summary>
    /// 将推荐布局应用到解析配置；没有唯一推荐时使用用户选择项。
    /// </summary>
    /// <param name="configuration">应用配置。</param>
    /// <param name="report">探测报告。</param>
    /// <param name="selectedOrder">用户选中布局。</param>
    public static void Apply(
        AppConfiguration configuration,
        ByteOrderProbeReport report,
        RegisterByteOrder selectedOrder)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(report);
        configuration.Parsing.DataType = report.DataType;
        configuration.Parsing.ByteOrder = report.RecommendedByteOrder ?? selectedOrder;
    }

    private static double? TryGetNumeric(object value)
    {
        return value switch
        {
            byte number => number,
            sbyte number => number,
            short number => number,
            ushort number => number,
            int number => number,
            uint number => number,
            long number => number,
            ulong number => number,
            float number => number,
            double number => number,
            _ => null
        };
    }
}
