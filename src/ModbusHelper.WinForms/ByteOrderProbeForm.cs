using System.Globalization;

using ModbusHelper.Business.ByteOrder;
using ModbusHelper.Core.Enums;
using ModbusHelper.Core.Exceptions;
using ModbusHelper.Core.Protocol;

namespace ModbusHelper.WinForms;

/// <summary>
/// 仅 UI 使用的字节序探测窗口。窗口只调用业务探测服务。
/// </summary>
internal sealed class ByteOrderProbeForm : Form
{
    private readonly ByteOrderProbeService _probeService;
    private readonly AddressBase _addressBase;
    private readonly NumericUpDown _unitInput = UiHelpers.CreateNumber(1, 247, 1);
    private readonly NumericUpDown _addressInput = UiHelpers.CreateNumber(0, 999999, 0);
    private readonly ComboBox _dataTypeCombo = UiHelpers.CreateCombo();
    private readonly TextBox _minimumInput = UiHelpers.CreateTextBox("0");
    private readonly TextBox _maximumInput = UiHelpers.CreateTextBox("100000");
    private readonly ListView _resultList = new();
    private readonly Label _recommendationLabel = UiHelpers.CreateLabel("请输入参数后执行探测。");
    private readonly Button _applyButton = UiHelpers.CreateButton("应用到解析配置", () => { }, 190);
    private ByteOrderProbeReport? _report;

    public ByteOrderProbeForm(ByteOrderProbeService probeService, AddressBase addressBase)
    {
        _probeService = probeService;
        _addressBase = addressBase;
        Text = "字节序探测";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(920, 560);
        MinimumSize = new Size(820, 500);
        Font = new Font("Microsoft YaHei UI", 9F);
        BackColor = Color.FromArgb(244, 247, 248);

        BuildLayout();
        UiHelpers.SetEnumOptions(
            _dataTypeCombo,
            new[]
            {
                DataValueType.Int32,
                DataValueType.UInt32,
                DataValueType.Float,
                DataValueType.Double
            },
            UiHelpers.DataTypeText);
        _dataTypeCombo.SelectedIndex = 1;
        _applyButton.Click += (_, _) => ApplySelected();
    }

    /// <summary>用户选择应用的数据类型。</summary>
    public DataValueType AppliedDataType { get; private set; }

    /// <summary>用户选择应用的布局。</summary>
    public RegisterByteOrder AppliedByteOrder { get; private set; }

