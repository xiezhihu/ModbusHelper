using System.ComponentModel;
using System.Globalization;

using ModbusHelper.Core.Configuration;
using ModbusHelper.Core.Enums;
using ModbusHelper.Core.Protocol;

namespace ModbusHelper.WinForms;

/// <summary>
/// 仅 UI 使用的从站随机数据规则窗口。
/// </summary>
internal sealed class SlaveRandomDataForm : Form
{
    private readonly AddressBase _addressBase;
    private readonly HashSet<byte> _unitIds;
    private readonly BindingList<RandomRuleEditor> _editors = new();
    private readonly DataGridView _grid = new();
    private readonly Button _okButton = UiHelpers.CreateButton("确定", () => { }, 88);

    public SlaveRandomDataForm(
        IEnumerable<SlaveRandomDataRule> rules,
        AddressBase addressBase,
        IEnumerable<byte> unitIds)
    {
        _addressBase = addressBase;
        _unitIds = unitIds.ToHashSet();
        Text = "从站随机数据配置";
        StartPosition = FormStartPosition.CenterParent;
        ClientSize = new Size(1240, 520);
        MinimumSize = new Size(1000, 420);
        Font = new Font("Microsoft YaHei UI", 9F);
        BackColor = Color.FromArgb(244, 247, 248);

        foreach (SlaveRandomDataRule rule in rules)
        {
            _editors.Add(RandomRuleEditor.FromConfiguration(rule, addressBase));
        }

        BuildLayout();
        InitializeToolTips();
    }

    /// <summary>用户确认后的随机规则。</summary>
    public IReadOnlyList<SlaveRandomDataRule> AppliedRules { get; private set; } =
        Array.Empty<SlaveRandomDataRule>();

