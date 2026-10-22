using System.ComponentModel;
using System.Globalization;

using ModbusHelper.Core.Configuration;
using ModbusHelper.Core.Enums;
using ModbusHelper.Core.Protocol;

namespace ModbusHelper.WinForms;

/// <summary>
/// 仅 UI 使用的从站初始数据明细编辑窗口。
/// 主表只显示摘要，本窗口按存储区将每个地址拆成独立行，便于查看大量数据。
/// </summary>
internal sealed class SlaveInitialDataForm : Form
{
    private readonly AddressBase _addressBase;
    private readonly SlaveUnitConfiguration _source;
    private readonly TabControl _areaTabs = new();
    private readonly Dictionary<SlaveArea, BindingList<ValueRow>> _rows = new();
    private readonly Dictionary<SlaveArea, DataGridView> _grids = new();
    private readonly Dictionary<SlaveArea, Label> _countLabels = new();
    private readonly Button _applyButton = UiHelpers.CreateButton("应用", () => { }, 88);
    private readonly Label _validationLabel = new()
    {
        AutoSize = true,
        Visible = false,
        ForeColor = Color.DarkRed,
        Margin = new Padding(4, 0, 4, 4)
    };

    public SlaveInitialDataForm(
        SlaveUnitConfiguration source,
        AddressBase addressBase,
        SlaveArea selectedArea)
    {
        _source = source;
        _addressBase = addressBase;
        _validationLabel.TextChanged += (_, _) =>
            _validationLabel.Visible = !string.IsNullOrWhiteSpace(_validationLabel.Text);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = $"从站 {source.UnitId} 初始数据";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(760, 560);
        MinimumSize = new Size(620, 440);
        Font = new Font("Microsoft YaHei UI", 9F);
        BackColor = Color.FromArgb(244, 247, 248);

        BuildRows();
        BuildLayout();
        _areaTabs.SelectedIndex = AreaIndex(selectedArea);
    }

    /// <summary>应用后的完整四区配置。</summary>
    public SlaveUnitConfiguration? UpdatedConfiguration { get; private set; }

    private void BuildRows()
    {
        _rows[SlaveArea.HoldingRegisters] = new BindingList<ValueRow>(
            _source.HoldingRegisters.Select(value => new ValueRow
            {
                Address = ModbusAddressConverter.ToDisplayAddress(
                    value.Address,
                    _addressBase,
                    ModbusFunction.ReadHoldingRegisters),
                Value = value.Value.ToString(CultureInfo.InvariantCulture)
            }).ToList());
        _rows[SlaveArea.InputRegisters] = new BindingList<ValueRow>(
            _source.InputRegisters.Select(value => new ValueRow
            {
                Address = ModbusAddressConverter.ToDisplayAddress(
                    value.Address,
                    _addressBase,
                    ModbusFunction.ReadInputRegisters),
                Value = value.Value.ToString(CultureInfo.InvariantCulture)
            }).ToList());
        _rows[SlaveArea.Coils] = new BindingList<ValueRow>(
            _source.Coils.Select(value => new ValueRow
            {
                Address = ModbusAddressConverter.ToDisplayAddress(
                    value.Address,
                    _addressBase,
                    ModbusFunction.ReadCoils),
                Value = value.Value ? "ON" : "OFF"
            }).ToList());
        _rows[SlaveArea.DiscreteInputs] = new BindingList<ValueRow>(
            _source.DiscreteInputs.Select(value => new ValueRow
            {
                Address = ModbusAddressConverter.ToDisplayAddress(
                    value.Address,
                    _addressBase,
                    ModbusFunction.ReadDiscreteInputs),
                Value = value.Value ? "ON" : "OFF"
            }).ToList());
    }

    private void BuildLayout()
    {
        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            RowCount = 4,
            ColumnCount = 1,
            Padding = new Padding(8)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        Label description = new()
        {
            AutoSize = true,
            Dock = DockStyle.Fill,
            Text = $"当前从站地址基准：{(_addressBase == AddressBase.ZeroBased ? "0 基" : "1 基（手册地址）")}。"
                + "每行一个地址和值；按 Ctrl/Shift 可多选删除。",
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.DimGray,
            Margin = new Padding(4, 4, 4, 8)
        };
        root.Controls.Add(description, 0, 0);

        ConfigureTabs();
        root.Controls.Add(_areaTabs, 0, 1);
        root.Controls.Add(_validationLabel, 0, 2);
        root.SizeChanged += (_, _) =>
        {
            int availableWidth = Math.Max(1, root.ClientSize.Width - root.Padding.Horizontal - 8);
            description.MaximumSize = new Size(availableWidth, 0);
            _validationLabel.MaximumSize = new Size(availableWidth, 0);
        };

        FlowLayoutPanel footer = new()
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        Button cancelButton = UiHelpers.CreateButton("取消", () => DialogResult = DialogResult.Cancel, 88);
        cancelButton.CausesValidation = false;
        _applyButton.Click += (_, _) => ApplyAndClose();
        footer.Controls.Add(cancelButton);
        footer.Controls.Add(_applyButton);
        root.Controls.Add(footer, 0, 3);
        Controls.Add(root);
        AcceptButton = _applyButton;
        CancelButton = cancelButton;
    }

