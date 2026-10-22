using System.ComponentModel;

using ModbusHelper.Business.Logging;
using ModbusHelper.Core.Enums;

namespace ModbusHelper.WinForms;

public sealed partial class MainForm
{
    private readonly DataGridView _logGrid = new();
    private readonly BindingList<ModbusLogRecord> _logBindings = new();
    private readonly ComboBox _logLevelCombo = UiHelpers.CreateCombo();
    private readonly TextBox _logSearchInput = UiHelpers.CreateTextBox();
    private readonly Button _logPauseButton = new()
    {
        Text = "暂停采集",
        Width = 96,
        Height = 30,
        Margin = new Padding(4),
        UseVisualStyleBackColor = true
    };
    private long _lastLogRevision = -1;

    private void InitializeLogPage()
    {
        _logPage.BackColor = Color.FromArgb(244, 247, 248);
        TableLayoutPanel layout = new()
        {
            Dock = DockStyle.Fill,
            RowCount = 2,
            ColumnCount = 1,
            Padding = new Padding(8)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 44F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

        FlowLayoutPanel toolbar = new()
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            Padding = new Padding(0, 3, 0, 3)
        };
        UiHelpers.SetEnumOptions(
            _logLevelCombo,
            Enum.GetValues<ModbusLogLevel>(),
            level => level switch
            {
                ModbusLogLevel.Debug => "调试及以上",
                ModbusLogLevel.Info => "信息及以上",
                ModbusLogLevel.Warn => "警告及以上",
                ModbusLogLevel.Error => "仅错误",
                _ => level.ToString()
            });
        _logLevelCombo.Width = 120;
        _logLevelCombo.SelectedIndex = 0;
        _logLevelCombo.SelectedIndexChanged += (_, _) => _lastLogRevision = -1;
        _logSearchInput.Width = 260;
        _logSearchInput.PlaceholderText = "搜索报文、含义或错误提示";
        _logSearchInput.TextChanged += (_, _) => _lastLogRevision = -1;
        toolbar.Controls.Add(UiHelpers.CreateLabel("级别"));
        toolbar.Controls.Add(_logLevelCombo);
        toolbar.Controls.Add(_logSearchInput);
        toolbar.Controls.Add(_logPauseButton);
        toolbar.Controls.Add(UiHelpers.CreateButton("导出 CSV", () => _ = ExportLogsAsync(true), 96));
        toolbar.Controls.Add(UiHelpers.CreateButton("导出 TXT", () => _ = ExportLogsAsync(false), 96));
        toolbar.Controls.Add(UiHelpers.CreateButton("清空日志", ClearLogs, 96));
        layout.Controls.Add(toolbar, 0, 0);

        ConfigureLogGrid();
        layout.Controls.Add(_logGrid, 0, 1);
        _logPage.Controls.Add(layout);
        _logPauseButton.Click += (_, _) => ToggleLogPause();
    }

    private void ConfigureLogGrid()
    {
        _logGrid.Dock = DockStyle.Fill;
        _logGrid.ReadOnly = true;
        _logGrid.AllowUserToAddRows = false;
        _logGrid.AllowUserToDeleteRows = false;
        _logGrid.RowHeadersVisible = false;
        _logGrid.AutoGenerateColumns = false;
        _logGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
        _logGrid.BackgroundColor = Color.White;
        UiHelpers.ConfigureGridMetrics(_logGrid);
        _logGrid.DataSource = _logBindings;
        _logGrid.CellFormatting += OnLogGridCellFormatting;

        _logGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(ModbusLogRecord.Timestamp),
            HeaderText = "时间戳",
            Width = 190,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _logGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(ModbusLogRecord.Level),
            HeaderText = "级别",
            Width = 64,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _logGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(ModbusLogRecord.Role),
            HeaderText = "角色",
            Width = 62,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _logGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(ModbusLogRecord.Direction),
            HeaderText = "方向",
            Width = 82,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _logGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(ModbusLogRecord.Hex),
            HeaderText = "原始十六进制",
            Width = 320,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _logGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(ModbusLogRecord.Meaning),
            HeaderText = "解析含义",
            AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
            MinimumWidth = 220,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
        _logGrid.Columns.Add(new DataGridViewTextBoxColumn
        {
            DataPropertyName = nameof(ModbusLogRecord.ErrorHint),
            HeaderText = "错误提示",
            Width = 280,
            SortMode = DataGridViewColumnSortMode.NotSortable
        });
    }

