using System.Collections.Concurrent;

using ModbusHelper.Business.Polling;
using ModbusHelper.Core.Configuration;
using ModbusHelper.Core.Enums;
using ModbusHelper.Core.Exceptions;

namespace ModbusHelper.WinForms;

/// <summary>
/// 仅 UI 使用的主窗体。窗体只负责输入收集、调用业务服务、消费事件和刷新控件。
/// </summary>
public sealed partial class MainForm : Form
{
    private readonly AppServices _services;
    private readonly AppConfiguration _configuration;
    private readonly TabControl _mainTabs = new();
    private readonly TabPage _connectionPage = new("主站连接与读写");
    private readonly TabPage _pollingPage = new("轮询任务");
    private readonly TabPage _slavePage = new("从站模拟");
    private readonly TabPage _trendPage = new("趋势图");
    private readonly TabPage _logPage = new("报文日志");
    private readonly System.Windows.Forms.Timer _uiTimer = new() { Interval = 200 };
    private readonly ConcurrentQueue<Action> _uiActions = new();
    private readonly ToolTip _toolTip = new()
    {
        AutoPopDelay = 10000,
        InitialDelay = 500,
        ReshowDelay = 100,
        ShowAlways = true
    };
    private readonly Label _headerStatusLabel = new();
    private readonly ToolStripStatusLabel _statusConnectionLabel = new();
    private readonly ToolStripStatusLabel _statusPollingLabel = new();
    private readonly ToolStripStatusLabel _statusStatisticsLabel = new();
    private readonly CheckBox _toolTipsEnabledCheck = new()
    {
        Text = "悬停提示",
        AutoSize = true,
        Checked = true,
        ForeColor = Color.White,
        BackColor = Color.FromArgb(18, 56, 63),
        Margin = new Padding(12, 2, 4, 2)
    };
    private readonly CheckBox _topMostCheck = new()
    {
        Text = "窗口置顶",
        AutoSize = true,
        Checked = false,
        ForeColor = Color.White,
        BackColor = Color.FromArgb(18, 56, 63),
        Margin = new Padding(8, 2, 4, 2)
    };
    private bool _closing;
    private bool _allowClose;
    private long _lastRequestHistoryRevision = -1;
    private string _lastChartToolTipText = string.Empty;

    internal MainForm(AppServices services)
    {
        _services = services;
        _configuration = services.Configuration;
        AutoScaleMode = AutoScaleMode.Dpi;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1440, 900);
        MinimumSize = new Size(1180, 760);
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        BackColor = Color.FromArgb(244, 247, 248);
        DoubleBuffered = true;

        InitializeShell();
        InitializeConnectionPage();
        InitializePollingPage();
        InitializeSlavePage();
        InitializeTrendPage();
        InitializeLogPage();
        SubscribeServiceEvents();
        ApplyConfigurationToControls();
        InitializeToolTips();