    private void ConfigureTabs()
    {
        _areaTabs.Dock = DockStyle.Fill;
        AddAreaTab(SlaveArea.HoldingRegisters, "03 保持寄存器");
        AddAreaTab(SlaveArea.InputRegisters, "04 输入寄存器");
        AddAreaTab(SlaveArea.Coils, "01 线圈");
        AddAreaTab(SlaveArea.DiscreteInputs, "02 离散输入");
    }

    private void AddAreaTab(SlaveArea area, string title)
    {
        TabPage page = new(title);
        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Padding = new Padding(4)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        FlowLayoutPanel toolbar = new()
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };
        toolbar.Controls.Add(UiHelpers.CreateButton("新增行", () => AddRow(area), 84));
        toolbar.Controls.Add(UiHelpers.CreateButton("删除行", () => DeleteRow(area), 84));
        toolbar.Controls.Add(UiHelpers.CreateButton("清空区域", () => ClearRows(area), 92));
        Label countLabel = UiHelpers.CreateLabel(string.Empty);
        _countLabels[area] = countLabel;
        toolbar.Controls.Add(countLabel);
        _rows[area].ListChanged += (_, _) => UpdateAreaCount(area);
        UpdateAreaCount(area);
        layout.Controls.Add(toolbar, 0, 0);