    private void RefreshLogGrid()
    {
        if (_mainTabs.SelectedTab != _logPage)
        {
            return;
        }

        long revision = _services.LogService.Revision;
        if (revision == _lastLogRevision)
        {
            return;
        }

        _lastLogRevision = revision;
        ModbusLogLevel minimum = UiHelpers.GetSelectedValue(
            _logLevelCombo,
            ModbusLogLevel.Debug);
        int firstDisplayedRow = _logGrid.FirstDisplayedScrollingRowIndex;
        bool followLatest = firstDisplayedRow <= 0;
        IReadOnlyList<ModbusLogRecord> snapshot = _services.LogService.GetSnapshot(
            minimum,
            _logSearchInput.Text,
            limit: 3000,
            newestFirst: true);
        _logBindings.RaiseListChangedEvents = false;
        _logBindings.Clear();
        foreach (ModbusLogRecord record in snapshot)
        {
            _logBindings.Add(record);
        }

        _logBindings.RaiseListChangedEvents = true;
        _logBindings.ResetBindings();
        if (!followLatest
            && _logGrid.Rows.Count > 0
            && firstDisplayedRow < _logGrid.Rows.Count)
        {
            _logGrid.FirstDisplayedScrollingRowIndex = firstDisplayedRow;
        }
    }

    private void OnLogGridCellFormatting(
        object? sender,
        DataGridViewCellFormattingEventArgs e)
    {
        if (e.ColumnIndex < 0 || e.ColumnIndex >= _logGrid.Columns.Count)
        {
            return;
        }

        if (_logGrid.Columns[e.ColumnIndex].DataPropertyName
            == nameof(ModbusLogRecord.Timestamp))
        {
            if (e.Value is DateTimeOffset timestamp)
            {
                e.Value = timestamp.ToString(
                    "【yyyy-MM-dd HH:mm:ss.fff】",
                    System.Globalization.CultureInfo.InvariantCulture);
                e.FormattingApplied = true;
            }

            return;
        }

        if (_logGrid.Columns[e.ColumnIndex].DataPropertyName == nameof(ModbusLogRecord.Role)
            && e.Value is ModbusRole role)
        {
            e.Value = role == ModbusRole.Master ? "主站" : "从站";
            e.FormattingApplied = true;
            return;
        }

        if (_logGrid.Columns[e.ColumnIndex].DataPropertyName
            == nameof(ModbusLogRecord.Direction)
            && e.Value is DataDirection direction)
        {
            e.Value = direction == DataDirection.Tx ? "发送 TX" : "接收 RX";
            e.FormattingApplied = true;
            return;
        }

        if (_logGrid.Columns[e.ColumnIndex].DataPropertyName == nameof(ModbusLogRecord.Level)
            && e.Value is ModbusLogLevel level)
        {
            e.Value = level switch
            {
                ModbusLogLevel.Debug => "调试",
                ModbusLogLevel.Info => "信息",
                ModbusLogLevel.Warn => "警告",
                ModbusLogLevel.Error => "错误",
                _ => level.ToString()
            };
            if (e.CellStyle is not null)
            {
                e.CellStyle.ForeColor = level switch
                {
                    ModbusLogLevel.Error => Color.DarkRed,
                    ModbusLogLevel.Warn => Color.DarkOrange,
                    ModbusLogLevel.Info => Color.DarkGreen,
                    _ => Color.DarkSlateGray
                };
            }

            e.FormattingApplied = true;
        }
    }

    private void ToggleLogPause()
    {
        bool paused = !_services.LogService.IsPaused;
        _services.LogService.SetPaused(paused);
        _logPauseButton.Text = paused ? "继续采集" : "暂停采集";
        _lastLogRevision = -1;
    }

    private void ClearLogs()
    {
        _services.LogService.Clear();
        _lastLogRevision = -1;
    }

    private async Task ExportLogsAsync(bool csv)
    {
        using SaveFileDialog dialog = new()
        {
            Filter = csv ? "CSV 文件|*.csv" : "文本文件|*.txt",
            DefaultExt = csv ? "csv" : "txt",
            FileName = $"modbus-log-{DateTime.Now:yyyyMMdd-HHmmss}.{(csv ? "csv" : "txt")}"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            if (csv)
            {
                await _services.LogService.ExportCsvAsync(dialog.FileName);
            }
            else
            {
                await _services.LogService.ExportTxtAsync(dialog.FileName);
            }

            _statusConnectionLabel.Text = $"日志已导出：{dialog.FileName}";
            _statusConnectionLabel.ForeColor = Color.DarkGreen;
        }
        catch (Exception exception)
        {
            MessageBox.Show(
                this,
                $"日志导出失败：{exception.Message}",
                "导出日志",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
        }
    }
}
