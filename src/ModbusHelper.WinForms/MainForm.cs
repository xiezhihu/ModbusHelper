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
    private readonly TabPage _connectionPage = new("连接与读写");
    private readonly TabPage _pollingPage = new("轮询任务");
    private readonly TabPage _slavePage = new("从站模拟");
    private readonly TabPage _trendPage = new("趋势图");
    private readonly TabPage _logPage = new("报文日志");
    private readonly System.Windows.Forms.Timer _uiTimer = new() { Interval = 200 };
    private readonly ConcurrentQueue<Action> _uiActions = new();
    private readonly Label _headerStatusLabel = new();
    private readonly ToolStripStatusLabel _statusConnectionLabel = new();
    private readonly ToolStripStatusLabel _statusPollingLabel = new();
    private readonly ToolStripStatusLabel _statusStatisticsLabel = new();
    private bool _closing;
    private bool _allowClose;

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

        _uiTimer.Tick += OnUiTimerTick;
        _uiTimer.Start();
        Shown += OnMainFormShown;
        FormClosing += OnMainFormClosing;
        FormClosed += OnMainFormFormClosed;
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
            ColumnCount = 2,
            RowCount = 1,
            Margin = new Padding(0),
            Padding = new Padding(0)
        };
        headerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
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
        headerLayout.Controls.Add(_headerStatusLabel, 1, 0);
        header.Controls.Add(headerLayout);

        _mainTabs.Dock = DockStyle.Fill;
        _mainTabs.Padding = new Point(12, 8);
        _mainTabs.SizeMode = TabSizeMode.Fixed;
        _mainTabs.ItemSize = new Size(UiHelpers.Scale(this, 108), UiHelpers.Scale(this, 34));
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
