using System.ComponentModel;
using System.Globalization;
using System.IO.Ports;

using ModbusHelper.Business.Logging;
using ModbusHelper.Business.Slaves;
using ModbusHelper.Core.Configuration;
using ModbusHelper.Core.Enums;
using ModbusHelper.Core.Protocol;

namespace ModbusHelper.WinForms;

public sealed partial class MainForm
{
    private static readonly char[] SlaveMapEntrySeparators = { ',', ';', '\r', '\n' };
    private readonly ComboBox _slaveTypeCombo = UiHelpers.CreateCombo();
    private readonly GroupBox _slaveRtuPanel = UiHelpers.CreateParameterGroup("RTU 串口参数");
    private readonly GroupBox _slaveTcpPanel = UiHelpers.CreateParameterGroup("TCP 监听参数");
    private readonly Panel _slaveParameterHost = new()
    {
        Dock = DockStyle.Top,
        AutoSize = true,
        AutoSizeMode = AutoSizeMode.GrowAndShrink
    };
    private readonly ComboBox _slaveSerialPortCombo = UiHelpers.CreateCombo();
    private readonly NumericUpDown _slaveBaudRateInput = UiHelpers.CreateNumber(1200, 921600, 9600);
    private readonly NumericUpDown _slaveDataBitsInput = UiHelpers.CreateNumber(5, 8, 8);
    private readonly ComboBox _slaveParityCombo = UiHelpers.CreateCombo();
    private readonly ComboBox _slaveStopBitsCombo = UiHelpers.CreateCombo();
    private readonly TextBox _slaveListenAddressInput = UiHelpers.CreateTextBox("0.0.0.0");
    private readonly NumericUpDown _slaveListenPortInput =
        UiHelpers.CreateNumber(1, 65535, 502);
    private readonly Button _slaveStartButton = UiHelpers.CreateButton("启动从站", () => { }, 104);
    private readonly Button _slaveStopButton = UiHelpers.CreateButton("停止从站", () => { }, 104);
    private readonly Label _slaveStateLabel = UiHelpers.CreateLabel("已停止", true);
    private readonly DataGridView _slaveUnitGrid = new();
    private readonly DataGridView _slaveRuleGrid = new();
    private readonly BindingList<SlaveUnitEditor> _slaveUnitEditors = new();
    private readonly BindingList<ExceptionRuleEditor> _slaveRuleEditors = new();
    private readonly LinkedList<string> _slaveManualHistory = new();
    private readonly NumericUpDown _slaveManualUnitInput =
        UiHelpers.CreateNumber(1, 247, 1);
    private readonly ComboBox _slaveManualAreaCombo = UiHelpers.CreateCombo();
    private readonly NumericUpDown _slaveManualAddressInput =
        UiHelpers.CreateNumber(0, 999999, 0);
    private readonly NumericUpDown _slaveManualCountInput =
        UiHelpers.CreateNumber(1, 2000, 1);
    private readonly TextBox _slaveManualValuesInput = UiHelpers.CreateTextBox();
    private readonly TextBox _slaveManualResultBox = CreateResultTextBox();
    private readonly TextBox _slaveFrameBox = CreateResultTextBox();
    private SlaveArea _slaveManualCurrentArea = SlaveArea.HoldingRegisters;
    private TableLayoutPanel? _slaveRootLayout;
    private GroupBox? _slaveEndpointGroup;
    private long _lastSlaveFrameRevision = -1;

