using System.ComponentModel;
using System.Globalization;

using ModbusHelper.Business.Polling;
using ModbusHelper.Core.Configuration;
using ModbusHelper.Core.Enums;
using ModbusHelper.Core.Protocol;

namespace ModbusHelper.WinForms;

public sealed partial class MainForm
{
    private static readonly int[] TrendColors =
    {
        unchecked((int)0xFF0078D7),
        unchecked((int)0xFF2E8B57),
        unchecked((int)0xFFD2691E),
        unchecked((int)0xFF8A2BE2),
        unchecked((int)0xFFB22222),
        unchecked((int)0xFF008B8B)
    };
    private readonly DataGridView _pollingGrid = new();
    private readonly DataGridView _liveValueGrid = new();
    private readonly BindingList<PollingTaskConfiguration> _pollingTaskBindings = new();
    private readonly Dictionary<string, DataGridViewRow> _liveRows =
        new(StringComparer.OrdinalIgnoreCase);

    private void InitializePollingPage()
    {
        _pollingPage.BackColor = Color.FromArgb(244, 247, 248);
        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1,
            Padding = new Padding(8)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 68F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 32F));

        FlowLayoutPanel toolbar = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 3, 0, 3)
        };
        toolbar.Controls.Add(UiHelpers.CreateButton("新增任务", AddPollingTask, 92));
        toolbar.Controls.Add(UiHelpers.CreateButton("删除任务", DeletePollingTask, 92));
        toolbar.Controls.Add(UiHelpers.CreateButton("点位表", OpenPointTable, 82));
        toolbar.Controls.Add(UiHelpers.CreateButton("启动选中", StartSelectedPollingTask, 92));
        toolbar.Controls.Add(UiHelpers.CreateButton("启动全部", StartAllPollingTasks, 92));
        toolbar.Controls.Add(UiHelpers.CreateButton("停止选中", StopSelectedPollingTask, 92));
        toolbar.Controls.Add(UiHelpers.CreateButton("停止全部", StopAllPollingTasks, 92));
        toolbar.Controls.Add(UiHelpers.CreateButton("曲线颜色", SelectTrendColor, 92));
        layout.Controls.Add(toolbar, 0, 0);

        ConfigurePollingGrid();
        layout.Controls.Add(_pollingGrid, 0, 1);

        GroupBox liveGroup = UiHelpers.CreateGroup("最近轮询值");
        ConfigureLiveValueGrid();
        liveGroup.Controls.Add(_liveValueGrid);
        layout.Controls.Add(liveGroup, 0, 2);
        _pollingPage.Controls.Add(layout);
    }

    private void ConfigurePollingGrid()
    {
        _pollingGrid.Dock = DockStyle.Fill;
        _pollingGrid.AllowUserToAddRows = false;
        _pollingGrid.AllowUserToDeleteRows = false;
        _pollingGrid.AllowUserToResizeRows = false;
        _pollingGrid.AutoGenerateColumns = false;
        _pollingGrid.RowHeadersVisible = false;
        _pollingGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _pollingGrid.MultiSelect = false;
        _pollingGrid.BackgroundColor = Color.White;
        _pollingGrid.EditMode = DataGridViewEditMode.EditOnEnter;
        _pollingGrid.DataSource = _pollingTaskBindings;
        _pollingGrid.CellFormatting += OnPollingGridCellFormatting;
        _pollingGrid.CellParsing += OnPollingGridCellParsing;
        _pollingGrid.CellValueChanged += OnPollingGridCellValueChanged;
        _pollingGrid.DataError += (_, args) =>
        {
            args.ThrowException = false;
            _statusPollingLabel.Text = $"轮询任务输入无效：{args.Exception?.Message}";
            _statusPollingLabel.ForeColor = Color.DarkRed;
        };

        _pollingGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            DataPropertyName = nameof(PollingTaskConfiguration.Enabled),
            HeaderText = "启用",
            Width = 60,
            ValueType = typeof(bool),
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _pollingGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(PollingTaskConfiguration.Name),
            HeaderText = "任务名称",
            MinimumWidth = 140,
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        DataGridViewComboBoxColumn functionColumn = new()
        {
            DataPropertyName = nameof(PollingTaskConfiguration.Function),
            HeaderText = "功能码",
            Width = 130,
            DataSource = Enum.GetValues<ModbusFunction>()
                .Select(function => new UiOption<ModbusFunction>(
                    function,
                    UiHelpers.ShortFunctionText(function)))
                .ToArray(),
            DisplayMember = nameof(UiOption<ModbusFunction>.Text),
            ValueMember = nameof(UiOption<ModbusFunction>.Value),
            ValueType = typeof(ModbusFunction),
            DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };
        _pollingGrid.Columns.Add(functionColumn);
        _pollingGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(PollingTaskConfiguration.UnitId),
            HeaderText = "从站",
            Width = 58,
            ValueType = typeof(byte),
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _pollingGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(PollingTaskConfiguration.StartAddress),
            HeaderText = "起始地址",
            Width = 92,
            ValueType = typeof(ushort),
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _pollingGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(PollingTaskConfiguration.Count),
            HeaderText = "数量",
            Width = 64,
            ValueType = typeof(ushort),
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _pollingGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(PollingTaskConfiguration.IntervalMilliseconds),
            HeaderText = "间隔毫秒",
            Width = 82,
            ValueType = typeof(int),
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        DataGridViewComboBoxColumn dataTypeColumn = new()
        {
            DataPropertyName = nameof(PollingTaskConfiguration.DataType),
            HeaderText = "数据类型",
            Width = 88,
            DataSource = Enum.GetValues<DataValueType>()
                .Select(type => new UiOption<DataValueType>(type, UiHelpers.DataTypeText(type)))
                .ToArray(),
            DisplayMember = nameof(UiOption<DataValueType>.Text),
            ValueMember = nameof(UiOption<DataValueType>.Value),
            ValueType = typeof(DataValueType),
            DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };
        _pollingGrid.Columns.Add(dataTypeColumn);
        DataGridViewComboBoxColumn byteOrderColumn = new()
        {
            DataPropertyName = nameof(PollingTaskConfiguration.ByteOrder),
            HeaderText = "数据布局",
            Width = 76,
            DataSource = Enum.GetValues<RegisterByteOrder>()
                .Select(order => new UiOption<RegisterByteOrder>(
                    order,
                    UiHelpers.ShortByteOrderText(order)))
                .ToArray(),
            DisplayMember = nameof(UiOption<RegisterByteOrder>.Text),
            ValueMember = nameof(UiOption<RegisterByteOrder>.Value),
            ValueType = typeof(RegisterByteOrder),
            DisplayStyle = DataGridViewComboBoxDisplayStyle.DropDownButton,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };
        _pollingGrid.Columns.Add(byteOrderColumn);
        _pollingGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            DataPropertyName = nameof(PollingTaskConfiguration.ShowInTrend),
            HeaderText = "趋势",
            Width = 54,
            ValueType = typeof(bool),
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _pollingGrid.Columns.Add(new DataGridViewCheckBoxColumn
        {
            DataPropertyName = nameof(PollingTaskConfiguration.TrendVisible),
            HeaderText = "显示",
            Width = 54,
            ValueType = typeof(bool),
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        DataGridViewCheckBoxColumn runningColumn = new()
        {
            DataPropertyName = nameof(PollingTaskConfiguration.IsRunning),
            HeaderText = "运行",
            Width = 54,
            ValueType = typeof(bool),
            ReadOnly = true,
            SortMode = DataGridViewColumnSortMode.NotSortable
        };
        _pollingGrid.Columns.Add(runningColumn);
    }

    private void ConfigureLiveValueGrid()
    {
        _liveValueGrid.Dock = DockStyle.Fill;
        _liveValueGrid.ReadOnly = true;
        _liveValueGrid.AllowUserToAddRows = false;
        _liveValueGrid.AllowUserToDeleteRows = false;
        _liveValueGrid.RowHeadersVisible = false;
        _liveValueGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
        _liveValueGrid.BackgroundColor = Color.White;
        _liveValueGrid.Columns.Add("任务", "任务");
        _liveValueGrid.Columns.Add("时间", "采样时间");
        _liveValueGrid.Columns.Add("数值", "解析值");
        _liveValueGrid.Columns.Add("耗时", "耗时毫秒");
        _liveValueGrid.Columns["任务"]!.FillWeight = 28F;
        _liveValueGrid.Columns["任务"]!.MinimumWidth = 180;
        _liveValueGrid.Columns["时间"]!.FillWeight = 22F;
        _liveValueGrid.Columns["时间"]!.MinimumWidth = 170;
        _liveValueGrid.Columns["数值"]!.FillWeight = 38F;
        _liveValueGrid.Columns["数值"]!.MinimumWidth = 220;
        _liveValueGrid.Columns["耗时"]!.FillWeight = 12F;
        _liveValueGrid.Columns["耗时"]!.MinimumWidth = 90;
    }

    private void ApplyPollingConfiguration()
    {
        _pollingTaskBindings.RaiseListChangedEvents = false;
        _pollingTaskBindings.Clear();
        foreach (PollingTaskConfiguration task in _configuration.PollingTasks)
        {
            if (string.IsNullOrWhiteSpace(task.Id))
            {
                task.Id = Guid.NewGuid().ToString("N");
            }

            _pollingTaskBindings.Add(task);
        }

        _pollingTaskBindings.RaiseListChangedEvents = true;
        _pollingTaskBindings.ResetBindings();
    }

    private void CapturePollingConfiguration()
    {
        _pollingGrid.EndEdit();
        _configuration.PollingTasks = _pollingTaskBindings.ToList();
    }

    private void AddPollingTask()
    {
        PollingTaskConfiguration task = new()
        {
            Name = $"轮询任务 {_pollingTaskBindings.Count + 1}",
            Function = ModbusFunction.ReadHoldingRegisters,
            UnitId = 1,
            StartAddress = 0,
            Count = 1,
            IntervalMilliseconds = 1000,
            DataType = DataValueType.UInt16,
            ByteOrder = RegisterByteOrder.ABCD,
            ShowInTrend = false,
            TrendColorArgb = TrendColors[_pollingTaskBindings.Count % TrendColors.Length]
        };
        _pollingTaskBindings.Add(task);
        CapturePollingConfiguration();
        _pollingGrid.CurrentCell = _pollingGrid.Rows[_pollingGrid.Rows.Count - 1].Cells[1];
        _pollingGrid.BeginEdit(true);
        RebuildTrendSeries();
    }

    private void DeletePollingTask()
    {
        if (_pollingGrid.CurrentRow?.DataBoundItem is not PollingTaskConfiguration task)
        {
            return;
        }

        _ = _services.PollingScheduler.StopTaskAsync(task.Id);
        _pollingTaskBindings.Remove(task);
        CapturePollingConfiguration();
        _services.TrendDataStore.Remove(task.Id);
        RebuildTrendSeries();
    }

    private void StartSelectedPollingTask()
    {
        if (_pollingGrid.CurrentRow?.DataBoundItem is not PollingTaskConfiguration task)
        {
            return;
        }

        StartPollingTask(task);
    }

    private void StartAllPollingTasks()
    {
        _pollingGrid.EndEdit();
        CapturePollingConfiguration();
        foreach (PollingTaskConfiguration task in _pollingTaskBindings.Where(task => task.Enabled))
        {
            StartPollingTask(task);
        }
    }

    private void StartPollingTask(PollingTaskConfiguration task)
    {
        if (!task.Enabled)
        {
            return;
        }

        try
        {
            _services.PollingScheduler.StartTask(task);
            task.IsRunning = true;
            ResetPollingTaskItem(task);
            RebuildTrendSeries();
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "轮询任务参数错误",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }
    }

    private async void StopAllPollingTasks()
    {
        await _services.PollingScheduler.StopAllAsync();
        foreach (PollingTaskConfiguration task in _pollingTaskBindings)
        {
            task.IsRunning = false;
        }

        _pollingTaskBindings.ResetBindings();
        _statusPollingLabel.Text = "已停止";
        _statusPollingLabel.ForeColor = Color.DarkSlateGray;
    }

    private async void StopSelectedPollingTask()
    {
        if (_pollingGrid.CurrentRow?.DataBoundItem is not PollingTaskConfiguration task)
        {
            return;
        }

        await _services.PollingScheduler.StopTaskAsync(task.Id);
        task.IsRunning = false;
        ResetPollingTaskItem(task);
    }

    private void SelectTrendColor()
    {
        if (_pollingGrid.CurrentRow?.DataBoundItem is not PollingTaskConfiguration task)
        {
            return;
        }

        using ColorDialog dialog = new()
        {
            FullOpen = true,
            Color = Color.FromArgb(task.TrendColorArgb)
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        task.TrendColorArgb = dialog.Color.ToArgb();
        ResetPollingTaskItem(task);
        RebuildTrendSeries();
    }

    private void OpenPointTable()
    {
        using PointTableForm dialog = new(
            _configuration.Points,
            UiHelpers.GetSelectedValue(_addressBaseCombo, AddressBase.ZeroBased));
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        _configuration.Points = dialog.Points.ToList();
        if (dialog.SelectedPoint is null)
        {
            return;
        }

        PointConfiguration point = dialog.SelectedPoint;
        PollingTaskConfiguration task = new()
        {
            Id = Guid.NewGuid().ToString("N"),
            Name = point.Name,
            Enabled = true,
            Function = point.Function,
            UnitId = point.UnitId,
            StartAddress = point.StartAddress,
            Count = point.Count,
            IntervalMilliseconds = 1000,
            DataType = point.DataType,
            ByteOrder = point.ByteOrder,
            PointId = point.Id,
            ShowInTrend = point.ShowInTrend,
            TrendColorArgb = point.TrendColorArgb
        };
        _pollingTaskBindings.Add(task);
        CapturePollingConfiguration();
        RebuildTrendSeries();
    }

    private void UpdatePollingTaskState(PollingTaskStateChangedEventArgs args)
    {
        PollingTaskConfiguration? task = _pollingTaskBindings.FirstOrDefault(
            item => string.Equals(item.Id, args.TaskId, StringComparison.OrdinalIgnoreCase));
        if (task is not null)
        {
            task.IsRunning = args.State == PollingTaskState.Running;
            ResetPollingTaskItem(task);
        }

        _statusPollingLabel.Text = args.Message;
        _statusPollingLabel.ForeColor = args.State == PollingTaskState.Faulted
            ? Color.DarkRed
            : args.State == PollingTaskState.Running
                ? Color.DarkGreen
                : Color.DarkSlateGray;
    }

    private void OnPollingSample(PollingSampleEventArgs args)
    {
        if (!_liveRows.TryGetValue(args.Task.Id, out DataGridViewRow? row))
        {
            int index = _liveValueGrid.Rows.Add();
            row = _liveValueGrid.Rows[index];
            row.Tag = args.Task.Id;
            _liveRows[args.Task.Id] = row;
        }

        row.Cells[1].Value = args.Timestamp.ToString(
            "yyyy-MM-dd HH:mm:ss.fff",
            CultureInfo.InvariantCulture);
        row.Cells[2].Value = args.ParsedValue;
        row.Cells[3].Value = args.ElapsedMilliseconds;
        row.Cells[0].Value = args.Task.Name;
    }

    private void UpdatePollingAddressDisplays()
    {
        if (_pollingGrid.Columns.Count > 4)
        {
            _pollingGrid.InvalidateColumn(4);
        }
    }

    private void OnPollingGridCellFormatting(
        object? sender,
        DataGridViewCellFormattingEventArgs e)
    {
        if (e.ColumnIndex != 4
            || e.RowIndex < 0
            || _pollingGrid.Rows[e.RowIndex].DataBoundItem is not PollingTaskConfiguration task)
        {
            return;
        }

        e.Value = ModbusAddressConverter.ToDisplayAddress(
                task.StartAddress,
                UiHelpers.GetSelectedValue(_addressBaseCombo, AddressBase.ZeroBased),
                task.Function)
            .ToString(CultureInfo.InvariantCulture);
        e.FormattingApplied = true;
    }

    private void OnPollingGridCellParsing(
        object? sender,
        DataGridViewCellParsingEventArgs e)
    {
        if (e.ColumnIndex != 4
            || e.RowIndex < 0
            || _pollingGrid.Rows[e.RowIndex].DataBoundItem is not PollingTaskConfiguration task
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
                task.Function);
            e.ParsingApplied = true;
        }
        catch (ArgumentOutOfRangeException exception)
        {
            MessageBox.Show(
                this,
                exception.Message,
                "地址错误",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            e.ParsingApplied = true;
        }
    }

    private async void OnPollingGridCellValueChanged(
        object? sender,
        DataGridViewCellEventArgs e)
    {
        if (e.ColumnIndex != 0
            || e.RowIndex < 0
            || _pollingGrid.Rows[e.RowIndex].DataBoundItem is not PollingTaskConfiguration task
            || task.Enabled
            || !task.IsRunning)
        {
            return;
        }

        await _services.PollingScheduler.StopTaskAsync(task.Id);
        task.IsRunning = false;
        ResetPollingTaskItem(task);
    }

    private void ResetPollingTaskItem(PollingTaskConfiguration task)
    {
        int index = _pollingTaskBindings.IndexOf(task);
        if (index >= 0)
        {
            _pollingTaskBindings.ResetItem(index);
        }
    }
}