        _uiTimer.Tick += OnUiTimerTick;
        _uiTimer.Start();
        Shown += OnMainFormShown;
        FormClosing += OnMainFormClosing;
        FormClosed += OnMainFormFormClosed;
        FormClosed += (_, _) => _toolTip.Dispose();
        _toolTipsEnabledCheck.CheckedChanged += (_, _) =>
        {
            _toolTip.Active = _toolTipsEnabledCheck.Checked;
            if (!_toolTipsEnabledCheck.Checked)
            {
                _lastChartToolTipText = string.Empty;
                _toolTip.SetToolTip(_trendChart, string.Empty);
            }
        };
        _topMostCheck.CheckedChanged += (_, _) =>
            TopMost = _topMostCheck.Checked;
        _toolTip.Active = true;
    }

    private void InitializeShell()
    {
        Text = "Modbus 调试工具";
        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            BackColor = BackColor
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        Panel header = new()
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            BackColor = Color.FromArgb(18, 56, 63),
            Padding = new Padding(18, 8, 18, 8),
            Margin = new Padding(0)
        };
        TableLayoutPanel headerLayout = new()
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 4,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));

        TableLayoutPanel titleStack = new()
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        titleStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        titleStack.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Label title = new()
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            ForeColor = Color.White,
            Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold),
            Text = "Modbus 调试工具",
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0)
        };
        Label subtitle = new()
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            ForeColor = Color.FromArgb(188, 218, 220),
            Text = "RTU / TCP 主站读写、轮询采集、从站模拟与现场排障",
            TextAlign = ContentAlignment.MiddleLeft,
            Margin = new Padding(0, 1, 0, 0)
        };
        titleStack.Controls.Add(title, 0, 0);
        titleStack.Controls.Add(subtitle, 0, 1);

        _headerStatusLabel.AutoSize = true;
        _headerStatusLabel.Dock = DockStyle.Fill;
        _headerStatusLabel.TextAlign = ContentAlignment.MiddleRight;
        _headerStatusLabel.ForeColor = Color.FromArgb(184, 224, 209);
        _headerStatusLabel.Font = new Font(Font, FontStyle.Bold);
        _headerStatusLabel.Text = "未连接";
        _headerStatusLabel.Margin = new Padding(18, 0, 0, 0);
        headerLayout.Controls.Add(titleStack, 0, 0);
        headerLayout.Controls.Add(_toolTipsEnabledCheck, 1, 0);
        headerLayout.Controls.Add(_topMostCheck, 2, 0);
        headerLayout.Controls.Add(_headerStatusLabel, 3, 0);
        header.Controls.Add(headerLayout);

        _mainTabs.Dock = DockStyle.Fill;
        _mainTabs.Padding = new Point(12, 8);
        _mainTabs.SizeMode = TabSizeMode.Fixed;
        _mainTabs.ItemSize = new Size(UiHelpers.Scale(this, 128), UiHelpers.Scale(this, 34));
        _mainTabs.Margin = new Padding(0);
        _mainTabs.TabPages.Add(_connectionPage);
        _mainTabs.TabPages.Add(_pollingPage);
        _mainTabs.TabPages.Add(_slavePage);
        _mainTabs.TabPages.Add(_trendPage);
        _mainTabs.TabPages.Add(_logPage);

        StatusStrip statusStrip = new()
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            SizingGrip = false,
            BackColor = Color.FromArgb(225, 232, 234)
        };
        _statusConnectionLabel.Text = "未连接";
        _statusPollingLabel.Text = "已停止";
        _statusStatisticsLabel.Text = "请求 0 / 响应 0 / 错误 0";
        statusStrip.Items.Add(new ToolStripStatusLabel("主站：") { ForeColor = Color.DimGray });
        statusStrip.Items.Add(_statusConnectionLabel);
        statusStrip.Items.Add(new ToolStripSeparator());
        statusStrip.Items.Add(new ToolStripStatusLabel("轮询：") { ForeColor = Color.DimGray });
        statusStrip.Items.Add(_statusPollingLabel);
        statusStrip.Items.Add(new ToolStripSeparator());
        statusStrip.Items.Add(_statusStatisticsLabel);

        root.Controls.Add(header, 0, 0);
        root.Controls.Add(_mainTabs, 0, 1);
        root.Controls.Add(statusStrip, 0, 2);
        Controls.Add(root);
    }

    private void InitializeToolTips()
    {
        SetToolTip(_connectionTypeCombo, "选择主站传输方式：RTU 使用串口，TCP 使用网络连接。");
        SetToolTip(_serialPortCombo, "主站使用的串口。虚拟串口自环时可选择 COM1 或 COM2。");
        SetToolTip(_baudRateInput, "RTU 波特率，必须与从站保持一致。");
        SetToolTip(
            _dataBitsInput,
            "RTU 数据位。Modbus RTU 通常使用 8；主站、从站和设备必须完全一致，否则可能出现 CRC 错误或数据截断。");
        SetToolTip(_parityCombo, "RTU 校验方式，必须与从站保持一致。");
        SetToolTip(_stopBitsCombo, "RTU 停止位，必须与从站保持一致。");
        SetToolTip(_tcpHostInput, "TCP 从站的 IP 地址或主机名。");
        SetToolTip(_tcpPortInput, "TCP 从站监听端口，Modbus TCP 常用端口为 502。");
        SetToolTip(_tcpConnectTimeoutInput, "建立 TCP 连接时的连接超时，不等同于应用层应答超时。");
        SetToolTip(
            _transportTimeoutInput,
            "传输层超时：串口一次底层读写或 TCP Socket 一次收发最多等待多久；未收到数据就会报传输层超时。");
        SetToolTip(
            _responseTimeoutInput,
            "应用层应答超时：从发送请求开始，到收到完整 Modbus 响应帧最多等待多久；它可能早于所有重试完成。");
        SetToolTip(
            _retryCountInput,
            "通信失败后的重试次数。填 0 表示只发送 1 次；填 3 表示初次发送失败后最多再发送 3 次。");
        SetToolTip(
            _retryDelayInput,
            "每次失败后、下一次重试前等待的时间；总等待预算约为（重试次数+1）×传输层超时+重试次数×间隔。");
        SetToolTip(_addressBaseCombo, "只影响主站手动读写、轮询和点位表的地址显示与输入；从站模拟使用该页独立的地址基准。0 基为协议地址，1 基为设备手册常见的 40001 风格。");
        SetToolTip(_manualFunctionCombo, "选择要执行的 Modbus 功能码。读写数量上限会随功能码校验。");
        SetToolTip(_manualUnitInput, "读操作允许 1-247；写操作还允许 0（广播写）。");
        SetToolTip(_manualAddressInput, "按当前地址基准输入，发送给 NModbus 前会转换为 0 基地址。");
        SetToolTip(_manualCountInput, "读取数量：寄存器最多 125，线圈或离散输入最多 2000。");
        SetToolTip(_manualDataTypeCombo, "将原始 ushort 寄存器解析为数值或字符串。32 位占 2 个寄存器，64 位占 4 个寄存器。");
        SetToolTip(_manualByteOrderCombo, "字序表示寄存器排列，字节序表示单个寄存器内部高低字节排列。");
        SetToolTip(_manualWriteDataInput, "写操作输入值；多值可使用逗号、分号或空格分隔。");
        SetToolTip(_executeManualButton, "立即执行一次当前参数对应的读写操作。");
        SetToolTip(_connectButton, "按当前连接参数建立主站连接。");
        SetToolTip(_disconnectButton, "断开当前主站连接并停止相关轮询任务。");
        SetToolTip(_requestHexBox, "主站最近发送的请求历史，格式为“【时间】十六进制报文”；新增记录会自动滚动到末尾，重试会逐条显示。");
        SetToolTip(_responseHexBox, "主站最近收到的响应历史，格式为“【时间】十六进制报文”；新增记录会自动滚动到末尾，轮询和重试产生的每个响应都会单独显示。");
        SetToolTip(_parsedValueBox, "显示当前这次操作的解析结果；寄存器会列出原始 ushort 和转换值，线圈或离散输入会按请求起始地址递增列出地址和值。");

        SetToolTip(_pollingGrid, "支持 Ctrl 或 Shift 多选任务；双击单元格可编辑任务参数。");
        SetGridToolTip(_pollingGrid, "启用", "允许此任务被启动；勾选不代表已经运行。启动选中和启动全部只会启动已启用任务。");
        SetGridToolTip(_pollingGrid, "功能码", "轮询只执行读取功能码 01、02、03、04；写入请使用主站连接与读写页。");
        SetGridToolTip(_pollingGrid, "起始地址", "按当前地址基准显示，实际发送前转换为 0 基协议地址。");
        SetGridToolTip(_pollingGrid, "间隔毫秒", "周期轮询间隔，最小 100 毫秒；同一连接下任务会串行执行。");
        SetGridToolTip(_pollingGrid, "数据类型", "将寄存器原始值解析为 Int16、UInt32、Float、字符串等类型。");
        SetGridToolTip(_pollingGrid, "字节序", "ABCD、CDAB、BADC、DCBA 四种寄存器与字节排列方式。");
        SetGridToolTip(_pollingGrid, "趋势", "勾选后将任务采样结果写入趋势数据缓冲区。");
        SetGridToolTip(_pollingGrid, "显示", "控制该任务曲线是否在趋势图中显示。");
        SetGridToolTip(_pollingGrid, "运行", "当前任务是否正在由轮询调度器执行。");

        SetToolTip(_slaveTypeCombo, "选择 RTU 串口从站或 TCP 网络从站。");
        SetToolTip(
            _slaveAddressBaseCombo,
            "只控制从站模拟页面的地址显示和输入，不影响主站连接、轮询任务或点位表。"
            + "0 基显示协议原生地址；1 基显示设备手册地址，例如保持寄存器 40001。");
        SetToolTip(_slaveSerialPortCombo, "从站监听使用的串口，不能与同一时间占用该串口的主站重复。");
        SetToolTip(_slaveBaudRateInput, "RTU 从站波特率，必须与主站一致。");
        SetToolTip(
            _slaveDataBitsInput,
            "RTU 从站数据位，通常为 8；必须与主站和真实设备完全一致，否则可能出现 CRC 错误或数据截断。");
        SetToolTip(_slaveParityCombo, "RTU 从站校验方式，必须与主站一致。");
        SetToolTip(_slaveStopBitsCombo, "RTU 从站停止位，必须与主站一致。");
        SetToolTip(_slaveListenAddressInput, "TCP 从站绑定地址，0.0.0.0 表示监听本机所有网卡。");
        SetToolTip(_slaveListenPortInput, "TCP 从站监听端口，主站连接时使用相同端口。");
        SetToolTip(
            _slaveUnitGrid,
            "每一行是一个独立的模拟从站；表格中的“从站地址”就是 UnitID，必须与主站请求一致。"
            + "此表设置从站启动时的初始值；从站启动中和运行时表格会锁定，停止后才能编辑并在下次启动时加载。"
            + "列名中的 01/02/03/04 就是主站读取时对应的功能码。"
            + "地址使用本页的从站地址基准。多个地址仅显示数量，悬停可查看部分预览，"
            + "双击或点击“编辑初始数据”后按地址逐行查看和编辑；“（空）”表示该区域没有预置值。");
        SetToolTip(
            _slaveRuleGrid,
            "规则只有在“启用”勾选且点击“应用规则”后才参与匹配。"
            + "触发条件是 UnitID、功能码相同，并且主站请求地址范围与规则范围有交集。"
            + "地址使用本页的从站地址基准；连续地址数填 1 时只覆盖起始地址，例如 1 基时 40001 和数量 1 只匹配 40001。");
        SetToolTip(_slaveManualUnitInput, "要修改的模拟从站地址（UnitID）；必须先在上方单元表中配置该地址，新增单元时会自动选中新单元。");
        SetToolTip(_slaveManualAreaCombo, "选择独立的数据区：同一个数字地址在保持寄存器、输入寄存器、线圈、离散输入中分别存储，互不相同。");
        SetToolTip(_slaveManualAddressInput, "模拟数据区的起始地址，遵循本页独立的从站地址基准；0 基从 0 开始，1 基时保持寄存器示例为 40001、输入寄存器为 30001。");
        SetToolTip(_slaveManualCountInput, "要读取或写入的连续元素数量；寄存器是 ushort，线圈和离散输入是 0/1。");
        SetToolTip(
            _slaveManualValuesInput,
            "这里直接修改当前运行中的从站内存，主站下一次读取即可看到新值；停止后临时修改会丢失，重新启动会加载上方初始值。"
            + "多个值用逗号、分号或空格分隔；寄存器可写十进制或 0x 十六进制，线圈使用 0/1 或 ON/OFF。");
        SetToolTip(_slaveManualResultBox, "按时间正序显示最近 100 条读取、写入和错误记录，格式为“【时间】信息”；新增记录会自动滚动到末尾，写入只修改运行中的临时内存。");
        SetToolTip(_slaveFrameBox, "按时间正序显示最近 200 条从站请求/响应，格式为“【时间】方向 | 原始十六进制报文 | 解析含义”；新增记录会自动滚动到末尾。");

        SetToolTip(_trendWindowCombo, "只显示最近 1 分钟、5 分钟或 30 分钟的趋势数据。");
        SetToolTip(_trendPauseCheck, "暂停图表刷新；后台轮询和环形缓冲区仍可继续采样。");
        SetToolTip(_trendCapacityInput, "每条曲线的环形缓冲区容量，超过后覆盖最旧采样点。");
        SetToolTip(_trendSeriesList, "勾选或取消勾选曲线，控制其在图表中的显示状态。");
        SetToolTip(_toolTipsEnabledCheck, "控制主窗口内所有悬停提示；取消后不会显示控件和趋势图提示。");
        SetToolTip(_topMostCheck, "让主窗口保持在其他窗口上方；再次取消即可恢复普通窗口层级。");

        SetToolTip(_logLevelCombo, "按 Debug、Info、Warn、Error 级别过滤日志。");
        SetToolTip(_logSearchInput, "按报文、解析含义或错误提示关键字筛选日志。");
        SetToolTip(_logPauseButton, "暂停或继续采集新日志，已存在的历史记录不会丢失。");
        SetToolTip(_logGrid, "日志包含时间戳、Role、方向、原始十六进制和解析含义。");

        SetButtonToolTip(_connectionPage, "刷新", "重新枚举本机串口。");
        SetButtonToolTip(_connectionPage, "字节序探测", "读取连续寄存器并比较 ABCD、CDAB、BADC、DCBA 四种解析结果。");
        SetButtonToolTip(_pollingPage, "新增任务", "添加一条轮询任务。");
        SetButtonToolTip(_pollingPage, "删除任务", "删除当前选中任务；支持 Ctrl/Shift 多选。");
        SetButtonToolTip(_pollingPage, "点位表", "打开点位表，保存常用点位并生成轮询任务。");
        SetButtonToolTip(_pollingPage, "启动选中", "启动所有选中的轮询任务。");
        SetButtonToolTip(_pollingPage, "启动全部", "启动所有已启用的轮询任务。");
        SetButtonToolTip(_pollingPage, "停止选中", "停止所有选中的轮询任务。");
        SetButtonToolTip(_pollingPage, "停止全部", "停止全部轮询任务。");
        SetButtonToolTip(_pollingPage, "曲线颜色", "为当前任务选择趋势曲线颜色。");
        SetButtonToolTip(_slavePage, "刷新", "重新枚举从站可用串口。");
        SetToolTip(_slaveStartButton, "启动当前配置的 RTU 或 TCP 从站监听。");
        SetToolTip(_slaveStopButton, "停止从站监听并释放串口或 TCP 端口。");
        SetButtonToolTip(_slavePage, "新增单元", "添加一个独立 UnitID 的模拟从站单元。");
        SetButtonToolTip(_slavePage, "删除单元", "删除当前选中的模拟从站单元。");
        SetButtonToolTip(
            _slavePage,
            "编辑初始数据",
            "打开当前从站的明细编辑窗口；四个存储区分别按地址逐行显示，适合查看和编辑大量数据。"
            + "也可以双击主表中的存储区单元格进入对应页签。");
        SetButtonToolTip(_slavePage, "随机数据配置", "配置多条随机数据规则，可选择启动时随机或运行中按周期随机。");
        SetButtonToolTip(_slavePage, "生成随机初始值", "按已勾选“启动时随机”的规则生成初始表数据；只能在从站停止时执行。");
        SetButtonToolTip(_slavePage, "新增规则", "新增一条异常注入规则。");
        SetButtonToolTip(_slavePage, "删除规则", "删除当前选中的异常注入规则。");
        SetButtonToolTip(_slavePage, "应用规则", "将表格中的异常注入规则应用到当前模拟从站。");
        SetButtonToolTip(_slavePage, "读取当前值", "读取模拟从站指定区域的当前值。");
        SetButtonToolTip(_slavePage, "写入模拟数据", "修改模拟从站指定区域的数据。");
        SetButtonToolTip(_trendPage, "导出图片", "将当前趋势图导出为 PNG 文件。");
        SetButtonToolTip(_logPage, "导出 CSV", "以 UTF-8 with BOM 编码导出 RFC 4180 CSV 日志。");
        SetButtonToolTip(_logPage, "导出 TXT", "以 UTF-8 with BOM 编码导出文本日志。");
        SetButtonToolTip(_logPage, "清空日志", "清除当前内存中的日志记录。");
    }

    private void SetToolTip(Control control, string text)
    {
        _toolTip.SetToolTip(control, text);
    }

    private static void SetGridToolTip(DataGridView grid, string headerText, string text)
    {
        DataGridViewColumn? column = grid.Columns
            .Cast<DataGridViewColumn>()
            .FirstOrDefault(item => string.Equals(
                item.HeaderText,
                headerText,
                StringComparison.Ordinal));
        if (column is not null)
        {
            column.HeaderCell.ToolTipText = text;
        }
    }

    private void SetButtonToolTip(Control root, string buttonText, string text)
    {
        foreach (Control control in EnumerateControls(root))
        {
            if (control is Button button
                && string.Equals(button.Text, buttonText, StringComparison.Ordinal))
            {
                _toolTip.SetToolTip(button, text);
            }
        }
    }

    private static IEnumerable<Control> EnumerateControls(Control parent)
    {
        foreach (Control child in parent.Controls)
        {
            yield return child;
            foreach (Control nested in EnumerateControls(child))
            {
                yield return nested;
            }
        }
    }

    private void SubscribeServiceEvents()
    {
        _services.ConnectionManager.StateChanged += (_, args) =>
            QueueUiAction(() => UpdateConnectionState(args.State, args.Message));
        _services.ConnectionManager.CommunicationError += exception =>
            QueueUiAction(() => ShowCommunicationError(exception));
        _services.PollingScheduler.TaskStateChanged += (_, args) =>
            QueueUiAction(() => UpdatePollingTaskState(args));
        _services.PollingScheduler.SampleReceived += (_, args) =>
            QueueUiAction(() => OnPollingSample(args));
        _services.PollingScheduler.TaskFailed += (_, args) =>
            QueueUiAction(() => OnPollingTaskFailed(args));
        _services.SlaveSimulationManager.StateChanged += (_, args) =>
            QueueUiAction(() => UpdateSlaveState(args.IsListening, args.Message));
        _services.SlaveSimulationManager.ErrorOccurred += (_, args) =>
            QueueUiAction(() => ShowSlaveError(args.Message));
    }

    private void QueueUiAction(Action action)
    {
        _uiActions.Enqueue(action);
    }

    private void OnUiTimerTick(object? sender, EventArgs e)
    {
        while (_uiActions.TryDequeue(out Action? action))
        {
            action();
        }

        RefreshCommunicationHistory();
        RefreshSlaveFrames();
        RefreshTrendChart();
        RefreshLogGrid();
        RefreshStatistics();
    }

    private void OnMainFormShown(object? sender, EventArgs e)
    {
        RefreshSerialPorts();
        if (!string.IsNullOrWhiteSpace(_services.ConfigurationWarning))
        {
            MessageBox.Show(
                this,
                _services.ConfigurationWarning,
                "配置提示",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private async void OnMainFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
        if (_closing)
        {
            return;
        }

        _closing = true;
        _uiTimer.Stop();
        try
        {
            CaptureConfigurationFromControls();
            await _services.PollingScheduler.StopAllAsync();
            await _services.SlaveSimulationManager.StopAsync();
            await _services.ConnectionManager.DisconnectAsync();
            await _services.SaveConfigurationAsync();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                $"关闭前释放资源时发生错误：{exception.Message}",
                "资源释放",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        finally
        {
            await _services.DisposeAsync();
            _allowClose = true;
            Close();
        }
    }

    private async void OnMainFormFormClosed(object? sender, FormClosedEventArgs e)
    {
        if (!_closing)
        {
            await _services.DisposeAsync();
        }
    }

    private void UpdateConnectionState(ConnectionState state, string message)
    {
        string text = state switch
        {
            ConnectionState.Disconnected => "未连接",
            ConnectionState.Connecting => "连接中",
            ConnectionState.Connected => "已连接",
            ConnectionState.Disconnecting => "断开中",
            ConnectionState.Faulted => "连接故障",
            _ => state.ToString()
        };
        _headerStatusLabel.Text = text;
        _headerStatusLabel.ForeColor = state == ConnectionState.Connected
            ? Color.FromArgb(184, 224, 209)
            : state == ConnectionState.Faulted
                ? Color.FromArgb(255, 184, 164)
                : Color.FromArgb(218, 226, 228);
        _statusConnectionLabel.Text = string.Equals(text, message, StringComparison.Ordinal)
            ? text
            : $"{text}，{message}";
        _statusConnectionLabel.ForeColor = state == ConnectionState.Faulted
            ? Color.DarkRed
            : Color.DarkSlateGray;
        UpdateConnectionControls(state);
    }

    private void ShowCommunicationError(ModbusCommunicationException exception)
    {
        if (_closing)
        {
            return;
        }

        _statusConnectionLabel.Text = exception.GetDisplayMessage();
        if (exception.ErrorKind is not CommunicationErrorKind.Cancelled)
        {
            _statusConnectionLabel.ForeColor = Color.DarkRed;
        }
    }

    private void RefreshStatistics()
    {
        Business.Statistics.CommunicationStatisticsSnapshot snapshot =
            _services.ConnectionManager.Statistics.GetSnapshot();
        _statusStatisticsLabel.Text =
            $"请求 {snapshot.Requests} / 响应 {snapshot.Responses} / 错误 {snapshot.Errors} / 平均 {snapshot.AverageDurationMilliseconds:F1} ms";
    }

    private void OnPollingTaskFailed(PollingTaskErrorEventArgs args)
    {
        _statusPollingLabel.Text = $"任务“{args.TaskName}”失败：{args.Exception.GetDisplayMessage()}";
        _statusPollingLabel.ForeColor = Color.DarkRed;
    }

    /// <summary>
    /// 捕获当前控件值到纯配置对象。
    /// </summary>
    private void CaptureConfigurationFromControls()
    {
        CaptureConnectionConfiguration();
        CapturePollingConfiguration();
        CaptureSlaveConfiguration();
    }

    private void ApplyConfigurationToControls()
    {
        ApplyConnectionConfiguration();
        ApplyPollingConfiguration();
        ApplySlaveConfiguration();
        ApplyTrendConfiguration();
        RebuildTrendSeries();
        UpdateConnectionState(_services.ConnectionManager.State, "未连接");
    }
}
