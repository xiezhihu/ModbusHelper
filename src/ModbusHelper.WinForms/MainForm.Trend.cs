using System.Windows.Forms.DataVisualization.Charting;

using ModbusHelper.Business.Trends;
using ModbusHelper.Core.Configuration;
using ModbusHelper.Core.Enums;

namespace ModbusHelper.WinForms;

public sealed partial class MainForm
{
    private readonly Chart _trendChart = new();
    private readonly ComboBox _trendWindowCombo = UiHelpers.CreateCombo();
    private readonly CheckBox _trendPauseCheck = new()
    {
        Text = "暂停刷新",
        AutoSize = true,
        Margin = new Padding(12, 8, 4, 4)
    };
    private readonly NumericUpDown _trendCapacityInput =
        UiHelpers.CreateNumber(100, 100_000, 5000);
    private readonly CheckedListBox _trendSeriesList = new();
    private readonly ToolTip _chartToolTip = new();
    private readonly Dictionary<string, long> _trendCursors =
        new(StringComparer.OrdinalIgnoreCase);
    private bool _rebuildingTrendSeries;

    private void InitializeTrendPage()
    {
        _trendPage.BackColor = Color.FromArgb(244, 247, 248);
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
        _trendWindowCombo.Items.Add(new UiOption<int>(60, "最近 1 分钟"));
        _trendWindowCombo.Items.Add(new UiOption<int>(300, "最近 5 分钟"));
        _trendWindowCombo.Items.Add(new UiOption<int>(1800, "最近 30 分钟"));
        _trendWindowCombo.Width = 130;
        _trendWindowCombo.SelectedIndex = 0;
        _trendCapacityInput.Width = 90;
        _trendCapacityInput.ValueChanged += (_, _) => OnTrendCapacityChanged();
        toolbar.Controls.Add(UiHelpers.CreateLabel("时间窗口"));
        toolbar.Controls.Add(_trendWindowCombo);
        toolbar.Controls.Add(_trendPauseCheck);
        toolbar.Controls.Add(UiHelpers.CreateLabel("每曲线点数"));
        toolbar.Controls.Add(_trendCapacityInput);
        toolbar.Controls.Add(UiHelpers.CreateButton("导出图片", ExportTrendImage, 96));
        layout.Controls.Add(toolbar, 0, 0);

        SplitContainer split = new()
        {
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            FixedPanel = FixedPanel.Panel2
        };
        UiHelpers.SetInitialSplitterLayout(split, 1110, 700, 210);
        ConfigureTrendChart();
        split.Panel1.Controls.Add(_trendChart);

        GroupBox seriesGroup = UiHelpers.CreateGroup("曲线显示");
        _trendSeriesList.Dock = DockStyle.Fill;
        _trendSeriesList.CheckOnClick = true;
        _trendSeriesList.ItemCheck += OnTrendSeriesItemCheck;
        seriesGroup.Controls.Add(_trendSeriesList);
        split.Panel2.Controls.Add(seriesGroup);
        layout.Controls.Add(split, 0, 1);
        _trendPage.Controls.Add(layout);
    }

    private void ConfigureTrendChart()
    {
        _trendChart.Dock = DockStyle.Fill;
        _trendChart.BackColor = Color.White;
        _trendChart.BorderlineColor = Color.FromArgb(196, 208, 210);
        _trendChart.BorderlineDashStyle = ChartDashStyle.Solid;
        _trendChart.BorderlineWidth = 1;
        _trendChart.Palette = ChartColorPalette.None;
        _trendChart.MouseMove += OnTrendChartMouseMove;

        ChartArea area = new("主图")
        {
            BackColor = Color.White,
            AxisX = CreateAxis("时间", Color.FromArgb(92, 107, 109)),
            AxisY = CreateAxis("数值", Color.FromArgb(92, 107, 109))
        };
        area.AxisX.LabelStyle.Format = "HH:mm:ss";
        area.AxisX.IntervalType = DateTimeIntervalType.Auto;
        area.AxisX.MajorGrid.LineColor = Color.FromArgb(232, 237, 238);
        area.AxisY.MajorGrid.LineColor = Color.FromArgb(232, 237, 238);
        area.CursorX.IsUserEnabled = true;
        area.CursorX.IsUserSelectionEnabled = true;
        _trendChart.ChartAreas.Add(area);

        Legend legend = new("图例")
        {
            Docking = Docking.Top,
            Alignment = StringAlignment.Near,
            Font = new Font("Microsoft YaHei UI", 8.5F)
        };
        _trendChart.Legends.Add(legend);
    }

