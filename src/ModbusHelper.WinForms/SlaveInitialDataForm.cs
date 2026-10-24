using System.ComponentModel;
using System.Globalization;

using ModbusHelper.Business.Slaves;
using ModbusHelper.Core.Configuration;
using ModbusHelper.Core.Enums;
using ModbusHelper.Core.Models;
using ModbusHelper.Core.Protocol;

namespace ModbusHelper.WinForms;

/// <summary>
/// 仅 UI 使用的从站数据明细窗口：停止时编辑初始值，运行时只读查看当前值。
/// 主表只显示摘要，本窗口按存储区将每个地址拆成独立行，便于查看大量数据。
/// </summary>
internal sealed class SlaveInitialDataForm : Form
{
    private readonly AddressBase _addressBase;
    private readonly SlaveUnitConfiguration _source;
    private readonly SlaveSimulationManager? _runtimeManager;
    private readonly UiUpdateQueue<byte> _runtimeUpdates;
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
        : this(source, addressBase, selectedArea, null)
    {
    }

    public SlaveInitialDataForm(
        SlaveUnitConfiguration source,
        AddressBase addressBase,
        SlaveArea selectedArea,
        SlaveSimulationManager? runtimeManager)
    {
        _source = source;
        _addressBase = addressBase;
        _runtimeManager = runtimeManager;
        _runtimeUpdates = new(this, _ => RefreshRuntimeData());
        _validationLabel.TextChanged += (_, _) =>
            _validationLabel.Visible = !string.IsNullOrWhiteSpace(_validationLabel.Text);
        AutoScaleMode = AutoScaleMode.Dpi;
        Text = runtimeManager is null
            ? $"从站 {source.UnitId} 初始数据"
            : $"从站 {source.UnitId} 当前数据（只读）";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(760, 560);
        MinimumSize = new Size(620, 440);
        Font = new Font("Microsoft YaHei UI", 9F);
        BackColor = Color.FromArgb(244, 247, 248);

        BuildRows();
        BuildLayout();
        _areaTabs.SelectedIndex = AreaIndex(selectedArea);
        if (runtimeManager is not null)
        {
            runtimeManager.DataChanged += OnRuntimeDataChanged;
            runtimeManager.StateChanged += OnRuntimeStateChanged;
            RefreshRuntimeData();
            Shown += (_, _) => RefreshRuntimeData();
        }
    }

    /// <summary>应用后的完整四区配置。</summary>
    public SlaveUnitConfiguration? UpdatedConfiguration { get; private set; }

    private void BuildRows()
    {
        if (_runtimeManager is not null)
        {
            foreach (SlaveArea area in Enum.GetValues<SlaveArea>())
            {
                _rows[area] = new BindingList<ValueRow>();
            }

            return;
        }

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
                + (_runtimeManager is null
                    ? "每行一个地址和值；按 Ctrl/Shift 可多选删除。"
                    : "当前运行数据（只读），数据变化时自动同步。"),
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
        Button cancelButton = UiHelpers.CreateButton(
            _runtimeManager is null ? "取消" : "关闭", () => DialogResult = DialogResult.Cancel, 88);
        cancelButton.CausesValidation = false;
        _applyButton.Click += (_, _) => ApplyAndClose();
        _applyButton.Visible = _runtimeManager is null;
        _applyButton.Enabled = _runtimeManager is null;
        footer.Controls.Add(cancelButton);
        footer.Controls.Add(_applyButton);
        root.Controls.Add(footer, 0, 3);
        Controls.Add(root);
        AcceptButton = _runtimeManager is null ? _applyButton : cancelButton;
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
        if (_runtimeManager is null)
        {
            toolbar.Controls.Add(UiHelpers.CreateButton("新增行", () => AddRow(area), 84));
            toolbar.Controls.Add(UiHelpers.CreateButton("删除行", () => DeleteRow(area), 84));
            toolbar.Controls.Add(UiHelpers.CreateButton("清空区域", () => ClearRows(area), 92));
        }
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
            ReadOnly = _runtimeManager is not null,
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
            _runtimeManager is null
                ? "按当前从站地址基准输入，例如保持寄存器 1 基地址 40001。"
                : "按当前从站地址基准显示的地址。";
        grid.Columns[1].HeaderCell.ToolTipText =
            _runtimeManager is not null
                ? "当前模拟内存值（只读）。"
                : area is SlaveArea.Coils or SlaveArea.DiscreteInputs
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
                    : _runtimeManager is not null
                        ? "当前模拟内存值（只读），运行期间自动刷新。"
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
        if (_runtimeManager is not null)
        {
            return;
        }

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
        _countLabels[area].Text = count == 0
            ? _runtimeManager is null ? "暂无预置值" : "暂无已写入的数据"
            : $"共 {count} 个地址";
    }