    private void InitializeSlavePage()
    {
        _slavePage.BackColor = Color.FromArgb(244, 247, 248);
        _slavePage.AutoScroll = true;
        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Top,
            RowCount = 3,
            ColumnCount = 1,
            Padding = new Padding(8),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 160F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, UiHelpers.Scale(root, 230)));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, UiHelpers.Scale(root, 195)));

        _slaveRootLayout = root;
        _slaveEndpointGroup = CreateSlaveEndpointGroup();
        root.Controls.Add(_slaveEndpointGroup, 0, 0);
        SplitContainer configSplit = new()
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            FixedPanel = FixedPanel.Panel1
        };
        // 初始数据表需要同时显示 UnitID 和四个存储区列，给左侧保留足够宽度；
        // 窄窗口仍允许用户拖动分隔条或使用表格横向滚动查看全部列。
        UiHelpers.SetInitialSplitterLayout(configSplit, 800, 780, 360);
        configSplit.Panel1.Controls.Add(CreateSlaveUnitsGroup());
        configSplit.Panel2.Controls.Add(CreateSlaveRulesGroup());
        root.Controls.Add(configSplit, 0, 1);

        SplitContainer manualSplit = new()
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            FixedPanel = FixedPanel.Panel1
        };
        UiHelpers.SetInitialSplitterLayout(manualSplit, 720, 420, 360);
        manualSplit.Panel1.Controls.Add(CreateSlaveManualGroup());
        GroupBox frameGroup = UiHelpers.CreateGroup("从站报文历史（最近 200 条）");
        frameGroup.Controls.Add(_slaveFrameBox);
        manualSplit.Panel2.Controls.Add(frameGroup);
        root.Controls.Add(manualSplit, 0, 2);
        _slavePage.Controls.Add(root);

        UiHelpers.SetEnumOptions(
            _slaveTypeCombo,
            Enum.GetValues<ConnectionType>(),
            type => type == ConnectionType.Rtu ? "RTU 串口从站" : "TCP 网络从站");
        UiHelpers.SetEnumOptions(
            _slaveParityCombo,
            Enum.GetValues<SerialParity>(),
            parity => parity switch
            {
                SerialParity.None => "无校验",
                SerialParity.Odd => "奇校验",
                SerialParity.Even => "偶校验",
                SerialParity.Mark => "标记校验",
                _ => "空格校验"
            });
        UiHelpers.SetEnumOptions(
            _slaveStopBitsCombo,
            Enum.GetValues<SerialStopBits>(),
            stopBits => stopBits switch
            {
                SerialStopBits.One => "1",
                SerialStopBits.OnePointFive => "1.5",
                _ => "2"
            });
        UiHelpers.SetEnumOptions(
            _slaveManualAreaCombo,
            Enum.GetValues<SlaveArea>(),
            area => area switch
            {
                SlaveArea.Coils => "线圈",
                SlaveArea.DiscreteInputs => "离散输入",
                SlaveArea.HoldingRegisters => "保持寄存器",
                SlaveArea.InputRegisters => "输入寄存器",
                _ => area.ToString()
            });

        _slaveTypeCombo.SelectedIndexChanged += (_, _) => UpdateSlaveTypeVisibility();
        _slaveManualAreaCombo.SelectedIndexChanged += OnSlaveManualAreaChanged;
        _slaveStartButton.Click += async (_, _) => await StartSlaveAsync();
        _slaveStopButton.Click += async (_, _) => await _services.SlaveSimulationManager.StopAsync();
        UpdateSlaveTypeVisibility();
    }

    private GroupBox CreateSlaveEndpointGroup()
    {
        GroupBox group = UiHelpers.CreateGroup("从站监听与启动");
        group.Dock = DockStyle.Top;
        group.AutoSize = true;
        group.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        group.Padding = new Padding(8, 6, 8, 8);
        TableLayoutPanel table = new()
        {
            Dock = DockStyle.Top,
            ColumnCount = 3,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 220F));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 330F));

        TableLayoutPanel typeTable = UiHelpers.CreateFieldTable(1);
        typeTable.Dock = DockStyle.Top;
        typeTable.AutoSize = true;
        typeTable.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        typeTable.ColumnStyles[0].Width = 76F;
        UiHelpers.AddField(typeTable, 0, "类型", _slaveTypeCombo);

        ConfigureSlaveRtuPanel();
        ConfigureSlaveTcpPanel();
        _slaveParameterHost.Controls.Add(_slaveTcpPanel);
        _slaveParameterHost.Controls.Add(_slaveRtuPanel);
        table.Controls.Add(typeTable, 0, 0);
        table.Controls.Add(_slaveParameterHost, 1, 0);

        FlowLayoutPanel actions = new()
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(4, 8, 4, 4)
        };
        _slaveStateLabel.AutoEllipsis = true;
        actions.Controls.Add(_slaveStartButton);
        actions.Controls.Add(_slaveStopButton);
        actions.Controls.Add(_slaveStateLabel);
        table.Controls.Add(actions, 2, 0);
        group.Controls.Add(table);
        return group;
    }

    private void ConfigureSlaveRtuPanel()
    {
        TableLayoutPanel table = UiHelpers.CreateFieldTable(4, autoSize: true);
        table.AutoSize = true;
        TableLayoutPanel portRow = new() { Dock = DockStyle.Fill, ColumnCount = 2, AutoSize = true };
        portRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        portRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 66F));
        portRow.Controls.Add(_slaveSerialPortCombo, 0, 0);
        portRow.Controls.Add(UiHelpers.CreateButton("刷新", RefreshSlaveSerialPorts, 60), 1, 0);
        UiHelpers.AddField(table, 0, "串口", portRow);
        UiHelpers.AddField(table, 1, "波特率", _slaveBaudRateInput);

        TableLayoutPanel formatRow = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            AutoSize = true
        };
        formatRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, UiHelpers.Scale(formatRow, 58)));
        formatRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35F));
        formatRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, UiHelpers.Scale(formatRow, 58)));
        formatRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65F));
        formatRow.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        Label dataBitsLabel = UiHelpers.CreateLabel("数据位");
        dataBitsLabel.AutoSize = false;
        dataBitsLabel.Dock = DockStyle.Fill;
        dataBitsLabel.TextAlign = ContentAlignment.MiddleLeft;
        dataBitsLabel.Margin = new Padding(4, 2, 4, 2);
        Label parityLabel = UiHelpers.CreateLabel("校验");
        parityLabel.AutoSize = false;
        parityLabel.Dock = DockStyle.Fill;
        parityLabel.TextAlign = ContentAlignment.MiddleLeft;
        parityLabel.Margin = new Padding(4, 2, 4, 2);
        formatRow.Controls.Add(dataBitsLabel, 0, 0);
        formatRow.Controls.Add(_slaveDataBitsInput, 1, 0);
        formatRow.Controls.Add(parityLabel, 2, 0);
        formatRow.Controls.Add(_slaveParityCombo, 3, 0);
        UiHelpers.AddField(table, 2, "串口格式", formatRow);
        UiHelpers.AddField(table, 3, "停止位", _slaveStopBitsCombo);
        _slaveRtuPanel.Controls.Add(table);
    }

    private void ConfigureSlaveTcpPanel()
    {
        TableLayoutPanel table = UiHelpers.CreateFieldTable(2, autoSize: true);
        table.AutoSize = true;
        UiHelpers.AddField(table, 0, "监听地址", _slaveListenAddressInput);
        UiHelpers.AddField(table, 1, "监听端口", _slaveListenPortInput);
        _slaveTcpPanel.Controls.Add(table);
    }

    private GroupBox CreateSlaveUnitsGroup()
    {
        GroupBox group = UiHelpers.CreateGroup("模拟设备与初始数据（启动时加载）");
        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            AutoSize = false
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, UiHelpers.Scale(layout, 44)));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        FlowLayoutPanel toolbar = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        toolbar.Controls.Add(UiHelpers.CreateButton("新增单元", AddSlaveUnit, 90));
        toolbar.Controls.Add(UiHelpers.CreateButton("删除单元", DeleteSlaveUnit, 90));
        layout.Controls.Add(toolbar, 0, 0);
        ConfigureSlaveUnitGrid();
        _slaveUnitGrid.MinimumSize = new Size(0, UiHelpers.Scale(_slaveUnitGrid, 62));
        layout.Controls.Add(_slaveUnitGrid, 0, 1);
        group.Controls.Add(layout);
        return group;
    }

    private void ConfigureSlaveUnitGrid()
    {
        _slaveUnitGrid.Dock = DockStyle.Fill;
        _slaveUnitGrid.AutoGenerateColumns = false;
        _slaveUnitGrid.AllowUserToAddRows = false;
        _slaveUnitGrid.RowHeadersVisible = false;
        _slaveUnitGrid.BackgroundColor = Color.White;
        _slaveUnitGrid.ShowCellToolTips = true;
        UiHelpers.ConfigureGridMetrics(_slaveUnitGrid);
        _slaveUnitGrid.DataSource = _slaveUnitEditors;
        AddSlaveUnitColumn(
            nameof(SlaveUnitEditor.UnitId),
            "从站地址",
            "一行代表一个独立从站。这里的从站地址就是 Modbus UnitID，必须与主站请求一致。");
        AddSlaveUnitColumn(
            nameof(SlaveUnitEditor.HoldingRegisters),
            "03 保持寄存器",
            "主站功能码 03 读取此列；格式为“地址 = 值”，例如 40001 = 1234。");
        AddSlaveUnitColumn(
            nameof(SlaveUnitEditor.InputRegisters),
            "04 输入寄存器",
            "主站功能码 04 读取此列；格式为“地址 = 值”，例如 30001 = 1234。");
        AddSlaveUnitColumn(
            nameof(SlaveUnitEditor.Coils),
            "01 线圈",
            "主站功能码 01 读取此列；格式为“地址 = ON/OFF”，例如 1 = ON。");
        AddSlaveUnitColumn(
            nameof(SlaveUnitEditor.DiscreteInputs),
            "02 离散输入",
            "主站功能码 02 读取此列；格式为“地址 = ON/OFF”，例如 10001 = OFF。");
    }

    private void AddSlaveUnitColumn(string propertyName, string headerText, string toolTip)
    {
        DataGridViewTextBoxColumn column = new()
        {
            DataPropertyName = propertyName,
            HeaderText = headerText,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            MinimumWidth = UiHelpers.Scale(_slaveUnitGrid, 100)
        };
        column.HeaderCell.ToolTipText = toolTip;
        if (propertyName == nameof(SlaveUnitEditor.UnitId))
        {
            column.Width = UiHelpers.Scale(_slaveUnitGrid, 88);
            column.MinimumWidth = UiHelpers.Scale(_slaveUnitGrid, 76);
            column.AutoSizeMode = DataGridViewAutoSizeColumnMode.None;
            column.ValueType = typeof(byte);
        }

        _slaveUnitGrid.Columns.Add(column);
    }

    private GroupBox CreateSlaveRulesGroup()
    {
        GroupBox group = UiHelpers.CreateGroup("异常注入规则");
        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        FlowLayoutPanel toolbar = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight
        };
        toolbar.Controls.Add(UiHelpers.CreateButton("新增规则", AddSlaveRule, 90));
        toolbar.Controls.Add(UiHelpers.CreateButton("删除规则", DeleteSlaveRule, 90));
        toolbar.Controls.Add(UiHelpers.CreateButton("应用规则", ApplySlaveRules, 90));
        layout.Controls.Add(toolbar, 0, 0);
        ConfigureSlaveRuleGrid();
        layout.Controls.Add(_slaveRuleGrid, 0, 1);
        group.Controls.Add(layout);
        return group;
    }

    private void ConfigureSlaveRuleGrid()
    {
        _slaveRuleGrid.Dock = DockStyle.Fill;
        _slaveRuleGrid.AutoGenerateColumns = false;
        _slaveRuleGrid.AllowUserToAddRows = false;
        _slaveRuleGrid.RowHeadersVisible = false;
        _slaveRuleGrid.BackgroundColor = Color.White;
        _slaveRuleGrid.ShowCellToolTips = true;
        UiHelpers.ConfigureGridMetrics(_slaveRuleGrid);
        _slaveRuleGrid.DataSource = _slaveRuleEditors;
        _slaveRuleGrid.CellFormatting += OnSlaveRuleGridCellFormatting;
        _slaveRuleGrid.CellParsing += OnSlaveRuleGridCellParsing;
        DataGridViewCheckBoxColumn enabledColumn = new()
        {
            DataPropertyName = nameof(ExceptionRuleEditor.Enabled),
            HeaderText = "启用",
            Width = UiHelpers.Scale(_slaveRuleGrid, 52),
            MinimumWidth = UiHelpers.Scale(_slaveRuleGrid, 48)
        };
        enabledColumn.HeaderCell.ToolTipText = "勾选后规则参与匹配；取消勾选后规则保留，但不会触发异常。";
        _slaveRuleGrid.Columns.Add(enabledColumn);
        DataGridViewTextBoxColumn unitColumn = new()
        {
            DataPropertyName = nameof(ExceptionRuleEditor.UnitId),
            HeaderText = "从站地址",
            Width = Math.Max(
                UiHelpers.Scale(_slaveRuleGrid, 56),
                TextRenderer.MeasureText("从站地址", _slaveRuleGrid.Font).Width
                + UiHelpers.Scale(_slaveRuleGrid, 16)),
            MinimumWidth = TextRenderer.MeasureText("从站地址", _slaveRuleGrid.Font).Width
                + UiHelpers.Scale(_slaveRuleGrid, 16),
            ValueType = typeof(byte)
        };
        unitColumn.HeaderCell.ToolTipText = "这里的从站地址就是 Modbus UnitID，必须与主站请求中的从站地址相同。";
        _slaveRuleGrid.Columns.Add(unitColumn);
        DataGridViewComboBoxColumn functionColumn = new()
        {
            DataPropertyName = nameof(ExceptionRuleEditor.Function),
            HeaderText = "功能码",
            Width = UiHelpers.Scale(_slaveRuleGrid, 112),
            MinimumWidth = UiHelpers.Scale(_slaveRuleGrid, 104),
            DataSource = Enum.GetValues<ModbusFunction>()
                .Select(function => new UiOption<ModbusFunction>(
                    function,
                    UiHelpers.ShortFunctionText(function)))
                .ToArray(),
            DisplayMember = nameof(UiOption<ModbusFunction>.Text),
            ValueMember = nameof(UiOption<ModbusFunction>.Value),
            DisplayStyle = DataGridViewComboBoxDisplayStyle.ComboBox,
            FlatStyle = FlatStyle.Standard,
            DefaultCellStyle = { Padding = Padding.Empty }
        };
        functionColumn.HeaderCell.ToolTipText = "主站请求功能码必须与这里相同，例如 03 读保持寄存器。";
        _slaveRuleGrid.Columns.Add(functionColumn);
        DataGridViewTextBoxColumn addressColumn = new()
        {
            DataPropertyName = nameof(ExceptionRuleEditor.StartAddress),
            HeaderText = "起始地址",
            Width = Math.Max(
                UiHelpers.Scale(_slaveRuleGrid, 58),
                TextRenderer.MeasureText("起始地址", _slaveRuleGrid.Font).Width
                + UiHelpers.Scale(_slaveRuleGrid, 16)),
            MinimumWidth = TextRenderer.MeasureText("起始地址", _slaveRuleGrid.Font).Width
                + UiHelpers.Scale(_slaveRuleGrid, 16),
            ValueType = typeof(ushort)
        };
        addressColumn.HeaderCell.ToolTipText = "规则覆盖范围的第一个地址；1 基模式下例如 40001。";
        _slaveRuleGrid.Columns.Add(addressColumn);
        DataGridViewTextBoxColumn countColumn = new()
        {
            DataPropertyName = nameof(ExceptionRuleEditor.Count),
            HeaderText = "连续地址数",
            Width = UiHelpers.Scale(_slaveRuleGrid, 92),
            MinimumWidth = UiHelpers.Scale(_slaveRuleGrid, 84),
            ValueType = typeof(ushort)
        };
        countColumn.HeaderCell.ToolTipText = "从起始地址开始连续覆盖的地址数量；填 1 只覆盖起始地址。";
        _slaveRuleGrid.Columns.Add(countColumn);
        DataGridViewTextBoxColumn exceptionColumn = new()
        {
            DataPropertyName = nameof(ExceptionRuleEditor.ExceptionCode),
            HeaderText = "异常码",
            Width = UiHelpers.Scale(_slaveRuleGrid, 64),
            MinimumWidth = UiHelpers.Scale(_slaveRuleGrid, 60),
            ValueType = typeof(byte)
        };
        exceptionColumn.HeaderCell.ToolTipText = "命中规则时从站返回的 Modbus 异常码，例如 02=非法数据地址。";
        _slaveRuleGrid.Columns.Add(exceptionColumn);
    }

    private GroupBox CreateSlaveManualGroup()
    {
        GroupBox group = UiHelpers.CreateGroup("运行中手动修改模拟数据");
        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        TableLayoutPanel fields = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 3,
            Padding = new Padding(4),
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, UiHelpers.Scale(fields, 108)));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, UiHelpers.Scale(fields, 78)));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        fields.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        fields.Controls.Add(UiHelpers.CreateLabel("从站地址"), 0, 0);
        fields.Controls.Add(_slaveManualUnitInput, 1, 0);
        fields.Controls.Add(UiHelpers.CreateLabel("存储区域"), 2, 0);
        fields.Controls.Add(_slaveManualAreaCombo, 3, 0);
        fields.Controls.Add(UiHelpers.CreateLabel("起始地址"), 0, 1);
        fields.Controls.Add(_slaveManualAddressInput, 1, 1);
        fields.Controls.Add(UiHelpers.CreateLabel("数量"), 2, 1);
        fields.Controls.Add(_slaveManualCountInput, 3, 1);
        fields.Controls.Add(UiHelpers.CreateLabel("写入值"), 0, 2);
        fields.Controls.Add(_slaveManualValuesInput, 1, 2);
        fields.SetColumnSpan(_slaveManualValuesInput, 3);
        layout.Controls.Add(fields, 0, 0);

        FlowLayoutPanel actions = new()
        {
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        actions.Controls.Add(UiHelpers.CreateButton("读取当前值", ReadSlaveManualValues, 100));
        actions.Controls.Add(UiHelpers.CreateButton("写入模拟数据", WriteSlaveManualValues, 112));
        layout.Controls.Add(actions, 0, 1);
        layout.Controls.Add(_slaveManualResultBox, 0, 2);
        layout.AutoScroll = true;
        group.Controls.Add(layout);
        return group;
    }

    private void ApplySlaveConfiguration()
    {
        SlaveSimulationConfiguration configuration = _configuration.SlaveSimulator;
        UiHelpers.SelectOption(_slaveTypeCombo, configuration.Type);
        UiHelpers.SelectOption(_slaveParityCombo, configuration.Rtu.Parity);
        UiHelpers.SelectOption(_slaveStopBitsCombo, configuration.Rtu.StopBits);
        _slaveBaudRateInput.Value = ClampDecimal(
            configuration.Rtu.BaudRate,
            _slaveBaudRateInput.Minimum,
            _slaveBaudRateInput.Maximum);
        _slaveDataBitsInput.Value = configuration.Rtu.DataBits;
        _slaveListenAddressInput.Text = configuration.Tcp.ListenAddress;
        _slaveListenPortInput.Value = configuration.Tcp.Port;
        _slaveManualAreaCombo.SelectedIndex = 2;
        RefreshSlaveSerialPorts();
        if (!string.IsNullOrWhiteSpace(configuration.Rtu.PortName))
        {
            if (!_slaveSerialPortCombo.Items.Contains(configuration.Rtu.PortName))
            {
                _slaveSerialPortCombo.Items.Add(configuration.Rtu.PortName);
            }

            _slaveSerialPortCombo.SelectedItem = configuration.Rtu.PortName;
        }

        _slaveUnitEditors.RaiseListChangedEvents = false;
        _slaveUnitEditors.Clear();
        foreach (SlaveUnitConfiguration unit in configuration.Units)
        {
            _slaveUnitEditors.Add(SlaveUnitEditor.FromConfiguration(unit, _configuration.AddressBase));
        }

        _slaveUnitEditors.RaiseListChangedEvents = true;
        _slaveUnitEditors.ResetBindings();
        _slaveManualUnitInput.Value = _slaveUnitEditors.Count == 0
            ? 1
            : _slaveUnitEditors[0].UnitId;
        _slaveManualCurrentArea = SlaveArea.HoldingRegisters;
        _slaveManualAddressInput.Value = ModbusAddressConverter.ToDisplayAddress(
            0,
            _configuration.AddressBase,
            ModbusFunction.ReadHoldingRegisters);

        _slaveRuleEditors.RaiseListChangedEvents = false;
        _slaveRuleEditors.Clear();
        foreach (SlaveUnitConfiguration unit in configuration.Units)
        {
            foreach (ExceptionInjectionRule rule in unit.ExceptionRules)
            {
                _slaveRuleEditors.Add(ExceptionRuleEditor.FromConfiguration(rule));
            }
        }

        _slaveRuleEditors.RaiseListChangedEvents = true;
        _slaveRuleEditors.ResetBindings();
        UpdateSlaveTypeVisibility();
        UpdateSlaveControls();
    }

    private void CaptureSlaveConfiguration()
    {
        _slaveUnitGrid.EndEdit();
        _slaveRuleGrid.EndEdit();
        _configuration.SlaveSimulator = BuildSlaveConfiguration();
    }

    private SlaveSimulationConfiguration BuildSlaveConfiguration()
    {
        List<SlaveUnitConfiguration> units = _slaveUnitEditors
            .Select(editor => editor.ToConfiguration(_configuration.AddressBase))
            .ToList();
        foreach (ExceptionRuleEditor rule in _slaveRuleEditors)
        {
            SlaveUnitConfiguration? unit = units.FirstOrDefault(
                item => item.UnitId == rule.UnitId);
            unit?.ExceptionRules.Add(rule.ToConfiguration());
        }

        return new SlaveSimulationConfiguration
        {
            Type = UiHelpers.GetSelectedValue(_slaveTypeCombo, ConnectionType.Tcp),
            Rtu = new RtuConnectionConfiguration
            {
                PortName = _slaveSerialPortCombo.Text,
                BaudRate = (int)_slaveBaudRateInput.Value,
                DataBits = (int)_slaveDataBitsInput.Value,
                Parity = UiHelpers.GetSelectedValue(_slaveParityCombo, SerialParity.None),
                StopBits = UiHelpers.GetSelectedValue(_slaveStopBitsCombo, SerialStopBits.One)
            },
            Tcp = new TcpSlaveConfiguration
            {
                ListenAddress = _slaveListenAddressInput.Text.Trim(),
                Port = (int)_slaveListenPortInput.Value
            },
            Units = units
        };
    }

    private async Task StartSlaveAsync()
    {
        try
        {
            SlaveSimulationConfiguration configuration = BuildSlaveConfiguration();
            ValidationResult validation =
                SlaveSimulationManager.ValidateConfiguration(configuration);
            if (!validation.IsValid)
            {
                MessageBox.Show(
                    this,
                    validation.GetErrorMessage(),
                    "从站参数错误",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            _slaveStartButton.Enabled = false;
            await _services.SlaveSimulationManager.StartAsync(configuration);
            _configuration.SlaveSimulator = configuration;
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "启动从站失败",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            _slaveStartButton.Enabled = true;
        }
    }

    private void UpdateSlaveState(bool listening, string message)
    {
        _slaveStateLabel.Text = listening ? "监听中" : "已停止";
        _slaveStateLabel.ForeColor = listening ? Color.DarkGreen : Color.DarkSlateGray;
        _slaveStartButton.Enabled = !listening;
        _slaveStopButton.Enabled = listening;
        _slaveStateLabel.Text = message;
        _slaveStateLabel.AutoEllipsis = true;
        _slaveStateLabel.ForeColor = listening
            ? Color.DarkGreen
            : message.Contains("错误", StringComparison.Ordinal)
                ? Color.DarkRed
                : Color.DarkSlateGray;
    }

    private void UpdateSlaveControls()
    {
        bool listening = _services.SlaveSimulationManager.IsListening;
        _slaveStartButton.Enabled = !listening;
        _slaveStopButton.Enabled = listening;
    }

    private void UpdateSlaveTypeVisibility()
    {
        bool rtu = UiHelpers.GetSelectedValue(_slaveTypeCombo, ConnectionType.Tcp)
                   == ConnectionType.Rtu;
        _slaveRtuPanel.Visible = rtu;
        _slaveTcpPanel.Visible = !rtu;
        UpdateSlaveEndpointRowHeight();
    }

    private void UpdateSlaveEndpointRowHeight()
    {
        if (_slaveRootLayout is null || _slaveEndpointGroup is null)
        {
            return;
        }

        _slaveEndpointGroup.PerformLayout();
        int preferredHeight = _slaveEndpointGroup.GetPreferredSize(
            new Size(_slaveRootLayout.ClientSize.Width, 0)).Height;
        _slaveRootLayout.RowStyles[0].Height = Math.Max(
            UiHelpers.Scale(_slaveRootLayout, 160),
            preferredHeight + UiHelpers.Scale(_slaveRootLayout, 8));
        _slaveRootLayout.PerformLayout();
    }

    private void RefreshSlaveSerialPorts()
    {
        string selected = _slaveSerialPortCombo.Text;
        string[] ports = SerialPort.GetPortNames().OrderBy(name => name).ToArray();
        _slaveSerialPortCombo.Items.Clear();
        _slaveSerialPortCombo.Items.AddRange(ports);
        if (!string.IsNullOrWhiteSpace(selected) && ports.Contains(selected))
        {
            _slaveSerialPortCombo.SelectedItem = selected;
        }
        else if (ports.Length > 0)
        {
            _slaveSerialPortCombo.SelectedIndex = 0;
        }
    }

    private void AddSlaveUnit()
    {
        HashSet<byte> configuredIds = _slaveUnitEditors
            .Select(editor => editor.UnitId)
            .ToHashSet();
        byte nextId = Enumerable.Range(1, 247)
            .Select(value => (byte)value)
            .FirstOrDefault(unitId => !configuredIds.Contains(unitId));
        if (nextId == 0)
        {
            MessageBox.Show(
                this,
                "最多配置 247 个标准从站单元。",
                "无法新增从站单元",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        AddressBase addressBase = UiHelpers.GetSelectedValue(
            _addressBaseCombo,
            AddressBase.ZeroBased);
        int registerAddress = ModbusAddressConverter.ToDisplayAddress(
            0,
            addressBase,
            ModbusFunction.ReadHoldingRegisters);
        int coilAddress = ModbusAddressConverter.ToDisplayAddress(
            0,
            addressBase,
            ModbusFunction.ReadCoils);
        _slaveUnitEditors.Add(new SlaveUnitEditor
        {
            UnitId = nextId,
            HoldingRegisters = $"{registerAddress} = 0",
            Coils = $"{coilAddress} = OFF"
        });
        _slaveManualUnitInput.Value = nextId;
    }

    private void DeleteSlaveUnit()
    {
        if (_slaveUnitGrid.CurrentRow?.DataBoundItem is SlaveUnitEditor editor)
        {
            _slaveUnitEditors.Remove(editor);
            _slaveManualUnitInput.Value = _slaveUnitEditors.Count == 0
                ? 1
                : _slaveUnitEditors[0].UnitId;
        }
    }

    private void AddSlaveRule()
    {
        byte unitId = _slaveUnitEditors.FirstOrDefault()?.UnitId ?? 1;
        _slaveRuleEditors.Add(new ExceptionRuleEditor
        {
            Enabled = true,
            UnitId = unitId,
            Function = ModbusFunction.ReadHoldingRegisters,
            StartAddress = 0,
            Count = 1,
            ExceptionCode = 2
        });
    }

    private void DeleteSlaveRule()
    {
        if (_slaveRuleGrid.CurrentRow?.DataBoundItem is ExceptionRuleEditor editor)
        {
            _slaveRuleEditors.Remove(editor);
        }
    }

    private void ApplySlaveRules()
    {
        _slaveRuleGrid.EndEdit();
        List<ExceptionInjectionRule> rules = _slaveRuleEditors
            .Select(editor => editor.ToConfiguration())
            .ToList();
        _services.SlaveSimulationManager.UpdateExceptionRules(rules);
        _statusConnectionLabel.Text = $"已应用 {rules.Count} 条从站异常规则";
        _statusConnectionLabel.ForeColor = Color.DarkGreen;
    }

    private void OnSlaveManualAreaChanged(object? sender, EventArgs e)
    {
        SlaveArea nextArea = UiHelpers.GetSelectedValue(
            _slaveManualAreaCombo,
            _slaveManualCurrentArea);
        if (nextArea == _slaveManualCurrentArea)
        {
            return;
        }

        AddressBase addressBase = UiHelpers.GetSelectedValue(
            _addressBaseCombo,
            AddressBase.ZeroBased);
        ModbusFunction previousFunction = AreaToFunction(_slaveManualCurrentArea);
        ModbusFunction nextFunction = AreaToFunction(nextArea);
        ushort protocolAddress;
        try
        {
            protocolAddress = ModbusAddressConverter.ToProtocolAddress(
                (int)_slaveManualAddressInput.Value,
                addressBase,
                previousFunction);
        }
        catch (ArgumentOutOfRangeException)
        {
            protocolAddress = 0;
        }

        _slaveManualAddressInput.Value = ModbusAddressConverter.ToDisplayAddress(
            protocolAddress,
            addressBase,
            nextFunction);
        _slaveManualCurrentArea = nextArea;
    }

    private void WriteSlaveManualValues()
    {
        try
        {
            byte unitId = GetConfiguredManualUnitId();
            SlaveArea area = UiHelpers.GetSelectedValue(
                _slaveManualAreaCombo,
                SlaveArea.HoldingRegisters);
            ModbusFunction function = AreaToFunction(area);
            ushort address = ModbusAddressConverter.ToProtocolAddress(
                (int)_slaveManualAddressInput.Value,
                UiHelpers.GetSelectedValue(_addressBaseCombo, AddressBase.ZeroBased),
                function);
            object valuesToWrite;
            if (area is SlaveArea.Coils or SlaveArea.DiscreteInputs)
            {
                bool[] values = ParseCoils(_slaveManualValuesInput.Text);
                _services.SlaveSimulationManager.WriteValues(
                    unitId,
                    area,
                    address,
                    values);
                _slaveManualCountInput.Value = values.Length;
                valuesToWrite = values;
            }
            else
            {
                ushort[] values = ParseRegisters(_slaveManualValuesInput.Text);
                _services.SlaveSimulationManager.WriteValues(
                    unitId,
                    area,
                    address,
                    values);
                _slaveManualCountInput.Value = values.Length;
                valuesToWrite = values;
            }

            AppendSlaveManualHistory(
                $"写入成功 | 从站地址（UnitID）：{unitId} | 区域：{GetSlaveAreaText(area)}"
                + $"{Environment.NewLine}显示地址：{_slaveManualAddressInput.Value}"
                + $"{Environment.NewLine}协议地址：{address}"
                + $"{Environment.NewLine}值：{FormatSlaveValues(valuesToWrite)}");
        }
        catch (ArgumentOutOfRangeException exception)
            when (exception.ParamName == "inputAddress")
        {
            AppendSlaveManualHistory("写入失败 | " + GetSlaveAddressInputError());
            MessageBox.Show(
                this,
                GetSlaveAddressInputError(),
                "模拟地址错误",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        catch (Exception exception)
        {
            AppendSlaveManualHistory("写入失败 | " + exception.Message);
            MessageBox.Show(
                this,
                exception.Message,
                "写入模拟值失败",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private byte GetConfiguredManualUnitId()
    {
        byte unitId = checked((byte)_slaveManualUnitInput.Value);
        if (!_slaveUnitEditors.Any(editor => editor.UnitId == unitId))
        {
            throw new InvalidOperationException(
                $"单元表中没有配置从站地址（UnitID）{unitId}。请先新增或选择已配置的从站单元。");
        }

        return unitId;
    }

    private void ReadSlaveManualValues()
    {
        try
        {
            byte unitId = GetConfiguredManualUnitId();
            SlaveArea area = UiHelpers.GetSelectedValue(
                _slaveManualAreaCombo,
                SlaveArea.HoldingRegisters);
            ModbusFunction function = AreaToFunction(area);
            ushort address = ModbusAddressConverter.ToProtocolAddress(
                (int)_slaveManualAddressInput.Value,
                UiHelpers.GetSelectedValue(_addressBaseCombo, AddressBase.ZeroBased),
                function);
            ushort count = checked((ushort)_slaveManualCountInput.Value);
            object values = _services.SlaveSimulationManager.ReadValues(
                unitId,
                area,
                address,
                count);
            string formatted = FormatSlaveValues(values);
            AppendSlaveManualHistory(
                $"读取成功 | 从站地址（UnitID）：{_slaveManualUnitInput.Value} | 区域：{GetSlaveAreaText(area)}"
                + $"{Environment.NewLine}显示地址：{_slaveManualAddressInput.Value}"
                + $"{Environment.NewLine}协议地址：{address}"
                + $"{Environment.NewLine}值：{formatted}");
        }
        catch (ArgumentOutOfRangeException exception)
            when (exception.ParamName == "inputAddress")
        {
            AppendSlaveManualHistory("读取失败 | " + GetSlaveAddressInputError());
        }
        catch (Exception exception)
        {
            AppendSlaveManualHistory("读取失败 | " + exception.Message);
        }
    }

    private void AppendSlaveManualHistory(string message)
    {
        string entry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} : {message}";
        _slaveManualHistory.AddFirst(entry);
        while (_slaveManualHistory.Count > 100)
        {
            _slaveManualHistory.RemoveLast();
        }

        _slaveManualResultBox.Text = string.Join(
            Environment.NewLine + Environment.NewLine,
            _slaveManualHistory);
        _slaveManualResultBox.SelectionStart = 0;
        _slaveManualResultBox.SelectionLength = 0;
    }

    private static string FormatSlaveValues(object values)
    {
        return values switch
        {
            ushort[] registers => string.Join(
                ", ",
                registers.Select(value => $"{value} (0x{value:X4})")),
            bool[] coils => string.Join(
                ", ",
                coils.Select(value => value ? "ON (1)" : "OFF (0)")),
            _ => values.ToString() ?? string.Empty
        };
    }

    private string GetSlaveAddressInputError()
    {
        AddressBase addressBase = UiHelpers.GetSelectedValue(
            _addressBaseCombo,
            AddressBase.ZeroBased);
        if (addressBase != AddressBase.OneBased)
        {
            return "协议地址必须在 0 至 65535 范围内。";
        }

        return "地址不能按当前 1 基规则换算。请使用对应手册地址，例如保持寄存器 40001、"
               + "输入寄存器 30001、线圈 1、离散输入 10001。";
    }

    private static string GetSlaveAreaText(SlaveArea area)
    {
        return area switch
        {
            SlaveArea.Coils => "线圈",
            SlaveArea.DiscreteInputs => "离散输入",
            SlaveArea.InputRegisters => "输入寄存器",
            _ => "保持寄存器"
        };
    }

    private static ModbusFunction AreaToFunction(SlaveArea area)
    {
        return area switch
        {
            SlaveArea.Coils => ModbusFunction.WriteSingleCoil,
            SlaveArea.DiscreteInputs => ModbusFunction.ReadDiscreteInputs,
            SlaveArea.HoldingRegisters => ModbusFunction.WriteSingleRegister,
            SlaveArea.InputRegisters => ModbusFunction.ReadInputRegisters,
            _ => ModbusFunction.ReadHoldingRegisters
        };
    }

    private void RefreshSlaveFrames()
    {
        if (_mainTabs.SelectedTab != _slavePage)
        {
            return;
        }

        long revision = _services.LogService.Revision;
        if (revision == _lastSlaveFrameRevision)
        {
            return;
        }

        _lastSlaveFrameRevision = revision;
        IReadOnlyList<ModbusLogRecord> records = _services.LogService.GetSnapshot(
            ModbusLogLevel.Debug,
            limit: 500,
            newestFirst: true);
        _slaveFrameBox.Text = string.Join(
            Environment.NewLine,
            records.Where(record => record.Role == ModbusRole.Slave)
                .Take(200)
                .Select(FormatSlaveFrameHistoryLine));
        if (string.IsNullOrWhiteSpace(_slaveFrameBox.Text))
        {
            _slaveFrameBox.Text = "暂无从站报文记录。";
        }
    }

    private static string FormatSlaveFrameHistoryLine(ModbusLogRecord record)
    {
        string direction = record.Direction == DataDirection.Rx
            ? "接收请求"
            : "发送响应";
        return $"{record.Timestamp:yyyy-MM-dd HH:mm:ss.fff} : {direction} | "
               + $"{record.Hex} | {record.Meaning}";
    }

    private void ShowSlaveError(string message)
    {
        _statusConnectionLabel.Text = $"从站模拟器：{message}";
        _statusConnectionLabel.ForeColor = Color.DarkRed;
    }

    private void OnSlaveRuleGridCellFormatting(
        object? sender,
        DataGridViewCellFormattingEventArgs e)
    {
        if (_slaveRuleGrid.Columns[e.ColumnIndex].DataPropertyName
            != nameof(ExceptionRuleEditor.StartAddress)
            || _slaveRuleGrid.Rows[e.RowIndex].DataBoundItem is not ExceptionRuleEditor rule)
        {
            return;
        }

        e.Value = ModbusAddressConverter.ToDisplayAddress(
                rule.StartAddress,
                UiHelpers.GetSelectedValue(_addressBaseCombo, AddressBase.ZeroBased),
                rule.Function)
            .ToString(CultureInfo.InvariantCulture);
        e.FormattingApplied = true;
    }

    private void OnSlaveRuleGridCellParsing(
        object? sender,
        DataGridViewCellParsingEventArgs e)
    {
        if (_slaveRuleGrid.Columns[e.ColumnIndex].DataPropertyName
            != nameof(ExceptionRuleEditor.StartAddress)
            || _slaveRuleGrid.Rows[e.RowIndex].DataBoundItem is not ExceptionRuleEditor rule
            || e.Value is not string text
            || !int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int display))
        {
            return;
        }

        try
        {
            e.Value = ModbusAddressConverter.ToProtocolAddress(
                display,
                UiHelpers.GetSelectedValue(_addressBaseCombo, AddressBase.ZeroBased),
                rule.Function);
            e.ParsingApplied = true;
        }
        catch (ArgumentOutOfRangeException exception)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "异常规则地址错误",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            e.ParsingApplied = true;
        }
    }

    private void ConvertSlaveEditorAddresses(AddressBase previousBase, AddressBase nextBase)
    {
        foreach (SlaveUnitEditor unit in _slaveUnitEditors)
        {
            unit.HoldingRegisters = SlaveUnitEditor.ConvertRegisterMap(
                unit.HoldingRegisters,
                previousBase,
                nextBase,
                ModbusFunction.ReadHoldingRegisters);
            unit.InputRegisters = SlaveUnitEditor.ConvertRegisterMap(
                unit.InputRegisters,
                previousBase,
                nextBase,
                ModbusFunction.ReadInputRegisters);
            unit.Coils = SlaveUnitEditor.ConvertCoilMap(
                unit.Coils,
                previousBase,
                nextBase,
                ModbusFunction.ReadCoils);
            unit.DiscreteInputs = SlaveUnitEditor.ConvertCoilMap(
                unit.DiscreteInputs,
                previousBase,
                nextBase,
                ModbusFunction.ReadDiscreteInputs);
        }

        _slaveUnitEditors.ResetBindings();
        _slaveRuleGrid.Invalidate();
    }

    private sealed class SlaveUnitEditor
    {
        public byte UnitId { get; set; }

        public string HoldingRegisters { get; set; } = string.Empty;

        public string InputRegisters { get; set; } = string.Empty;

        public string Coils { get; set; } = string.Empty;

        public string DiscreteInputs { get; set; } = string.Empty;

        public SlaveUnitConfiguration ToConfiguration(AddressBase addressBase)
        {
            return new SlaveUnitConfiguration
            {
                UnitId = UnitId,
                HoldingRegisters = ParseRegisterMap(
                    HoldingRegisters,
                    addressBase,
                    ModbusFunction.ReadHoldingRegisters),
                InputRegisters = ParseRegisterMap(
                    InputRegisters,
                    addressBase,
                    ModbusFunction.ReadInputRegisters),
                Coils = ParseCoilMap(Coils, addressBase, ModbusFunction.ReadCoils),
                DiscreteInputs = ParseCoilMap(
                    DiscreteInputs,
                    addressBase,
                    ModbusFunction.ReadDiscreteInputs)
            };
        }

        public static SlaveUnitEditor FromConfiguration(
            SlaveUnitConfiguration unit,
            AddressBase addressBase)
        {
            return new SlaveUnitEditor
            {
                UnitId = unit.UnitId,
                HoldingRegisters = FormatRegisterMap(
                    unit.HoldingRegisters,
                    addressBase,
                    ModbusFunction.ReadHoldingRegisters),
                InputRegisters = FormatRegisterMap(
                    unit.InputRegisters,
                    addressBase,
                    ModbusFunction.ReadInputRegisters),
                Coils = FormatCoilMap(unit.Coils, addressBase, ModbusFunction.ReadCoils),
                DiscreteInputs = FormatCoilMap(
                    unit.DiscreteInputs,
                    addressBase,
                    ModbusFunction.ReadDiscreteInputs)
            };
        }

        public static string ConvertRegisterMap(
            string text,
            AddressBase previousBase,
            AddressBase nextBase,
            ModbusFunction function)
        {
            List<RegisterInitialValue> values = ParseRegisterMap(text, previousBase, function);
            return FormatRegisterMap(values, nextBase, function);
        }

        public static string ConvertCoilMap(
            string text,
            AddressBase previousBase,
            AddressBase nextBase,
            ModbusFunction function)
        {
            List<CoilInitialValue> values = ParseCoilMap(text, previousBase, function);
            return FormatCoilMap(values, nextBase, function);
        }
    }

    private sealed class ExceptionRuleEditor
    {
        public bool Enabled { get; set; }

        public byte UnitId { get; set; }

        public ModbusFunction Function { get; set; }

        public ushort StartAddress { get; set; }

        public ushort Count { get; set; }

        public byte ExceptionCode { get; set; }

        public ExceptionInjectionRule ToConfiguration()
        {
            return new ExceptionInjectionRule
            {
                Enabled = Enabled,
                UnitId = UnitId,
                Function = Function,
                StartAddress = StartAddress,
                Count = Count,
                ExceptionCode = ExceptionCode
            };
        }

        public static ExceptionRuleEditor FromConfiguration(ExceptionInjectionRule rule)
        {
            return new ExceptionRuleEditor
            {
                Enabled = rule.Enabled,
                UnitId = rule.UnitId,
                Function = rule.Function,
                StartAddress = rule.StartAddress,
                Count = rule.Count,
                ExceptionCode = rule.ExceptionCode
            };
        }
    }

    private static List<RegisterInitialValue> ParseRegisterMap(
        string text,
        AddressBase addressBase,
        ModbusFunction function)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new List<RegisterInitialValue>();
        }

        List<RegisterInitialValue> result = new();
        foreach (string entry in text.Split(
                     SlaveMapEntrySeparators,
                     StringSplitOptions.RemoveEmptyEntries))
        {
            if (!TrySplitAddressValue(entry, out string addressText, out string valueText)
                || !int.TryParse(addressText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int display)
                || !ushort.TryParse(
                    valueText.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                        ? valueText[2..]
                        : valueText,
                    valueText.StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                        ? NumberStyles.HexNumber
                        : NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out ushort value))
            {
                throw new ArgumentException($"无法解析寄存器初始值“{entry}”。");
            }

            result.Add(new RegisterInitialValue
            {
                Address = ModbusAddressConverter.ToProtocolAddress(display, addressBase, function),
                Value = value
            });
        }

        return result;
    }

    private static List<CoilInitialValue> ParseCoilMap(
        string text,
        AddressBase addressBase,
        ModbusFunction function)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return new List<CoilInitialValue>();
        }

        List<CoilInitialValue> result = new();
        foreach (string entry in text.Split(
                     SlaveMapEntrySeparators,
                     StringSplitOptions.RemoveEmptyEntries))
        {
            if (!TrySplitAddressValue(entry, out string addressText, out string valueText)
                || !int.TryParse(addressText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int display))
            {
                throw new ArgumentException($"无法解析线圈初始值“{entry}”。");
            }

            bool value = valueText.ToUpperInvariant() switch
            {
                "1" or "TRUE" or "ON" or "ON (1)" => true,
                "0" or "FALSE" or "OFF" or "OFF (0)" => false,
                _ => throw new ArgumentException($"无法解析线圈初始值“{entry}”。")
            };
            result.Add(new CoilInitialValue
            {
                Address = ModbusAddressConverter.ToProtocolAddress(display, addressBase, function),
                Value = value
            });
        }

        return result;
    }

    private static bool TrySplitAddressValue(
        string entry,
        out string addressText,
        out string valueText)
    {
        int separatorIndex = entry.IndexOf('=');
        int separatorLength = 1;
        if (separatorIndex < 0)
        {
            separatorIndex = entry.IndexOf('→');
        }

        if (separatorIndex < 0)
        {
            addressText = string.Empty;
            valueText = string.Empty;
            return false;
        }

        addressText = entry[..separatorIndex].Trim();
        valueText = entry[(separatorIndex + separatorLength)..].Trim();
        if (addressText.StartsWith("地址 ", StringComparison.Ordinal))
        {
            addressText = addressText["地址 ".Length..].Trim();
        }

        if (valueText.StartsWith("值 ", StringComparison.Ordinal))
        {
            valueText = valueText["值 ".Length..].Trim();
        }

        return addressText.Length > 0 && valueText.Length > 0;
    }

    private static string FormatRegisterMap(
        IEnumerable<RegisterInitialValue> values,
        AddressBase addressBase,
        ModbusFunction function)
    {
        return string.Join(
            ", ",
            values.Select(value =>
                $"{ModbusAddressConverter.ToDisplayAddress(value.Address, addressBase, function)}"
                + $" = {value.Value}"));
    }

    private static string FormatCoilMap(
        IEnumerable<CoilInitialValue> values,
        AddressBase addressBase,
        ModbusFunction function)
    {
        return string.Join(
            ", ",
            values.Select(value =>
                $"{ModbusAddressConverter.ToDisplayAddress(value.Address, addressBase, function)}"
                + $" = {(value.Value ? "ON" : "OFF")}"));
    }
}