    private void BuildLayout()
    {
        TableLayoutPanel root = new()
        {
            Dock = DockStyle.Fill,
            RowCount = 4,
            ColumnCount = 1,
            Padding = new Padding(8)
        };
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 56F));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));

        FlowLayoutPanel toolbar = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false
        };
        toolbar.Controls.Add(UiHelpers.CreateButton("新增规则", AddRule, 90));
        toolbar.Controls.Add(UiHelpers.CreateButton("删除规则", DeleteRule, 90));
        root.Controls.Add(toolbar, 0, 0);

        Label description = new()
        {
            Dock = DockStyle.Fill,
            Text = "每条规则可独立指定 UnitID、存储区和地址范围。"
                + "“启动时随机”只在每次启动从站时生成一次运行初值；"
                + "“运行中随机”按周期更新运行内存。停止后初始数据表不会被运行中随机值覆盖。",
            ForeColor = Color.DimGray,
            AutoEllipsis = true,
            TextAlign = ContentAlignment.MiddleLeft
        };
        root.Controls.Add(description, 0, 1);

        ConfigureGrid();
        root.Controls.Add(_grid, 0, 2);

        FlowLayoutPanel footer = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false
        };
        Button cancelButton = UiHelpers.CreateButton("取消", () => DialogResult = DialogResult.Cancel, 88);
        _okButton.Click += (_, _) => ApplyAndClose();
        footer.Controls.Add(cancelButton);
        footer.Controls.Add(_okButton);
        root.Controls.Add(footer, 0, 3);
        Controls.Add(root);
        AcceptButton = _okButton;
        CancelButton = cancelButton;
    }

    private void ConfigureGrid()
    {
        _grid.Dock = DockStyle.Fill;
        _grid.AutoGenerateColumns = false;
        _grid.AllowUserToAddRows = false;
        _grid.RowHeadersVisible = false;
        _grid.BackgroundColor = Color.White;
        _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _grid.DataSource = _editors;
        UiHelpers.ConfigureGridMetrics(_grid);

        AddCheckColumn(nameof(RandomRuleEditor.Enabled), "启用", 52);
        AddTextColumn(nameof(RandomRuleEditor.UnitId), "从站地址", 72);

        DataGridViewComboBoxColumn areaColumn = new()
        {
            Name = nameof(RandomRuleEditor.Area),
            DataPropertyName = nameof(RandomRuleEditor.Area),
            HeaderText = "存储区域",
            Width = 112,
            MinimumWidth = 100,
            DataSource = Enum.GetValues<SlaveArea>()
                .Select(area => new UiOption<SlaveArea>(area, AreaText(area)))
                .ToArray(),
            DisplayMember = nameof(UiOption<SlaveArea>.Text),
            ValueMember = nameof(UiOption<SlaveArea>.Value),
            DisplayStyle = DataGridViewComboBoxDisplayStyle.ComboBox,
            FlatStyle = FlatStyle.Standard
        };
        _grid.Columns.Add(areaColumn);

        AddTextColumn(nameof(RandomRuleEditor.StartAddress), "起始地址", 86);
        AddTextColumn(nameof(RandomRuleEditor.Count), "数量", 64);
        AddTextColumn(nameof(RandomRuleEditor.MinimumValue), "最小值", 70);
        AddTextColumn(nameof(RandomRuleEditor.MaximumValue), "最大值", 70);
        AddTextColumn(nameof(RandomRuleEditor.IntervalMilliseconds), "周期毫秒", 86);
        AddCheckColumn(nameof(RandomRuleEditor.RandomizeOnStart), "启动时随机", 88);
        AddCheckColumn(nameof(RandomRuleEditor.RandomizeWhileRunning), "运行中随机", 88);
    }

    private void AddTextColumn(string propertyName, string header, int width)
    {
        _grid.Columns.Add(new DataGridViewTextBoxColumn
        {
            Name = propertyName,
            DataPropertyName = propertyName,
            HeaderText = header,
            Width = UiHelpers.Scale(_grid, width),
            MinimumWidth = UiHelpers.Scale(_grid, Math.Max(width - 8, 48)),
            ValueType = propertyName == nameof(RandomRuleEditor.UnitId)
                ? typeof(byte)
                : typeof(string)
        });
    }

    private void AddCheckColumn(string propertyName, string header, int width)
    {
        _grid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            Name = propertyName,
            DataPropertyName = propertyName,
            HeaderText = header,
            Width = UiHelpers.Scale(_grid, width),
            MinimumWidth = UiHelpers.Scale(_grid, Math.Max(width - 8, 48))
        });
    }

    private void InitializeToolTips()
    {
        _grid.ShowCellToolTips = true;
        _grid.Columns[nameof(RandomRuleEditor.Enabled)].HeaderCell.ToolTipText =
            "取消勾选后该规则不会生成或更新随机数据。";
        _grid.Columns[nameof(RandomRuleEditor.StartAddress)].HeaderCell.ToolTipText =
            "按当前从站地址基准填写；1 基时保持寄存器首地址为 40001。";
        _grid.Columns[nameof(RandomRuleEditor.Count)].HeaderCell.ToolTipText =
            "从起始地址开始连续生成的数据数量。";
        _grid.Columns[nameof(RandomRuleEditor.MinimumValue)].HeaderCell.ToolTipText =
            "寄存器为 0 至 65535；线圈和离散输入只能使用 0 或 1。";
        _grid.Columns[nameof(RandomRuleEditor.RandomizeOnStart)].HeaderCell.ToolTipText =
            "每次点击启动从站时生成一次运行初值，不修改上方初始数据表。";
        _grid.Columns[nameof(RandomRuleEditor.RandomizeWhileRunning)].HeaderCell.ToolTipText =
            "从站运行期间按周期更新运行内存，主站轮询会读到变化值。";
    }

    private void AddRule()
    {
        _editors.Add(new RandomRuleEditor
        {
            UnitId = _unitIds.Count == 0 ? (byte)1 : _unitIds.Min(),
            Area = SlaveArea.HoldingRegisters,
            StartAddress = ModbusAddressConverter.ToDisplayAddress(
                0,
                _addressBase,
                ModbusFunction.ReadHoldingRegisters),
            Count = 1,
            MinimumValue = 0,
            MaximumValue = 100,
            IntervalMilliseconds = 1000,
            RandomizeOnStart = true,
            RandomizeWhileRunning = true
        });
    }

    private void DeleteRule()
    {
        if (_grid.CurrentRow?.DataBoundItem is RandomRuleEditor editor)
        {
            _editors.Remove(editor);
        }
    }

    private void ApplyAndClose()
    {
        try
        {
            _grid.EndEdit();
            List<SlaveRandomDataRule> rules = _editors
                .Select(editor => editor.ToConfiguration(_addressBase))
                .ToList();
            HashSet<byte> configured = _unitIds;
            foreach (SlaveRandomDataRule rule in rules)
            {
                if (!configured.Contains(rule.UnitId))
                {
                    throw new ArgumentException($"随机规则使用了未配置的从站地址 {rule.UnitId}。");
                }

                if (rule.Count == 0
                    || !ModbusAddressConverter.IsRangeValid(rule.StartAddress, rule.Count))
                {
                    throw new ArgumentException($"从站 {rule.UnitId} 的随机地址范围无效。");
                }

                if (rule.MinimumValue > rule.MaximumValue)
                {
                    throw new ArgumentException($"从站 {rule.UnitId} 的随机最小值不能大于最大值。");
                }

                if (rule.Area is SlaveArea.Coils or SlaveArea.DiscreteInputs
                    && rule.MaximumValue > 1)
                {
                    throw new ArgumentException("线圈和离散输入的随机值只能是 0 或 1。");
                }

                if (rule.RandomizeWhileRunning && rule.IntervalMilliseconds < 100)
                {
                    throw new ArgumentException("运行中随机周期不能小于 100 毫秒。");
                }
            }

            AppliedRules = rules;
            DialogResult = DialogResult.OK;
        }
        catch (Exception exception) when (exception is ArgumentException or FormatException)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "随机数据规则错误",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
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

    private sealed class RandomRuleEditor
    {
        public bool Enabled { get; set; } = true;
        public byte UnitId { get; set; } = 1;
        public SlaveArea Area { get; set; } = SlaveArea.HoldingRegisters;
        public int StartAddress { get; set; }
        public ushort Count { get; set; } = 1;
        public ushort MinimumValue { get; set; }
        public ushort MaximumValue { get; set; } = 100;
        public int IntervalMilliseconds { get; set; } = 1000;
        public bool RandomizeOnStart { get; set; }
        public bool RandomizeWhileRunning { get; set; }

        public static RandomRuleEditor FromConfiguration(
            SlaveRandomDataRule rule,
            AddressBase addressBase)
        {
            return new RandomRuleEditor
            {
                Enabled = rule.Enabled,
                UnitId = rule.UnitId,
                Area = rule.Area,
                StartAddress = ModbusAddressConverter.ToDisplayAddress(
                    rule.StartAddress,
                    addressBase,
                    ToFunction(rule.Area)),
                Count = rule.Count,
                MinimumValue = rule.MinimumValue,
                MaximumValue = rule.MaximumValue,
                IntervalMilliseconds = rule.IntervalMilliseconds,
                RandomizeOnStart = rule.RandomizeOnStart,
                RandomizeWhileRunning = rule.RandomizeWhileRunning
            };
        }

        public SlaveRandomDataRule ToConfiguration(AddressBase addressBase)
        {
            return new SlaveRandomDataRule
            {
                Enabled = Enabled,
                UnitId = UnitId,
                Area = Area,
                StartAddress = ModbusAddressConverter.ToProtocolAddress(
                    StartAddress,
                    addressBase,
                    ToFunction(Area)),
                Count = Count,
                MinimumValue = MinimumValue,
                MaximumValue = MaximumValue,
                IntervalMilliseconds = IntervalMilliseconds,
                RandomizeOnStart = RandomizeOnStart,
                RandomizeWhileRunning = RandomizeWhileRunning
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
    }
}
