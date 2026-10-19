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
    private TableLayoutPanel? _slaveRootLayout;
    private GroupBox? _slaveEndpointGroup;
    private long _lastSlaveFrameRevision = -1;

    private void InitializeSlavePage()
    {
        _slavePage.BackColor = Color.FromArgb(244, 247, 248);
        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
            Padding = new Padding(8)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 160F));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 43F));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 57F));

        _slaveRootLayout = root;
        _slaveEndpointGroup = CreateSlaveEndpointGroup();
        root.Controls.Add(_slaveEndpointGroup, 0, 0);
        SplitContainer configSplit = new()
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            FixedPanel = FixedPanel.Panel1
        };
        UiHelpers.SetInitialSplitterLayout(configSplit, 720, 420, 360);
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
        GroupBox frameGroup = UiHelpers.CreateGroup("从站收发报文");
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
        formatRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 45F));
        formatRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35F));
        formatRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 45F));
        formatRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 65F));
        formatRow.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        formatRow.Controls.Add(UiHelpers.CreateLabel("数据位"), 0, 0);
        formatRow.Controls.Add(_slaveDataBitsInput, 1, 0);
        formatRow.Controls.Add(UiHelpers.CreateLabel("校验"), 2, 0);
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
        GroupBox group = UiHelpers.CreateGroup("从站单元与初始值（显示地址=值）");
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
        toolbar.Controls.Add(UiHelpers.CreateButton("新增单元", AddSlaveUnit, 90));
        toolbar.Controls.Add(UiHelpers.CreateButton("删除单元", DeleteSlaveUnit, 90));
        layout.Controls.Add(toolbar, 0, 0);
        ConfigureSlaveUnitGrid();
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
        _slaveUnitGrid.DataSource = _slaveUnitEditors;
        _slaveUnitGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(SlaveUnitEditor.UnitId),
            HeaderText = "从站地址",
            Width = 72
        });
        _slaveUnitGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(SlaveUnitEditor.HoldingRegisters),
            HeaderText = "保持寄存器",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        });
        _slaveUnitGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(SlaveUnitEditor.InputRegisters),
            HeaderText = "输入寄存器",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        });
        _slaveUnitGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(SlaveUnitEditor.Coils),
            HeaderText = "线圈",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        });
        _slaveUnitGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(SlaveUnitEditor.DiscreteInputs),
            HeaderText = "离散输入",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        });
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
        _slaveRuleGrid.DataSource = _slaveRuleEditors;
        _slaveRuleGrid.CellFormatting += OnSlaveRuleGridCellFormatting;
        _slaveRuleGrid.CellParsing += OnSlaveRuleGridCellParsing;
        _slaveRuleGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            DataPropertyName = nameof(ExceptionRuleEditor.Enabled),
            HeaderText = "启用",
            Width = 52
        });
        _slaveRuleGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(ExceptionRuleEditor.UnitId),
            HeaderText = "从站",
            Width = 58,
            ValueType = typeof(byte)
        });
        DataGridViewComboBoxColumn functionColumn = new()
        {
            DataPropertyName = nameof(ExceptionRuleEditor.Function),
            HeaderText = "功能码",
            Width = 115,
            DataSource = Enum.GetValues<ModbusFunction>()
                .Select(function => new UiOption<ModbusFunction>(
                    function,
                    UiHelpers.ShortFunctionText(function)))
                .ToArray(),
            DisplayMember = nameof(UiOption<ModbusFunction>.Text),
            ValueMember = nameof(UiOption<ModbusFunction>.Value),
            DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton
        };
        _slaveRuleGrid.Columns.Add(functionColumn);
        _slaveRuleGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(ExceptionRuleEditor.StartAddress),
            HeaderText = "地址",
            Width = 78,
            ValueType = typeof(ushort)
        });
        _slaveRuleGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(ExceptionRuleEditor.Count),
            HeaderText = "数量",
            Width = 60,
            ValueType = typeof(ushort)
        });
        _slaveRuleGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(ExceptionRuleEditor.ExceptionCode),
            HeaderText = "异常码",
            Width = 70,
            ValueType = typeof(byte)
        });
    }

    private GroupBox CreateSlaveManualGroup()
    {
        GroupBox group = UiHelpers.CreateGroup("手动修改模拟数据");
        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 112F));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        TableLayoutPanel fields = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 4,
            RowCount = 3,
            Padding = new Padding(4)
        };
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 84F));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 56F));
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
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight
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
        _slaveRootLayout.RowStyles[0].Height = Math.Max(160, preferredHeight + 8);
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
        byte nextId = (byte)Math.Min(
            247,
            _slaveUnitEditors.Count == 0
                ? 1
                : _slaveUnitEditors.Max(editor => editor.UnitId) + 1);
        _slaveUnitEditors.Add(new SlaveUnitEditor
        {
            UnitId = nextId,
            HoldingRegisters = "0=0",
            Coils = "0=0"
        });
    }

    private void DeleteSlaveUnit()
    {
        if (_slaveUnitGrid.CurrentRow?.DataBoundItem is SlaveUnitEditor editor)
        {
            _slaveUnitEditors.Remove(editor);
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

    private void WriteSlaveManualValues()
    {
        try
        {
            SlaveArea area = UiHelpers.GetSelectedValue(
                _slaveManualAreaCombo,
                SlaveArea.HoldingRegisters);
            ModbusFunction function = AreaToFunction(area);
            ushort address = ModbusAddressConverter.ToProtocolAddress(
                (int)_slaveManualAddressInput.Value,
                UiHelpers.GetSelectedValue(_addressBaseCombo, AddressBase.ZeroBased),
                function);
            if (area is SlaveArea.Coils or SlaveArea.DiscreteInputs)
            {
                bool[] values = ParseCoils(_slaveManualValuesInput.Text);
                _services.SlaveSimulationManager.WriteValues(
                    (byte)_slaveManualUnitInput.Value,
                    area,
                    address,
                    values);
                _slaveManualCountInput.Value = values.Length;
            }
            else
            {
                ushort[] values = ParseRegisters(_slaveManualValuesInput.Text);
                _services.SlaveSimulationManager.WriteValues(
                    (byte)_slaveManualUnitInput.Value,
                    area,
                    address,
                    values);
                _slaveManualCountInput.Value = values.Length;
            }

            ReadSlaveManualValues();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "写入模拟值失败",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void ReadSlaveManualValues()
    {
        try
        {
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
                (byte)_slaveManualUnitInput.Value,
                area,
                address,
                count);
            string formatted = values switch
            {
                ushort[] registers => string.Join(
                    ", ",
                    registers.Select(value => $"0x{value:X4}")),
                bool[] coils => string.Join(", ", coils.Select(value => value ? "1" : "0")),
                _ => values.ToString() ?? string.Empty
            };
            _slaveManualResultBox.Text =
                $"区域：{area}{Environment.NewLine}地址：{_slaveManualAddressInput.Value}{Environment.NewLine}值：{formatted}";
        }
        catch (Exception exception)
        {
            _slaveManualResultBox.Text = exception.Message;
        }
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
                .Select(record =>
                    $"{record.Timestamp:HH:mm:ss.fff} {record.Direction,-2} {record.Hex} | {record.Meaning}"));
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
            string[] pair = entry.Split('=', 2, StringSplitOptions.TrimEntries);
            if (pair.Length != 2
                || !int.TryParse(pair[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int display)
                || !ushort.TryParse(
                    pair[1].StartsWith("0x", StringComparison.OrdinalIgnoreCase)
                        ? pair[1][2..]
                        : pair[1],
                    pair[1].StartsWith("0x", StringComparison.OrdinalIgnoreCase)
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
            string[] pair = entry.Split('=', 2, StringSplitOptions.TrimEntries);
            if (pair.Length != 2
                || !int.TryParse(pair[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int display)
                || pair[1] is not ("0" or "1" or "true" or "false" or "ON" or "OFF"
                    or "on" or "off"))
            {
                throw new ArgumentException($"无法解析线圈初始值“{entry}”。");
            }

            bool value = pair[1] is "1" or "true" or "ON" or "on";
            result.Add(new CoilInitialValue
            {
                Address = ModbusAddressConverter.ToProtocolAddress(display, addressBase, function),
                Value = value
            });
        }

        return result;
    }

    private static string FormatRegisterMap(
        IEnumerable<RegisterInitialValue> values,
        AddressBase addressBase,
        ModbusFunction function)
    {
        return string.Join(
            ", ",
            values.Select(value =>
                $"{ModbusAddressConverter.ToDisplayAddress(value.Address, addressBase, function)}={value.Value}"));
    }

    private static string FormatCoilMap(
        IEnumerable<CoilInitialValue> values,
        AddressBase addressBase,
        ModbusFunction function)
    {
        return string.Join(
            ", ",
            values.Select(value =>
                $"{ModbusAddressConverter.ToDisplayAddress(value.Address, addressBase, function)}={(value.Value ? 1 : 0)}"));
    }
}
