using System.Globalization;
using System.IO.Ports;

using ModbusHelper.Business.Connections;
using ModbusHelper.Business.Data;
using ModbusHelper.Core.Enums;
using ModbusHelper.Core.Exceptions;
using ModbusHelper.Core.Models;
using ModbusHelper.Core.Protocol;

namespace ModbusHelper.WinForms;

public sealed partial class MainForm
{
    private static readonly char[] DataSeparators = { ',', ';', ' ', '\r', '\n', '\t' };
    private readonly ComboBox _connectionTypeCombo = UiHelpers.CreateCombo();
    private readonly GroupBox _rtuConnectionPanel = UiHelpers.CreateParameterGroup("RTU 串口参数");
    private readonly GroupBox _tcpConnectionPanel = UiHelpers.CreateParameterGroup("TCP 网络参数");
    private readonly ComboBox _serialPortCombo = UiHelpers.CreateCombo();
    private readonly NumericUpDown _baudRateInput = UiHelpers.CreateNumber(1200, 921600, 9600);
    private readonly NumericUpDown _dataBitsInput = UiHelpers.CreateNumber(5, 8, 8);
    private readonly ComboBox _parityCombo = UiHelpers.CreateCombo();
    private readonly ComboBox _stopBitsCombo = UiHelpers.CreateCombo();
    private readonly TextBox _tcpHostInput = UiHelpers.CreateTextBox("127.0.0.1");
    private readonly NumericUpDown _tcpPortInput = UiHelpers.CreateNumber(1, 65535, 502);
    private readonly NumericUpDown _tcpConnectTimeoutInput =
        UiHelpers.CreateNumber(100, 60000, 3000);
    private readonly NumericUpDown _transportTimeoutInput =
        UiHelpers.CreateNumber(50, 60000, 1000);
    private readonly NumericUpDown _responseTimeoutInput =
        UiHelpers.CreateNumber(50, 120000, 1500);
    private readonly NumericUpDown _retryCountInput = UiHelpers.CreateNumber(0, 10, 1);
    private readonly NumericUpDown _retryDelayInput = UiHelpers.CreateNumber(0, 10000, 100);
    private readonly ComboBox _addressBaseCombo = UiHelpers.CreateCombo();
    private readonly Button _connectButton = UiHelpers.CreateButton("连接", () => { }, 104);
    private readonly Button _disconnectButton = UiHelpers.CreateButton("断开", () => { }, 104);
    private readonly Label _connectionStateLabel = UiHelpers.CreateLabel("未连接", true);

    private readonly ComboBox _manualFunctionCombo = UiHelpers.CreateCombo();
    private readonly NumericUpDown _manualUnitInput = UiHelpers.CreateNumber(0, 255, 1);
    private readonly NumericUpDown _manualAddressInput =
        UiHelpers.CreateNumber(0, 999999, 0);
    private readonly NumericUpDown _manualCountInput = UiHelpers.CreateNumber(1, 2000, 1);
    private readonly ComboBox _manualDataTypeCombo = UiHelpers.CreateCombo();
    private readonly ComboBox _manualByteOrderCombo = UiHelpers.CreateCombo();
    private readonly TextBox _manualWriteDataInput = UiHelpers.CreateTextBox();
    private readonly Label _manualWriteDataLabel = UiHelpers.CreateLabel("写入数据");
    private readonly Button _executeManualButton =
        UiHelpers.CreateButton("执行读取", () => { }, 120);
    private readonly TextBox _requestHexBox = CreateResultTextBox();
    private readonly TextBox _responseHexBox = CreateResultTextBox();
    private readonly TextBox _parsedValueBox = CreateResultTextBox();
    private readonly Label _elapsedLabel = UiHelpers.CreateLabel("耗时：-- ms");
    private TableLayoutPanel? _manualParameterTable;
    private AddressBase _currentAddressBase;
    private bool _addressBaseInitializing;