    private void OnRuntimeDataChanged(object? sender, SlaveDataChangedEventArgs args)
    {
        if (args.UnitId == _source.UnitId)
        {
            _runtimeUpdates.Enqueue(args.UnitId);
        }
    }

    private void OnRuntimeStateChanged(object? sender, SlaveStatusChangedEventArgs args)
    {
        _runtimeUpdates.Enqueue(_source.UnitId);
    }

    private void RefreshRuntimeData()
    {
        if (_runtimeManager?.GetDataSnapshot(_source.UnitId) is not { } snapshot)
        {
            Text = $"从站 {_source.UnitId} 当前数据（已停止，保留最后一次显示）";
            return;
        }

        Text = $"从站 {_source.UnitId} 当前数据（只读）";
        UpdateRuntimeRows(SlaveArea.HoldingRegisters, snapshot.HoldingRegisters.Select(value =>
            CreateRuntimeRow(SlaveArea.HoldingRegisters, value.Address, value.Value.ToString(CultureInfo.InvariantCulture))));
        UpdateRuntimeRows(SlaveArea.InputRegisters, snapshot.InputRegisters.Select(value =>
            CreateRuntimeRow(SlaveArea.InputRegisters, value.Address, value.Value.ToString(CultureInfo.InvariantCulture))));
        UpdateRuntimeRows(SlaveArea.Coils, snapshot.Coils.Select(value =>
            CreateRuntimeRow(SlaveArea.Coils, value.Address, value.Value ? "ON" : "OFF")));
        UpdateRuntimeRows(SlaveArea.DiscreteInputs, snapshot.DiscreteInputs.Select(value =>
            CreateRuntimeRow(SlaveArea.DiscreteInputs, value.Address, value.Value ? "ON" : "OFF")));
    }

    private ValueRow CreateRuntimeRow(SlaveArea area, ushort address, string value)
    {
        return new ValueRow
        {
            Address = ModbusAddressConverter.ToDisplayAddress(address, _addressBase, ToFunction(area)),
            Value = value
        };
    }

    private void UpdateRuntimeRows(SlaveArea area, IEnumerable<ValueRow> values)
    {
        BindingList<ValueRow> rows = _rows[area];
        DataGridView grid = _grids[area];
        int firstIndex = grid.FirstDisplayedScrollingRowIndex;
        ValueRow? firstRow = firstIndex >= 0 ? rows[firstIndex] : null;
        int horizontalOffset = grid.HorizontalScrollingOffset;
        int index = 0;
        foreach (ValueRow value in values)
        {
            if (index < rows.Count && rows[index].Address == value.Address)
            {
                if (rows[index].Value != value.Value)
                {
                    rows[index].Value = value.Value;
                    rows.ResetItem(index);
                }
            }
            else
            {
                rows.Insert(index, value);
            }

            index++;
        }

        while (rows.Count > index)
        {
            rows.RemoveAt(rows.Count - 1);
        }

        // 不重绑数据源；已有地址只更新对应行，并保留用户查看位置。
        if (firstRow is not null && grid.Visible && rows.IndexOf(firstRow) is int restoredIndex && restoredIndex >= 0)
        {
            grid.FirstDisplayedScrollingRowIndex = restoredIndex;
        }

        if (grid.HorizontalScrollingOffset != horizontalOffset)
        {
            grid.HorizontalScrollingOffset = horizontalOffset;
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            if (_runtimeManager is not null)
            {
                _runtimeManager.DataChanged -= OnRuntimeDataChanged;
                _runtimeManager.StateChanged -= OnRuntimeStateChanged;
            }

            _runtimeUpdates.Dispose();
        }

        base.Dispose(disposing);
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
