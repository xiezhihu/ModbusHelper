using System.Diagnostics;
using System.Reflection;
using System.Windows.Forms.DataVisualization.Charting;

using ModbusHelper.Business.Polling;
using ModbusHelper.Business.Trends;
using ModbusHelper.Core.Configuration;

namespace ModbusHelper.UiSmoke;

internal static class TrendCollectionChecks
{
    public static void Run(
        Form form,
        TabControl tabs,
        DataGridView grid,
        PollingTaskConfiguration task)
    {
        object services = GetField<object>(form, "_services");
        PollingScheduler scheduler = (PollingScheduler)services.GetType()
            .GetProperty("PollingScheduler")!.GetValue(services)!;
        TrendDataStore store = (TrendDataStore)services.GetType()
            .GetProperty("TrendDataStore")!.GetValue(services)!;
        Chart chart = GetField<Chart>(form, "_trendChart");
        TrendSeries series = store.Get(task.Id)
            ?? throw new InvalidOperationException("运行中的轮询任务缺少趋势缓冲区。");
        DataGridViewRow row = grid.Rows.Cast<DataGridViewRow>()
            .Single(item => ReferenceEquals(item.DataBoundItem, task));
        DataGridViewColumn column = grid.Columns.Cast<DataGridViewColumn>()
            .Single(item => item.DataPropertyName == nameof(PollingTaskConfiguration.ShowInTrend));
        int sampleCount = 0;
        int stateChanges = 0;
        scheduler.SampleReceived += OnSample;
        scheduler.TaskStateChanged += OnStateChanged;
        try
        {
            WaitUntil(
                () => Volatile.Read(ref sampleCount) >= 2,
                "关闭趋势采集时轮询应继续产生最近值。");
            if (series.LatestSequence != 0)
            {
                throw new InvalidOperationException("启动时关闭趋势采集仍写入了趋势样本。");
            }

            Toggle(true);
            tabs.SelectedIndex = 3;
            WaitUntil(
                () => series.LatestSequence >= 2 && HasChartPoints(),
                "运行中勾选趋势后，没有采集样本并绘制曲线。");

            Toggle(false);
            long pausedSequence = series.LatestSequence;
            int pausedSamples = Volatile.Read(ref sampleCount);
            WaitUntil(
                () => Volatile.Read(ref sampleCount) >= pausedSamples + 3,
                "取消趋势采集后轮询停止了。");
            if (series.LatestSequence != pausedSequence
                || chart.Series.IndexOf(task.Id) >= 0)
            {
                throw new InvalidOperationException("取消趋势后仍在采集，或图表仍保留该曲线。");
            }

            Toggle(true);
            tabs.SelectedIndex = 3;
            WaitUntil(
                () => series.LatestSequence >= pausedSequence + 2 && HasChartPoints(),
                "重新勾选趋势后没有恢复采集和绘图。");
            if (!task.IsRunning || Volatile.Read(ref stateChanges) != 0)
            {
                throw new InvalidOperationException("切换趋势采集不应停止或重启轮询任务。");
            }

            Console.WriteLine("通过：运行中趋势采集开关即时同步，反复切换无需重启任务，图表存在实际采样点。");
        }
        finally
        {
            scheduler.SampleReceived -= OnSample;
            scheduler.TaskStateChanged -= OnStateChanged;
        }

        void Toggle(bool enabled)
        {
            tabs.SelectedIndex = 1;
            grid.CurrentCell = row.Cells[column.Index];
            grid.CurrentCell.Value = enabled;
            grid.EndEdit();
            if (task.ShowInTrend != enabled)
            {
                throw new InvalidOperationException("趋势复选框未提交到任务配置。");
            }
        }

        bool HasChartPoints()
        {
            return chart.Series.IndexOf(task.Id) >= 0
                && chart.Series[task.Id].Enabled
                && chart.Series[task.Id].Points.Count >= 2;
        }

        void OnSample(object? sender, PollingSampleEventArgs args)
        {
            if (string.Equals(args.Task.Id, task.Id, StringComparison.OrdinalIgnoreCase))
            {
                Interlocked.Increment(ref sampleCount);
            }
        }

        void OnStateChanged(object? sender, PollingTaskStateChangedEventArgs args)
        {
            if (string.Equals(args.TaskId, task.Id, StringComparison.OrdinalIgnoreCase))
            {
                Interlocked.Increment(ref stateChanges);
            }
        }
    }

    private static void WaitUntil(Func<bool> condition, string failureMessage)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (!condition() && stopwatch.Elapsed < TimeSpan.FromSeconds(5))
        {
            Application.DoEvents();
            Thread.Sleep(20);
        }

        if (!condition())
        {
            throw new InvalidOperationException(failureMessage);
        }
    }

    private static T GetField<T>(object target, string name)
        where T : class
    {
        return (T)target.GetType()
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(target)!;
    }
}