        DataGridView grid = new()
        {
            Dock = DockStyle.Fill,
            AutoGenerateColumns = false,
            AllowUserToAddRows = false,
            AllowUserToDeleteRows = false,
            RowHeadersVisible = false,
            BackgroundColor = Color.White,
            SelectionMode = DataGridViewSelectionMode.FullRowSelect,
            MultiSelect = true,
            ShowCellToolTips = true,
            DataSource = _rows[area]
        };
        UiHelpers.ConfigureGridMetrics(grid);
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Address",
            DataPropertyName = nameof(ValueRow.Address),
            HeaderText = "地址",
            Width = UiHelpers.Scale(grid, 150),
            MinimumWidth = UiHelpers.Scale(grid, 110),
            ValueType = typeof(int)
        });
        grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = "Value",
            DataPropertyName = nameof(ValueRow.Value),
            HeaderText = area is SlaveArea.Coils or SlaveArea.DiscreteInputs
                ? "值（ON/OFF 或 0/1）"
                : "值（十进制或 0x 十六进制）",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            MinimumWidth = UiHelpers.Scale(grid, 180)
        });
        grid.Columns[0].HeaderCell.ToolTipText =
            "按当前从站地址基准输入，例如保持寄存器 1 基地址 40001。";
        grid.Columns[1].HeaderCell.ToolTipText =
            area is SlaveArea.Coils or SlaveArea.DiscreteInputs
                ? "可输入 ON/OFF、1/0。"
                : "可输入十进制或 0x 开头的十六进制值。";
        grid.DataError += (_, e) =>
        {
            // 地址列绑定整数，拦截绑定转换错误，避免弹出 WinForms 原始异常对话框。
            e.ThrowException = false;
            e.Cancel = true;
            _validationLabel.Text = "地址必须是十进制整数，请修正当前单元格或按 Esc 撤销输入。";
        };
        grid.CellEndEdit += (_, e) =>
        {
            if (e.RowIndex >= 0)
            {
                grid.Rows[e.RowIndex].ErrorText = string.Empty;
            }

            _validationLabel.Text = string.Empty;
        };
        grid.CellToolTipTextNeeded += (_, e) =>
        {
            if (e.RowIndex >= 0 && e.ColumnIndex >= 0)
            {
                e.ToolTipText = e.ColumnIndex == 0
                    ? "地址按当前从站地址基准显示。"
                    : area is SlaveArea.Coils or SlaveArea.DiscreteInputs
                        ? "线圈和离散输入可输入 ON/OFF、1/0。"
                        : "寄存器可输入十进制或 0x 开头的十六进制值。";
            }
        };
        layout.Controls.Add(grid, 0, 1);
        page.Controls.Add(layout);
        _grids[area] = grid;
        _areaTabs.TabPages.Add(page);
    }

    private void AddRow(SlaveArea area)
    {
        if (!CommitPendingEdits())
        {
            return;
        }

        BindingList<ValueRow> rows = _rows[area];
        int nextProtocolAddress = rows
            .Select(row => TryToProtocolAddress(row.Address, area, out ushort address)
                ? address
                : (ushort?)null)
            .Where(address => address.HasValue)
            .Select(address => (int)address!.Value + 1)
            .DefaultIfEmpty(0)
            .Max();
        if (nextProtocolAddress > ushort.MaxValue)
        {
            MessageBox.Show(this, "地址空间已达到上限。", "新增行", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        rows.Add(new ValueRow
        {
            Address = ModbusAddressConverter.ToDisplayAddress(
                (ushort)nextProtocolAddress,
                _addressBase,
                ToFunction(area)),
            Value = area is SlaveArea.Coils or SlaveArea.DiscreteInputs ? "OFF" : "0"
        });
        DataGridView grid = _grids[area];
        grid.ClearSelection();
        grid.CurrentCell = grid.Rows[^1].Cells[1];
        grid.Rows[^1].Selected = true;
        grid.Focus();
        grid.BeginEdit(true);
    }

    private void DeleteRow(SlaveArea area)
    {
        DataGridView grid = _grids[area];
        // 删除可以丢弃选中行尚未提交的输入，避免无效地址阻止用户移除该行。
        grid.CancelEdit();
        List<ValueRow> selectedRows = grid.SelectedRows
            .Cast<DataGridViewRow>()
            .Select(row => row.DataBoundItem)
            .OfType<ValueRow>()
            .ToList();
        if (selectedRows.Count == 0
            && grid.CurrentRow?.DataBoundItem is ValueRow currentRow)
        {
            selectedRows.Add(currentRow);
        }

        foreach (ValueRow row in selectedRows)
        {
            _rows[area].Remove(row);
        }
    }

    private void ClearRows(SlaveArea area)
    {
        if (_rows[area].Count == 0)
        {
            return;
        }

        if (MessageBox.Show(
                this,
                "确定清空当前存储区域的全部初始值吗？",
                "清空区域",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question) == DialogResult.Yes)
        {
            _grids[area].CancelEdit();
            _rows[area].Clear();
            _validationLabel.Text = string.Empty;
        }
    }

    private void ApplyAndClose()
    {
        try
        {
            if (!CommitPendingEdits())
            {
                return;
            }

            UpdatedConfiguration = new SlaveUnitConfiguration
            {
                UnitId = _source.UnitId,
                HoldingRegisters = ParseRegisters(SlaveArea.HoldingRegisters),
                InputRegisters = ParseRegisters(SlaveArea.InputRegisters),
                Coils = ParseCoils(SlaveArea.Coils),
                DiscreteInputs = ParseCoils(SlaveArea.DiscreteInputs),
                ExceptionRules = _source.ExceptionRules
                    .Select(CloneExceptionRule)
                    .ToList()
            };
            DialogResult = DialogResult.OK;
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            _validationLabel.Text = exception.Message;
        }
    }

    private bool CommitPendingEdits()
    {
        _validationLabel.Text = string.Empty;
        foreach ((SlaveArea area, DataGridView grid) in _grids)
        {
            // 显式结束单元格和绑定源编辑，不能仅依赖焦点变化，否则末次输入可能丢失。
            if (!grid.EndEdit())
            {
                _areaTabs.SelectedIndex = AreaIndex(area);
                grid.Focus();
                return false;
            }

            if (grid.BindingContext?[_rows[area]] is CurrencyManager manager)
            {
                manager.EndCurrentEdit();
            }
        }

        return true;
    }

    private void UpdateAreaCount(SlaveArea area)
    {
        int count = _rows[area].Count;
        _countLabels[area].Text = count == 0 ? "暂无预置值" : $"共 {count} 个地址";
    }

    private List<RegisterInitialValue> ParseRegisters(SlaveArea area)
    {
        HashSet<ushort> addresses = new();
        List<RegisterInitialValue> result = new();
        foreach (ValueRow row in _rows[area])
        {
            ushort address = ValidateAddress(row, area, addresses);
            string text = row.Value?.Trim() ?? string.Empty;
            bool hexadecimal = text.StartsWith("0x", StringComparison.OrdinalIgnoreCase);
            if (!ushort.TryParse(
                    hexadecimal ? text[2..] : text,
                    hexadecimal ? NumberStyles.HexNumber : NumberStyles.Integer,
                    CultureInfo.InvariantCulture,
                    out ushort value))
            {
                throw InvalidValue(
                    area,
                    row,
                    1,
                    $"“{AreaText(area)}”地址 {row.Address}：寄存器值必须在 0 至 65535 内，可输入十进制或 0x 十六进制。");
            }

            result.Add(new RegisterInitialValue { Address = address, Value = value });
        }

        return result.OrderBy(value => value.Address).ToList();
    }

    private List<CoilInitialValue> ParseCoils(SlaveArea area)
    {
        HashSet<ushort> addresses = new();
        List<CoilInitialValue> result = new();
        foreach (ValueRow row in _rows[area])
        {
            ushort address = ValidateAddress(row, area, addresses);
            bool value = (row.Value?.Trim() ?? string.Empty).ToUpperInvariant() switch
            {
                "1" or "TRUE" or "ON" => true,
                "0" or "FALSE" or "OFF" => false,
                _ => throw InvalidValue(
                    area,
                    row,
                    1,
                    $"“{AreaText(area)}”地址 {row.Address}：请输入 ON/OFF、1/0 或 TRUE/FALSE。")
            };
            result.Add(new CoilInitialValue { Address = address, Value = value });
        }

        return result.OrderBy(value => value.Address).ToList();
    }

    private ushort ValidateAddress(ValueRow row, SlaveArea area, HashSet<ushort> addresses)
    {
        ushort address;
        try
        {
            address = ParseAddress(row.Address, area);
        }
        catch (ArgumentException exception)
        {
            throw InvalidValue(area, row, 0, exception.Message);
        }

        if (!addresses.Add(address))
        {
            throw InvalidValue(area, row, 0, $"“{AreaText(area)}”存在重复地址 {row.Address}，请修改或删除重复行。");
        }

        return address;
    }

    private ArgumentException InvalidValue(SlaveArea area, ValueRow row, int columnIndex, string message)
    {
        _areaTabs.SelectedIndex = AreaIndex(area);
        DataGridView grid = _grids[area];
        int rowIndex = _rows[area].IndexOf(row);
        grid.CurrentCell = grid.Rows[rowIndex].Cells[columnIndex];
        grid.Rows[rowIndex].ErrorText = message;
        grid.Focus();
        return new ArgumentException(message);
    }

    private ushort ParseAddress(int displayAddress, SlaveArea area)
    {
        try
        {
            return ModbusAddressConverter.ToProtocolAddress(
                displayAddress,
                _addressBase,
                ToFunction(area));
        }
        catch (ArgumentOutOfRangeException exception)
        {
            throw new ArgumentException(
                $"“{AreaText(area)}”地址 {displayAddress} 无效，请按当前从站地址基准输入有效地址。",
                exception);
        }
    }

    private bool TryToProtocolAddress(int displayAddress, SlaveArea area, out ushort address)
    {
        try
        {
            address = ParseAddress(displayAddress, area);
            return true;
        }
        catch (ArgumentException)
        {
            address = 0;
            return false;
        }
    }

    private static int AreaIndex(SlaveArea area)
    {
        return area switch
        {
            SlaveArea.InputRegisters => 1,
            SlaveArea.Coils => 2,
            SlaveArea.DiscreteInputs => 3,
            _ => 0
        };
    }

    private static ModbusFunction ToFunction(SlaveArea area)
    {
        return area switch
        {
            SlaveArea.Coils => ModbusFunction.ReadCoils,
            SlaveArea.DiscreteInputs => ModbusFunction.ReadDiscreteInputs,
            SlaveArea.InputRegisters => ModbusFunction.ReadInputRegisters,
            _ => ModbusFunction.ReadHoldingRegisters
        };
    }

    private static string AreaText(SlaveArea area)
    {
        return area switch
        {
            SlaveArea.Coils => "线圈",
            SlaveArea.DiscreteInputs => "离散输入",
            SlaveArea.InputRegisters => "输入寄存器",
            _ => "保持寄存器"
        };
    }

    private static ExceptionInjectionRule CloneExceptionRule(ExceptionInjectionRule source)
    {
        return new ExceptionInjectionRule
        {
            Enabled = source.Enabled,
            UnitId = source.UnitId,
            Function = source.Function,
            StartAddress = source.StartAddress,
            Count = source.Count,
            ExceptionCode = source.ExceptionCode
        };
    }

    private sealed class ValueRow
    {
        public int Address { get; set; }

        public string Value { get; set; } = string.Empty;
    }
}