    private void BuildLayout()
    {
        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            RowCount = 4,
            ColumnCount = 1,
            Padding = new Padding(10)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 118F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));

        GroupBox parameterGroup = UiHelpers.CreateGroup("读取参数");
        TableLayoutPanel parameters = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 6,
            RowCount = 2
        };
        for (int index = 0; index < 6; index++)
        {
            parameters.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 16.666F));
        }

        AddInlineField(parameters, 0, "从站地址", _unitInput);
        AddInlineField(parameters, 1, "起始地址", _addressInput);
        AddInlineField(parameters, 2, "数据类型", _dataTypeCombo);
        AddInlineField(parameters, 3, "合理范围最小", _minimumInput);
        AddInlineField(parameters, 4, "合理范围最大", _maximumInput);
        parameters.Controls.Add(
            UiHelpers.CreateButton("读取并探测", () => _ = ProbeAsync(), 108),
            5,
            0);
        Label note = new()
        {
            Text = "32 位读取 2 个寄存器，64 位读取 4 个寄存器。ABCD 为设备手册通常标示的标准大端布局。",
            AutoSize = true,
            ForeColor = Color.DimGray
        };
        parameters.Controls.Add(note, 0, 1);
        parameters.SetColumnSpan(note, 6);
        parameterGroup.Controls.Add(parameters);
        root.Controls.Add(parameterGroup, 0, 0);

        Label disclaimer = new()
        {
            Dock = DockStyle.Fill,
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(126, 74, 0),
            Text = "提示：自动探测只是辅助，合理范围可能同时命中多个结果，最终字节序必须以设备手册为准。",
            Font = new Font(Font, FontStyle.Bold)
        };
        root.Controls.Add(disclaimer, 0, 1);

        ConfigureResultList();
        root.Controls.Add(_resultList, 0, 2);

        TableLayoutPanel footer = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2
        };
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        footer.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 206F));
        footer.Controls.Add(_recommendationLabel, 0, 0);
        footer.Controls.Add(_applyButton, 1, 0);
        root.Controls.Add(footer, 0, 3);
        Controls.Add(root);
        AcceptButton = FindButton(root, "读取并探测");
    }

    private void ConfigureResultList()
    {
        _resultList.Dock = DockStyle.Fill;
        _resultList.View = View.Details;
        _resultList.FullRowSelect = true;
        _resultList.GridLines = true;
        _resultList.HideSelection = false;
        _resultList.Columns.Add("布局", 90);
        _resultList.Columns.Add("解析结果", 220);
        _resultList.Columns.Add("是否在合理范围", 120);
        _resultList.Columns.Add("说明", 380);
    }

    private async Task ProbeAsync()
    {
        try
        {
            DataValueType dataType = UiHelpers.GetSelectedValue(
                _dataTypeCombo,
                DataValueType.UInt32);
            ushort protocolAddress = ModbusAddressConverter.ToProtocolAddress(
                (int)_addressInput.Value,
                _addressBase,
                ModbusFunction.ReadHoldingRegisters);
            if (!double.TryParse(
                    _minimumInput.Text,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double minimum)
                || !double.TryParse(
                    _maximumInput.Text,
                    NumberStyles.Float,
                    CultureInfo.InvariantCulture,
                    out double maximum))
            {
                throw new ArgumentException("合理范围必须输入有效数值。");
            }

            _report = await _probeService.ProbeAsync(
                checked((byte)_unitInput.Value),
                protocolAddress,
                dataType,
                minimum,
                maximum);
            FillResults(_report);
        }
        catch (ModbusCommunicationException exception)
        {
            MessageBox.Show(
                this,
                exception.GetDisplayMessage(),
                "探测失败",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "探测参数错误",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private void FillResults(ByteOrderProbeReport report)
    {
        _resultList.Items.Clear();
        foreach (ByteOrderProbeItem item in report.Items)
        {
            ListViewItem row = new(new[]
            {
                item.ByteOrder.ToString(),
                item.DisplayValue,
                item.InRange ? "是" : "否",
                item.ByteOrder switch
                {
                    RegisterByteOrder.ABCD => "寄存器顺序不变，寄存器内高字节在前",
                    RegisterByteOrder.CDAB => "寄存器顺序反转，寄存器内高字节在前",
                    RegisterByteOrder.BADC => "寄存器顺序不变，寄存器内低字节在前",
                    _ => "寄存器顺序反转，寄存器内低字节在前"
                }
            })
            {
                Tag = item
            };
            if (item.InRange)
            {
                row.BackColor = Color.FromArgb(219, 244, 226);
            }

            _resultList.Items.Add(row);
            if (report.RecommendedByteOrder == item.ByteOrder)
            {
                row.Font = new Font(_resultList.Font, FontStyle.Bold);
                row.Selected = true;
            }
        }

        _recommendationLabel.Text =
            $"{report.RecommendationMessage} 原始寄存器：{string.Join(", ", report.Registers.Select(value => $"0x{value:X4}"))}";
        _applyButton.Enabled = true;
    }

    private void ApplySelected()
    {
        if (_report is null
            || _resultList.SelectedItems.Count == 0
            || _resultList.SelectedItems[0].Tag is not ByteOrderProbeItem item)
        {
            MessageBox.Show(
                this,
                "请先执行探测并选择一个布局。",
                "应用布局",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        AppliedDataType = _report.DataType;
        AppliedByteOrder = item.ByteOrder;
        DialogResult = DialogResult.OK;
        Close();
    }

    private static void AddInlineField(
        TableLayoutPanel table,
        int column,
        string label,
        Control control)
    {
        TableLayoutPanel field = new()
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1
        };
        field.RowStyles.Add(new RowStyle(SizeType.Absolute, 28F));
        field.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        field.Controls.Add(UiHelpers.CreateLabel(label, true), 0, 0);
        control.Dock = DockStyle.Fill;
        field.Controls.Add(control, 0, 1);
        table.Controls.Add(field, column, 0);
    }

    private static Button? FindButton(Control parent, string text)
    {
        foreach (Control child in parent.Controls)
        {
            if (child is Button button && button.Text == text)
            {
                return button;
            }

            Button? nested = FindButton(child, text);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }
}