    private static Axis CreateAxis(string title, Color color)
    {
        return new Axis
        {
            Title = title,
            TitleFont = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold),
            LabelStyle = { ForeColor = color },
            LineColor = color,
            MajorTickMark = { LineColor = color },
            IsStartedFromZero = false
        };
    }

    private void ApplyTrendConfiguration()
    {
        _trendCapacityInput.Value = Math.Clamp(
            _configuration.TrendCapacity,
            (int)_trendCapacityInput.Minimum,
            (int)_trendCapacityInput.Maximum);
    }

    private void OnTrendCapacityChanged()
    {
        if (_rebuildingTrendSeries)
        {
            return;
        }

        _configuration.TrendCapacity = (int)_trendCapacityInput.Value;
        _services.TrendDataStore.Clear();
        _trendCursors.Clear();
        RebuildTrendSeries();
    }

    private void RebuildTrendSeries()
    {
        _rebuildingTrendSeries = true;
        try
        {
            _trendChart.Series.Clear();
            _trendSeriesList.Items.Clear();
            _trendCursors.Clear();
            foreach (PollingTaskConfiguration task in _configuration.PollingTasks
                         .Where(task => task.ShowInTrend))
            {
                Series series = new(task.Id)
                {
                    Name = task.Id,
                    LegendText = task.Name,
                    ChartType = SeriesChartType.Line,
                    BorderWidth = 2,
                    Color = Color.FromArgb(task.TrendColorArgb),
                    XValueType = ChartValueType.DateTime,
                    YValueType = ChartValueType.Double,
                    IsVisibleInLegend = true,
                    ToolTip = "#LEGENDTEXT\\n#VALX{HH:mm:ss.fff}\\n#VALY{G9}"
                };
                series["PointWidth"] = "1";
                _trendChart.Series.Add(series);
                _trendSeriesList.Items.Add(
                    new TrendListItem(task.Id, task.Name, task.TrendVisible),
                    task.TrendVisible);
                _trendCursors.TryAdd(task.Id, 0);
            }
        }
        finally
        {
            _rebuildingTrendSeries = false;
        }
    }

    private void RefreshTrendChart()
    {
        if (_trendPauseCheck.Checked
            || _mainTabs.SelectedTab != _trendPage
            || _trendChart.ChartAreas.Count == 0)
        {
            return;
        }

        int windowSeconds = _trendWindowCombo.SelectedItem is UiOption<int> option
            ? option.Value
            : 60;
        DateTimeOffset now = DateTimeOffset.Now;
        DateTime cutoff = now.LocalDateTime.AddSeconds(-windowSeconds);
        ChartArea area = _trendChart.ChartAreas[0];
        area.AxisX.Maximum = now.LocalDateTime.ToOADate();
        area.AxisX.Minimum = cutoff.ToOADate();

        foreach (Series chartSeries in _trendChart.Series)
        {
            TrendSeries? trendSeries = _services.TrendDataStore.Get(chartSeries.Name);
            if (trendSeries is null)
            {
                continue;
            }

            long cursor = _trendCursors.GetValueOrDefault(chartSeries.Name);
            cursor = trendSeries.ReadAfter(cursor, out TrendSample[] samples);
            _trendCursors[chartSeries.Name] = cursor;
            chartSeries.Enabled = trendSeries.Visible;
            if (!trendSeries.Visible)
            {
                continue;
            }

            foreach (TrendSample sample in samples)
            {
                if (!double.IsFinite(sample.Value)
                    || sample.Timestamp.LocalDateTime < cutoff)
                {
                    continue;
                }

                int pointIndex = chartSeries.Points.AddXY(
                    sample.Timestamp.LocalDateTime.ToOADate(),
                    sample.Value);
                chartSeries.Points[pointIndex].ToolTip =
                    $"{trendSeries.Name}\\n{sample.Timestamp.LocalDateTime:HH:mm:ss.fff}\\n{sample.DisplayValue}";
            }

            while (chartSeries.Points.Count > 0
                   && chartSeries.Points[0].XValue < area.AxisX.Minimum)
            {
                chartSeries.Points.RemoveAt(0);
            }
        }
    }

    private void OnTrendSeriesItemCheck(object? sender, ItemCheckEventArgs e)
    {
        if (_rebuildingTrendSeries
            || _trendSeriesList.Items[e.Index] is not TrendListItem item)
        {
            return;
        }

        bool visible = e.NewValue == CheckState.Checked;
        PollingTaskConfiguration? task = _configuration.PollingTasks.FirstOrDefault(
            task => string.Equals(task.Id, item.Id, StringComparison.OrdinalIgnoreCase));
        if (task is not null)
        {
            task.TrendVisible = visible;
        }

        TrendSeries? trendSeries = _services.TrendDataStore.Get(item.Id);
        if (trendSeries is not null)
        {
            trendSeries.Visible = visible;
        }
    }

    private void OnTrendChartMouseMove(object? sender, MouseEventArgs e)
    {
        HitTestResult? hit = _trendChart
            .HitTest(e.X, e.Y, false, ChartElementType.DataPoint)
            .FirstOrDefault();
        if (hit?.Object is DataPoint point && hit.Series is not null)
        {
            _chartToolTip.SetToolTip(
                _trendChart,
                $"{hit.Series.LegendText}\\n{DateTime.FromOADate(point.XValue):HH:mm:ss.fff}\\n{point.YValues[0]:G9}");
        }
    }

    private void ExportTrendImage()
    {
        using SaveFileDialog dialog = new()
        {
            Filter = "PNG 图片|*.png",
            DefaultExt = "png",
            FileName = $"modbus-trend-{DateTime.Now:yyyyMMdd-HHmmss}.png"
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _trendChart.SaveImage(dialog.FileName, ChartImageFormat.Png);
        }
    }

    private sealed record TrendListItem(string Id, string Name, bool Visible)
    {
        public override string ToString()
        {
            return Name;
        }
    }
}