    private void InitializeConnectionPage()
    {
        _connectionPage.BackColor = Color.FromArgb(244, 247, 248);
        SplitContainer split = new()
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            FixedPanel = FixedPanel.Panel1,
            Margin = new Padding(8)
        };
        UiHelpers.SetInitialSplitterLayout(split, 390, 350, 620);
        split.Panel1.Padding = new Padding(4);
        split.Panel2.Padding = new Padding(4);

        TableLayoutPanel left = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(0, 0, 4, 0)
        };
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        left.Controls.Add(CreateMasterConnectionGroup(), 0, 0);
        left.Controls.Add(CreateAddressBaseGroup(), 0, 1);
        split.Panel1.Controls.Add(left);

        TableLayoutPanel right = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            AutoScroll = true,
            Padding = new Padding(0)
        };
        right.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        right.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
        right.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        right.Controls.Add(CreateManualParameterGroup(), 0, 0);

        FlowLayoutPanel actions = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(2, 4, 2, 2)
        };
        actions.Controls.Add(_executeManualButton);
        actions.Controls.Add(UiHelpers.CreateButton("字节序探测", OpenByteOrderProbe, 124));
        actions.Controls.Add(_elapsedLabel);
        right.Controls.Add(actions, 0, 1);
        right.Controls.Add(CreateResultGroup(), 0, 2);
        split.Panel2.Controls.Add(right);
        _connectionPage.Controls.Add(split);

        UiHelpers.SetEnumOptions(
            _connectionTypeCombo,
            Enum.GetValues<ConnectionType>(),
            type => type == ConnectionType.Rtu ? "RTU 串口" : "TCP 网络");
        UiHelpers.SetEnumOptions(
            _parityCombo,
            Enum.GetValues<SerialParity>(),
            parity => parity switch
            {
                SerialParity.None => "无校验",
                SerialParity.Odd => "奇校验",
                SerialParity.Even => "偶校验",
                SerialParity.Mark => "标记校验",
                SerialParity.Space => "空格校验",
                _ => parity.ToString()
            });
        UiHelpers.SetEnumOptions(
            _stopBitsCombo,
            Enum.GetValues<SerialStopBits>(),
            stopBits => stopBits switch
            {
                SerialStopBits.One => "1",
                SerialStopBits.OnePointFive => "1.5",
                SerialStopBits.Two => "2",
                _ => stopBits.ToString()
            });
        UiHelpers.SetEnumOptions(
            _addressBaseCombo,
            Enum.GetValues<AddressBase>(),
            addressBase => addressBase == AddressBase.ZeroBased
                ? "0 基（协议原生）"
                : "1 基（40001 风格）");
        UiHelpers.SetEnumOptions(
            _manualFunctionCombo,
            Enum.GetValues<ModbusFunction>(),
            function => function.GetChineseName());
        UiHelpers.SetEnumOptions(
            _manualDataTypeCombo,
            Enum.GetValues<DataValueType>(),
            UiHelpers.DataTypeText);
        UiHelpers.SetEnumOptions(
            _manualByteOrderCombo,
            Enum.GetValues<RegisterByteOrder>(),
            UiHelpers.ByteOrderText);

        _connectionTypeCombo.SelectedIndexChanged += (_, _) => UpdateConnectionTypeVisibility();
        _executeManualButton.Click += async (_, _) => await ExecuteManualOperationAsync();
        _manualFunctionCombo.SelectedIndexChanged += (_, _) => UpdateManualFunctionMode();
        _manualAddressInput.ValueChanged += (_, _) => UpdateManualCountLimits();
        _addressBaseCombo.SelectedIndexChanged += (_, _) => OnAddressBaseChanged();
    }

    private GroupBox CreateMasterConnectionGroup()
    {
        GroupBox group = UiHelpers.CreateGroup("主站连接");
        group.Dock = DockStyle.Top;
        group.AutoSize = true;
        group.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        group.Padding = new Padding(8, 6, 8, 8);
        Panel scroll = new()
        {
            Dock = DockStyle.Top,
            AutoScroll = true,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(0)
        };
        TableLayoutPanel table = new()
        {
            Dock = DockStyle.Top,
            ColumnCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(0)
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));

        TableLayoutPanel typeRow = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = new Padding(4, 4, 4, 6)
        };
        typeRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96F));
        typeRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        typeRow.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
        typeRow.Controls.Add(UiHelpers.CreateLabel("连接类型"), 0, 0);
        typeRow.Controls.Add(_connectionTypeCombo, 1, 0);
        table.Controls.Add(typeRow);

        ConfigureRtuPanel();
        ConfigureTcpPanel();
        table.Controls.Add(_rtuConnectionPanel);
        table.Controls.Add(_tcpConnectionPanel);

        GroupBox timeoutGroup = UiHelpers.CreateParameterGroup("超时与重试");
        TableLayoutPanel timeoutTable = UiHelpers.CreateFieldTable(4, autoSize: true);
        timeoutTable.ColumnStyles[0].Width = 112F;
        UiHelpers.AddField(timeoutTable, 0, "传输层超时", AddUnit(_transportTimeoutInput, "毫秒"));
        UiHelpers.AddField(timeoutTable, 1, "应用层应答", AddUnit(_responseTimeoutInput, "毫秒"));
        UiHelpers.AddField(timeoutTable, 2, "重试次数", _retryCountInput);
        UiHelpers.AddField(timeoutTable, 3, "重试间隔", AddUnit(_retryDelayInput, "毫秒"));
        timeoutGroup.Controls.Add(timeoutTable);
        table.Controls.Add(timeoutGroup);

        FlowLayoutPanel buttons = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Padding = new Padding(0),
            AutoSize = true
        };
        _connectionStateLabel.AutoSize = true;
        _connectionStateLabel.AutoEllipsis = true;
        _connectionStateLabel.Margin = new Padding(8, 10, 4, 4);
        buttons.Controls.Add(_connectButton);
        buttons.Controls.Add(_disconnectButton);
        buttons.Controls.Add(_connectionStateLabel);
        buttons.Margin = new Padding(4, 8, 4, 4);
        table.Controls.Add(buttons);

        _connectButton.Click += async (_, _) => await ConnectMasterAsync();
        _disconnectButton.Click += async (_, _) => await DisconnectMasterAsync();
        scroll.Controls.Add(table);
        group.Controls.Add(scroll);
        return group;
    }

    private void ConfigureRtuPanel()
    {
        TableLayoutPanel table = UiHelpers.CreateFieldTable(5, autoSize: true);
        table.ColumnStyles[0].Width = 92F;
        table.Dock = DockStyle.Top;
        table.AutoSize = true;
        TableLayoutPanel portRow = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoSize = true
        };
        portRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        portRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 74F));
        portRow.Controls.Add(_serialPortCombo, 0, 0);
        portRow.Controls.Add(UiHelpers.CreateButton("刷新", RefreshSerialPorts, 66), 1, 0);
        UiHelpers.AddField(table, 0, "串口", portRow);
        UiHelpers.AddField(table, 1, "波特率", _baudRateInput);
        UiHelpers.AddField(table, 2, "数据位", _dataBitsInput);
        UiHelpers.AddField(table, 3, "校验位", _parityCombo);
        UiHelpers.AddField(table, 4, "停止位", _stopBitsCombo);
        _rtuConnectionPanel.Controls.Add(table);
    }

    private void ConfigureTcpPanel()
    {
        TableLayoutPanel table = UiHelpers.CreateFieldTable(3, autoSize: true);
        table.ColumnStyles[0].Width = 92F;
        table.Dock = DockStyle.Top;
        table.AutoSize = true;
        UiHelpers.AddField(table, 0, "主机", _tcpHostInput);
        UiHelpers.AddField(table, 1, "端口", _tcpPortInput);
        UiHelpers.AddField(table, 2, "连接超时", AddUnit(_tcpConnectTimeoutInput, "毫秒"));
        _tcpConnectionPanel.Controls.Add(table);
    }

    private GroupBox CreateAddressBaseGroup()
    {
        GroupBox group = UiHelpers.CreateGroup("地址基准");
        group.Dock = DockStyle.Top;
        group.AutoSize = true;
        group.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        group.Padding = new Padding(8, 4, 8, 4);
        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Top,
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(0)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 92F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.Controls.Add(UiHelpers.CreateLabel("界面基准"), 0, 0);
        layout.Controls.Add(_addressBaseCombo, 1, 0);
        group.Controls.Add(layout);
        return group;
    }

    private GroupBox CreateManualParameterGroup()
    {
        GroupBox group = UiHelpers.CreateGroup("手动读写参数");
        group.Dock = DockStyle.Top;
        group.AutoSize = true;
        group.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        TableLayoutPanel table = UiHelpers.CreateFieldTable(7, autoSize: true);
        table.ColumnStyles[0].Width = 126F;
        _manualParameterTable = table;
        UiHelpers.AddField(table, 0, "功能码", _manualFunctionCombo);
        UiHelpers.AddField(table, 1, "从站地址", _manualUnitInput);
        UiHelpers.AddField(table, 2, "起始地址", _manualAddressInput);
        UiHelpers.AddField(table, 3, "数量", _manualCountInput);
        UiHelpers.AddField(table, 4, "数据类型", _manualDataTypeCombo);
        UiHelpers.AddField(table, 5, "字序与字节序", _manualByteOrderCombo);
        UiHelpers.AddField(table, 6, _manualWriteDataLabel.Text, _manualWriteDataInput);
        group.Controls.Add(table);
        return group;
    }

    private GroupBox CreateResultGroup()
    {
        GroupBox group = UiHelpers.CreateGroup("执行结果");
        TableLayoutPanel table = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2
        };
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 55F));
        table.RowStyles.Add(new RowStyle(SizeType.Percent, 45F));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

        GroupBox requestGroup = UiHelpers.CreateGroup("请求报文（十六进制）");
        requestGroup.Controls.Add(_requestHexBox);
        GroupBox responseGroup = UiHelpers.CreateGroup("响应报文（十六进制）");
        responseGroup.Controls.Add(_responseHexBox);
        GroupBox parsedGroup = UiHelpers.CreateGroup("解析结果");
        parsedGroup.Controls.Add(_parsedValueBox);
        table.Controls.Add(requestGroup, 0, 0);
        table.Controls.Add(responseGroup, 1, 0);
        table.Controls.Add(parsedGroup, 0, 1);
        table.SetColumnSpan(parsedGroup, 2);
        group.Controls.Add(table);
        return group;
    }

    private static TextBox CreateResultTextBox()
    {
        return new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Both,
            WordWrap = false,
            BackColor = Color.White,
            Font = new Font("Consolas", 9F)
        };
    }

    private static TableLayoutPanel AddUnit(Control input, string unit)
    {
        TableLayoutPanel table = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            AutoSize = true
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 56F));
        table.Controls.Add(input, 0, 0);
        table.Controls.Add(UiHelpers.CreateLabel(unit), 1, 0);
        return table;
    }

    private void ApplyConnectionConfiguration()
    {
        _addressBaseInitializing = true;
        UiHelpers.SelectOption(_connectionTypeCombo, _configuration.Connection.Type);
        UiHelpers.SelectOption(_addressBaseCombo, _configuration.AddressBase);
        UiHelpers.SelectOption(_parityCombo, _configuration.Connection.Rtu.Parity);
        UiHelpers.SelectOption(_stopBitsCombo, _configuration.Connection.Rtu.StopBits);
        _currentAddressBase = _configuration.AddressBase;
        _addressBaseInitializing = false;

        SelectSerialPort(_configuration.Connection.Rtu.PortName);
        _baudRateInput.Value = ClampDecimal(
            _configuration.Connection.Rtu.BaudRate,
            _baudRateInput.Minimum,
            _baudRateInput.Maximum);
        _dataBitsInput.Value = _configuration.Connection.Rtu.DataBits;
        _tcpHostInput.Text = _configuration.Connection.Tcp.Host;
        _tcpPortInput.Value = _configuration.Connection.Tcp.Port;
        _tcpConnectTimeoutInput.Value = ClampDecimal(
            _configuration.Connection.Tcp.ConnectTimeoutMilliseconds,
            _tcpConnectTimeoutInput.Minimum,
            _tcpConnectTimeoutInput.Maximum);
        _transportTimeoutInput.Value = ClampDecimal(
            _configuration.Connection.TransportTimeoutMilliseconds,
            _transportTimeoutInput.Minimum,
            _transportTimeoutInput.Maximum);
        _responseTimeoutInput.Value = ClampDecimal(
            _configuration.Connection.ApplicationResponseTimeoutMilliseconds,
            _responseTimeoutInput.Minimum,
            _responseTimeoutInput.Maximum);
        _retryCountInput.Value = _configuration.Connection.RetryCount;
        _retryDelayInput.Value = _configuration.Connection.RetryDelayMilliseconds;
        UiHelpers.SelectOption(_manualDataTypeCombo, _configuration.Parsing.DataType);
        UiHelpers.SelectOption(_manualByteOrderCombo, _configuration.Parsing.ByteOrder);
        _manualFunctionCombo.SelectedIndex = 2;
        _manualAddressInput.Value = ModbusAddressConverter.ToDisplayAddress(
            0,
            _currentAddressBase,
            ModbusFunction.ReadHoldingRegisters);
        UpdateConnectionTypeVisibility();
        UpdateManualFunctionMode();
        UpdateConnectionControls(_services.ConnectionManager.State);
    }

    private void CaptureConnectionConfiguration()
    {
        Core.Configuration.ConnectionConfiguration connection = _configuration.Connection;
        connection.Type = UiHelpers.GetSelectedValue(_connectionTypeCombo, ConnectionType.Tcp);
        connection.Rtu.PortName = _serialPortCombo.Text;
        connection.Rtu.BaudRate = (int)_baudRateInput.Value;
        connection.Rtu.DataBits = (int)_dataBitsInput.Value;
        connection.Rtu.Parity = UiHelpers.GetSelectedValue(_parityCombo, SerialParity.None);
        connection.Rtu.StopBits = UiHelpers.GetSelectedValue(_stopBitsCombo, SerialStopBits.One);
        connection.Tcp.Host = _tcpHostInput.Text.Trim();
        connection.Tcp.Port = (int)_tcpPortInput.Value;
        connection.Tcp.ConnectTimeoutMilliseconds = (int)_tcpConnectTimeoutInput.Value;
        connection.TransportTimeoutMilliseconds = (int)_transportTimeoutInput.Value;
        connection.ApplicationResponseTimeoutMilliseconds = (int)_responseTimeoutInput.Value;
        connection.RetryCount = (int)_retryCountInput.Value;
        connection.RetryDelayMilliseconds = (int)_retryDelayInput.Value;
        _configuration.AddressBase = UiHelpers.GetSelectedValue(
            _addressBaseCombo,
            AddressBase.ZeroBased);
        _configuration.Parsing.DataType = UiHelpers.GetSelectedValue(
            _manualDataTypeCombo,
            DataValueType.UInt16);
        _configuration.Parsing.ByteOrder = UiHelpers.GetSelectedValue(
            _manualByteOrderCombo,
            RegisterByteOrder.ABCD);
    }

    private async Task ConnectMasterAsync()
    {
        try
        {
            CaptureConnectionConfiguration();
            ValidationResult validation =
                ConnectionManager.ValidateConnection(_configuration.Connection);
            if (!validation.IsValid)
            {
                MessageBox.Show(
                    this,
                    validation.GetErrorMessage(),
                    "连接参数错误",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            _connectButton.Enabled = false;
            await _services.ConnectionManager.ConnectAsync(_configuration.Connection);
        }
        catch (ModbusCommunicationException exception)
        {
            MessageBox.Show(
                this,
                exception.GetDisplayMessage(),
                "连接失败",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
        finally
        {
            _connectButton.Enabled = true;
        }
    }

    private async Task DisconnectMasterAsync()
    {
        await _services.PollingScheduler.StopAllAsync();
        await _services.ConnectionManager.DisconnectAsync();
    }

    private void UpdateConnectionControls(ConnectionState state)
    {
        bool connected = state == ConnectionState.Connected;
        _connectButton.Enabled = !connected && state != ConnectionState.Connecting;
        _disconnectButton.Enabled = connected || state == ConnectionState.Connecting;
        _connectionStateLabel.Text = state switch
        {
            ConnectionState.Disconnected => "未连接",
            ConnectionState.Connecting => "连接中",
            ConnectionState.Connected => "已连接",
            ConnectionState.Disconnecting => "断开中",
            ConnectionState.Faulted => "故障",
            _ => state.ToString()
        };
        _connectionStateLabel.ForeColor = state == ConnectionState.Connected
            ? Color.DarkGreen
            : state == ConnectionState.Faulted
                ? Color.DarkRed
                : Color.DarkSlateGray;
    }

    private void UpdateConnectionTypeVisibility()
    {
        bool isRtu = UiHelpers.GetSelectedValue(_connectionTypeCombo, ConnectionType.Tcp)
                     == ConnectionType.Rtu;
        _rtuConnectionPanel.Visible = isRtu;
        _tcpConnectionPanel.Visible = !isRtu;
    }

    private void RefreshSerialPorts()
    {
        string selected = _serialPortCombo.Text;
        string[] ports = SerialPort.GetPortNames().OrderBy(name => name).ToArray();
        _serialPortCombo.Items.Clear();
        _serialPortCombo.Items.AddRange(ports);
        if (!string.IsNullOrWhiteSpace(selected) && ports.Contains(selected))
        {
            _serialPortCombo.SelectedItem = selected;
        }
        else if (ports.Length > 0)
        {
            _serialPortCombo.SelectedIndex = 0;
        }
    }

    private void SelectSerialPort(string portName)
    {
        if (string.IsNullOrWhiteSpace(portName))
        {
            return;
        }

        if (!_serialPortCombo.Items.Contains(portName))
        {
            _serialPortCombo.Items.Add(portName);
        }

        _serialPortCombo.SelectedItem = portName;
    }

    private void UpdateManualFunctionMode()
    {
        ModbusFunction function = UiHelpers.GetSelectedValue(
            _manualFunctionCombo,
            ModbusFunction.ReadHoldingRegisters);
        bool write = function.IsWrite();
        _executeManualButton.Text = write ? "执行写入" : "执行读取";
        _manualWriteDataInput.Visible = write;
        _manualWriteDataLabel.Visible = write;
        if (_manualParameterTable is not null)
        {
            foreach (Control control in _manualParameterTable.Controls)
            {
                if (_manualParameterTable.GetRow(control) == 6)
                {
                    control.Visible = write;
                }
            }

            _manualParameterTable.RowStyles[6] = new RowStyle(
                SizeType.Absolute,
                write ? 34F : 0F);
        }

        _manualCountInput.Enabled = function is not (ModbusFunction.WriteSingleCoil
            or ModbusFunction.WriteSingleRegister);
        if (function is ModbusFunction.WriteSingleCoil or ModbusFunction.WriteSingleRegister)
        {
            _manualCountInput.Value = 1;
        }

        UpdateManualCountLimits();
    }

    private void UpdateManualCountLimits()
    {
        ModbusFunction function = UiHelpers.GetSelectedValue(
            _manualFunctionCombo,
            ModbusFunction.ReadHoldingRegisters);
        decimal maximum = function switch
        {
            ModbusFunction.ReadCoils or ModbusFunction.ReadDiscreteInputs => 2000,
            ModbusFunction.ReadHoldingRegisters or ModbusFunction.ReadInputRegisters => 125,
            ModbusFunction.WriteMultipleCoils => 1968,
            ModbusFunction.WriteMultipleRegisters => 123,
            _ => 1
        };
        _manualCountInput.Maximum = maximum;
        if (_manualCountInput.Value > maximum)
        {
            _manualCountInput.Value = maximum;
        }
    }

    private void OnAddressBaseChanged()
    {
        if (_addressBaseInitializing)
        {
            return;
        }

        AddressBase next = UiHelpers.GetSelectedValue(
            _addressBaseCombo,
            AddressBase.ZeroBased);
        if (next == _currentAddressBase)
        {
            return;
        }

        ModbusFunction function = UiHelpers.GetSelectedValue(
            _manualFunctionCombo,
            ModbusFunction.ReadHoldingRegisters);
        AddressBase previousBase = _currentAddressBase;
        try
        {
            ushort protocol = ModbusAddressConverter.ToProtocolAddress(
                (int)_manualAddressInput.Value,
                previousBase,
                function);
            ModbusFunction slaveFunction = AreaToFunction(UiHelpers.GetSelectedValue(
                _slaveManualAreaCombo,
                SlaveArea.HoldingRegisters));
            ushort? slaveProtocol = null;
            try
            {
                slaveProtocol = ModbusAddressConverter.ToProtocolAddress(
                    (int)_slaveManualAddressInput.Value,
                    previousBase,
                    slaveFunction);
                ConvertSlaveEditorAddresses(previousBase, next);
            }
            catch (ArgumentException)
            {
                // 从站初始值文本尚不完整时，仅切换主站地址基准。
            }

            _currentAddressBase = next;
            _configuration.AddressBase = next;
            _manualAddressInput.Value = ModbusAddressConverter.ToDisplayAddress(
                protocol,
                next,
                function);
            if (slaveProtocol.HasValue)
            {
                _slaveManualAddressInput.Value = ModbusAddressConverter.ToDisplayAddress(
                    slaveProtocol.Value,
                    next,
                    slaveFunction);
            }
            UpdatePollingAddressDisplays();
        }
        catch (ArgumentOutOfRangeException)
        {
            _currentAddressBase = next;
            _configuration.AddressBase = next;
        }
    }

    private async Task ExecuteManualOperationAsync()
    {
        try
        {
            ModbusFunction function = UiHelpers.GetSelectedValue(
                _manualFunctionCombo,
                ModbusFunction.ReadHoldingRegisters);
            byte unitId = checked((byte)_manualUnitInput.Value);
            ushort protocolAddress = ModbusAddressConverter.ToProtocolAddress(
                (int)_manualAddressInput.Value,
                UiHelpers.GetSelectedValue(_addressBaseCombo, AddressBase.ZeroBased),
                function);
            ushort count = checked((ushort)_manualCountInput.Value);
            IReadOnlyList<string> warnings = function.IsWrite()
                ? _services.ConnectionManager.ValidateWriteParameters(
                    function,
                    unitId,
                    protocolAddress,
                    count).Warnings
                : ConnectionManager.ValidateReadParameters(
                    function,
                    unitId,
                    protocolAddress,
                    count).Warnings;
            ShowWarnings(warnings);

            ModbusOperationResult result;
            if (function.IsWrite())
            {
                bool[] coils = Array.Empty<bool>();
                ushort[] registers = Array.Empty<ushort>();
                string input = _manualWriteDataInput.Text.Trim();
                if (function is ModbusFunction.WriteSingleCoil or ModbusFunction.WriteMultipleCoils)
                {
                    coils = ParseCoils(input);
                    _manualCountInput.Value = coils.Length;
                }
                else
                {
                    registers = ParseRegisters(input);
                    _manualCountInput.Value = registers.Length;
                }

                count = checked((ushort)Math.Max(coils.Length, registers.Length));
                result = await _services.ConnectionManager.WriteAsync(
                    function,
                    unitId,
                    protocolAddress,
                    coils,
                    registers);
            }
            else
            {
                result = await _services.ConnectionManager.ReadAsync(
                    function,
                    unitId,
                    protocolAddress,
                    count);
            }

            ShowOperationResult(result, protocolAddress);
        }
        catch (ModbusCommunicationException exception)
        {
            _requestHexBox.Text = exception.RequestHex ?? string.Empty;
            _responseHexBox.Text = exception.ResponseHex ?? string.Empty;
            _parsedValueBox.Text = exception.GetDisplayMessage();
            _elapsedLabel.Text = "耗时：-- ms";
            MessageBox.Show(
                this,
                exception.GetDisplayMessage(),
                "操作失败",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "输入错误",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void ShowOperationResult(ModbusOperationResult result, ushort protocolAddress)
    {
        _requestHexBox.Text = result.RequestHex;
        _responseHexBox.Text = result.ResponseHex;
        _elapsedLabel.Text = $"耗时：{result.ElapsedMilliseconds} ms";
        ModbusFunction function = result.Function;
        AddressBase addressBase = UiHelpers.GetSelectedValue(
            _addressBaseCombo,
            AddressBase.ZeroBased);
        if (result.Coils is { } coils)
        {
            _parsedValueBox.Text = string.Join(
                Environment.NewLine,
                coils.Select((value, index) =>
                {
                    int displayAddress = ModbusAddressConverter.ToDisplayAddress(
                        checked((ushort)(protocolAddress + index)),
                        addressBase,
                        function);
                    return $"{displayAddress} = {(value ? "1 / ON" : "0 / OFF")}";
                }));
            return;
        }

        if (result.Registers is not { } registers)
        {
            _parsedValueBox.Text = result.ParsedValue;
            return;
        }

        string raw = string.Join(", ", registers.Select(value => $"0x{value:X4}"));
        string decoded = "无法解析";
        try
        {
            object value = RegisterCodec.Decode(
                registers,
                UiHelpers.GetSelectedValue(_manualDataTypeCombo, DataValueType.UInt16),
                UiHelpers.GetSelectedValue(_manualByteOrderCombo, RegisterByteOrder.ABCD));
            decoded = RegisterCodec.Format(value);
        }
        catch (ArgumentException)
        {
            // 数量不足以容纳指定类型时仍显示原始寄存器。
        }

        _parsedValueBox.Text =
            $"原始值：{raw}{Environment.NewLine}解析值：{decoded}{Environment.NewLine}起始协议地址：{protocolAddress}";
    }

    private void ShowWarnings(IReadOnlyList<string> warnings)
    {
        if (warnings.Count == 0)
        {
            return;
        }

        MessageBox.Show(
            this,
            string.Join(Environment.NewLine, warnings),
            "参数警告",
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }

    private static bool[] ParseCoils(string input)
    {
        string[] tokens = input.Split(
            DataSeparators,
            StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            throw new ArgumentException("请输入线圈值，例如：1,0,1。");
        }

        return tokens.Select(token => token switch
        {
            "1" or "true" or "TRUE" or "ON" or "on" => true,
            "0" or "false" or "FALSE" or "OFF" or "off" => false,
            _ => throw new ArgumentException($"无法识别线圈值“{token}”。")
        }).ToArray();
    }

    private static ushort[] ParseRegisters(string input)
    {
        string[] tokens = input.Split(
            DataSeparators,
            StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
        {
            throw new ArgumentException("请输入寄存器值，例如：0x0001,100。");
        }

        return tokens.Select(token =>
        {
            bool hexadecimal = token.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
            string valueText = hexadecimal ? token[2..] : token;
            NumberStyles style = hexadecimal ? NumberStyles.HexNumber : NumberStyles.Integer;
            if (!ushort.TryParse(
                    valueText,
                    style,
                    CultureInfo.InvariantCulture,
                    out ushort value))
            {
                throw new ArgumentException($"无法识别 16 位寄存器值“{token}”。");
            }

            return value;
        }).ToArray();
    }

    private static decimal ClampDecimal(decimal value, decimal minimum, decimal maximum)
    {
        return Math.Clamp(value, minimum, maximum);
    }

    private void OpenByteOrderProbe()
    {
        using ByteOrderProbeForm form = new(
            _services.ByteOrderProbeService,
            UiHelpers.GetSelectedValue(_addressBaseCombo, AddressBase.ZeroBased));
        if (form.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _configuration.Parsing.DataType = form.AppliedDataType;
        _configuration.Parsing.ByteOrder = form.AppliedByteOrder;
        UiHelpers.SelectOption(_manualDataTypeCombo, form.AppliedDataType);
        UiHelpers.SelectOption(_manualByteOrderCombo, form.AppliedByteOrder);
        _statusConnectionLabel.Text =
            $"已应用字节序探测结果：{form.AppliedByteOrder}，{form.AppliedDataType}";
        _statusConnectionLabel.ForeColor = Color.DarkGreen;
    }
}
