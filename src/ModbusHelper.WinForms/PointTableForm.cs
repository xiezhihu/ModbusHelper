using System.ComponentModel;
using System.Globalization;

using ModbusHelper.Core.Configuration;
using ModbusHelper.Core.Enums;
using ModbusHelper.Core.Protocol;

namespace ModbusHelper.WinForms;

/// <summary>
/// 仅 UI 使用的点位表窗口。点位可保存为纯 POCO 配置，并一键生成轮询任务。
/// </summary>
internal sealed class PointTableForm : Form
{
    private static readonly int[] Palette =
    {
        unchecked((int)0xFF0078D7),
        unchecked((int)0xFF2E8B57),
        unchecked((int)0xFFD2691E),
        unchecked((int)0xFF8A2BE2),
        unchecked((int)0xFFB22222),
        unchecked((int)0xFF008B8B)
    };

    private readonly AddressBase _addressBase;
    private readonly BindingList<PointConfiguration> _points = new();
    private readonly DataGridView _grid = new();

    public PointTableForm(IEnumerable<PointConfiguration> points, AddressBase addressBase)
    {
        _addressBase = addressBase;
        foreach (PointConfiguration point in points)
        {
            _points.Add(Clone(point));
        }

        Text = "点位表";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1080, 560);
        MinimumSize = new Size(900, 460);
        Font = new Font("Microsoft YaHei UI", 9F);
        BackColor = Color.FromArgb(244, 247, 248);
        BuildLayout();
    }

    public IReadOnlyList<PointConfiguration> Points => _points.ToArray();

    public PointConfiguration? SelectedPoint { get; private set; }

    private void BuildLayout()
    {
        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
            Padding = new Padding(10)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));

        FlowLayoutPanel toolbar = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight
        };
        toolbar.Controls.Add(UiHelpers.CreateButton("新增点位", AddPoint, 92));
        toolbar.Controls.Add(UiHelpers.CreateButton("删除点位", DeletePoint, 92));
        root.Controls.Add(toolbar, 0, 0);

        ConfigureGrid();
        root.Controls.Add(_grid, 0, 1);

        FlowLayoutPanel footer = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft
        };
        Button cancel = UiHelpers.CreateButton("取消", () => { }, 88);
        cancel.DialogResult = DialogResult.Cancel;
        footer.Controls.Add(cancel);
        footer.Controls.Add(UiHelpers.CreateButton("保存点位表", SavePoints, 116));
        footer.Controls.Add(UiHelpers.CreateButton("生成轮询任务", GenerateTask, 132));
        root.Controls.Add(footer, 0, 2);
        Controls.Add(root);
        CancelButton = cancel;
    }

    private void ConfigureGrid()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.AutoGenerateColumns = false;
        _grid.AllowUserToAddRows = false;
        _grid.RowHeadersVisible = false;
        _grid.BackgroundColor = Color.White;
        _grid.DataSource = _points;
        _grid.CellFormatting += OnGridCellFormatting;
        _grid.CellParsing += OnGridCellParsing;
        _grid.CellDoubleClick += (_, args) =>
        {
            if (args.RowIndex >= 0)
            {
                GenerateTask();
            }
        };

        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(PointConfiguration.Name),
            HeaderText = "点位名称",
            MinimumWidth = 160,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(PointConfiguration.UnitId),
            HeaderText = "从站",
            Width = 60,
            ValueType = typeof(byte)
        });
        _grid.Columns.Add(new DataGridViewComboBoxColumn
        {
            DataPropertyName = nameof(PointConfiguration.Function),
            HeaderText = "功能码",
            Width = 150,
            DataSource = Enum.GetValues<ModbusFunction>()
                .Select(function => new UiOption<ModbusFunction>(
                    function,
                    UiHelpers.ShortFunctionText(function)))
                .ToArray(),
            DisplayMember = nameof(UiOption<ModbusFunction>.Text),
            ValueMember = nameof(UiOption<ModbusFunction>.Value),
            ValueType = typeof(ModbusFunction),
            DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(PointConfiguration.StartAddress),
            HeaderText = "起始地址",
            Width = 90,
            ValueType = typeof(ushort)
        });
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(PointConfiguration.Count),
            HeaderText = "数量",
            Width = 62,
            ValueType = typeof(ushort)
        });
        _grid.Columns.Add(new DataGridViewComboBoxColumn
        {
            DataPropertyName = nameof(PointConfiguration.DataType),
            HeaderText = "数据类型",
            Width = 105,
            DataSource = Enum.GetValues<DataValueType>()
                .Select(type => new UiOption<DataValueType>(type, UiHelpers.DataTypeText(type)))
                .ToArray(),
            DisplayMember = nameof(UiOption<DataValueType>.Text),
            ValueMember = nameof(UiOption<DataValueType>.Value),
            ValueType = typeof(DataValueType),
            DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton
        });
        _grid.Columns.Add(new DataGridViewComboBoxColumn
        {
            DataPropertyName = nameof(PointConfiguration.ByteOrder),
            HeaderText = "数据布局",
            Width = 80,
            DataSource = Enum.GetValues<RegisterByteOrder>()
                .Select(order => new UiOption<RegisterByteOrder>(
                    order,
                    UiHelpers.ShortByteOrderText(order)))
                .ToArray(),
            DisplayMember = nameof(UiOption<RegisterByteOrder>.Text),
            ValueMember = nameof(UiOption<RegisterByteOrder>.Value),
            ValueType = typeof(RegisterByteOrder),
            DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton
        });
        _grid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            DataPropertyName = nameof(PointConfiguration.ShowInTrend),
            HeaderText = "趋势",
            Width = 56,
            ValueType = typeof(bool)
        });
    }

    private void AddPoint()
    {
        PointConfiguration point = new()
        {
            Name = $"点位 {_points.Count + 1}",
            UnitId = 1,
            Function = ModbusFunction.ReadHoldingRegisters,
            StartAddress = 0,
            Count = 1,
            DataType = DataValueType.UInt16,
            ByteOrder = RegisterByteOrder.ABCD,
            ShowInTrend = false,
            TrendColorArgb = Palette[_points.Count % Palette.Length]
        };
        _points.Add(point);
        _grid.CurrentCell = _grid.Rows[_grid.Rows.Count - 1].Cells[0];
        _grid.BeginEdit(true);
    }

    private void DeletePoint()
    {
        if (_grid.CurrentRow?.DataBoundItem is PointConfiguration point)
        {
            _points.Remove(point);
        }
    }

    private void SavePoints()
    {
        _grid.EndEdit();
        DialogResult = DialogResult.OK;
        Close();
    }

    private void GenerateTask()
    {
        _grid.EndEdit();
        if (_grid.CurrentRow?.DataBoundItem is not PointConfiguration point)
        {
            MessageBox.Show(
                this,
                "请先选择一个点位。",
                "生成轮询任务",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            return;
        }

        SelectedPoint = Clone(point);
        DialogResult = DialogResult.OK;
        Close();
    }

    private void OnGridCellFormatting(
        object? sender,
        DataGridViewCellFormattingEventArgs e)
    {
        if (e.RowIndex < 0
            || _grid.Columns[e.ColumnIndex].DataPropertyName
            != nameof(PointConfiguration.StartAddress)
            || _grid.Rows[e.RowIndex].DataBoundItem is not PointConfiguration point)
        {
            return;
        }

        e.Value = ModbusAddressConverter.ToDisplayAddress(
                point.StartAddress,
                _addressBase,
                point.Function)
            .ToString(CultureInfo.InvariantCulture);
        e.FormattingApplied = true;
    }

    private void OnGridCellParsing(
        object? sender,
        DataGridViewCellParsingEventArgs e)
    {
        if (e.RowIndex < 0
            || _grid.Columns[e.ColumnIndex].DataPropertyName
            != nameof(PointConfiguration.StartAddress)
            || _grid.Rows[e.RowIndex].DataBoundItem is not PointConfiguration point
            || e.Value is not string text
            || !int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int display))
        {
            return;
        }

        e.Value = ModbusAddressConverter.ToProtocolAddress(
            display,
            _addressBase,
            point.Function);
        e.ParsingApplied = true;
    }

    private static PointConfiguration Clone(PointConfiguration point)
    {
        return new PointConfiguration
        {
            Id = string.IsNullOrWhiteSpace(point.Id) ? Guid.NewGuid().ToString("N") : point.Id,
            Name = point.Name,
            UnitId = point.UnitId,
            Function = point.Function,
            StartAddress = point.StartAddress,
            Count = point.Count,
            DataType = point.DataType,
            ByteOrder = point.ByteOrder,
            TrendColorArgb = point.TrendColorArgb,
            ShowInTrend = point.ShowInTrend
        };
    }
}
