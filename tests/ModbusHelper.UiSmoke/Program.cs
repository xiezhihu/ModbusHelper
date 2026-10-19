using System.ComponentModel;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Reflection;

using ModbusHelper.Core.Configuration;
using ModbusHelper.Core.Enums;

namespace ModbusHelper.UiSmoke;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        int rtuArgumentIndex = Array.FindIndex(
            args,
            argument => string.Equals(argument, "--rtu", StringComparison.OrdinalIgnoreCase));
        bool useRtu = rtuArgumentIndex >= 0;
        string masterPort = useRtu && args.Length > rtuArgumentIndex + 2
            ? args[rtuArgumentIndex + 1]
            : "COM1";
        string slavePort = useRtu && args.Length > rtuArgumentIndex + 2
            ? args[rtuArgumentIndex + 2]
            : "COM2";
        string testConfigurationPath = Path.Combine(
            Path.GetTempPath(),
            $"modbus-helper-ui-smoke-{Guid.NewGuid():N}.db");
        Environment.SetEnvironmentVariable("MODBUSHELPER_DATABASE_PATH", testConfigurationPath);
        ApplicationConfiguration.Initialize();
        Assembly assembly = Assembly.Load("ModbusHelper.WinForms");
        Type compositionRoot = assembly.GetType(
            "ModbusHelper.WinForms.AppCompositionRoot",
            throwOnError: true)!;
        MethodInfo factory = compositionRoot.GetMethod(
            "CreateMainForm",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
        using Form form = (Form)factory.Invoke(null, null)!;
        form.StartPosition = FormStartPosition.Manual;
        form.Location = new Point(20, 20);
        form.Show();
        Pump(TimeSpan.FromMilliseconds(350));

        TabControl tabs = FindControl<TabControl>(form)
            ?? throw new InvalidOperationException("未找到主选项卡控件。");
        string outputDirectory = Path.Combine(AppContext.BaseDirectory, "ui-smoke");
        Directory.CreateDirectory(outputDirectory);

        for (int index = 0; index < tabs.TabPages.Count; index++)
        {
            tabs.SelectedIndex = index;
            Pump(TimeSpan.FromMilliseconds(250));
            using Bitmap bitmap = new(form.Width, form.Height);
            form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
            string path = Path.Combine(
                outputDirectory,
                $"{index + 1}-{tabs.TabPages[index].Text}.png");
            bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            Console.WriteLine($"已渲染：{path}");
        }

        CheckAllTabs(form, tabs, outputDirectory, "布局报告.txt");
        RunSelfLoopInteraction(
            form,
            tabs,
            outputDirectory,
            useRtu,
            masterPort,
            slavePort);
        form.ClientSize = new Size(1180, 760);
        Pump(TimeSpan.FromMilliseconds(350));
        for (int index = 0; index < tabs.TabPages.Count; index++)
        {
            tabs.SelectedIndex = index;
            Pump(TimeSpan.FromMilliseconds(180));
            SavePage(form, outputDirectory, $"10-小窗口-{index + 1}.png");
        }

        CheckAllTabs(form, tabs, outputDirectory, "小窗口布局报告.txt");
        form.Close();
        Pump(TimeSpan.FromSeconds(2));
        foreach (string suffix in new[] { string.Empty, "-shm", "-wal" })
        {
            string path = testConfigurationPath + suffix;
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        Console.WriteLine(useRtu
            ? $"界面 RTU 自环检查通过（{masterPort}/{slavePort}）。"
            : "界面 TCP 自环检查通过。");
    }

    private static void RunSelfLoopInteraction(
        Form form,
        TabControl tabs,
        string outputDirectory,
        bool useRtu,
        string masterPortName,
        string slavePortName)
    {
        Button slaveStart = GetField<Button>(form, "_slaveStartButton");
        Label slaveState = GetField<Label>(form, "_slaveStateLabel");
        int port = 0;
        tabs.SelectedIndex = 2;
        if (useRtu)
        {
            SelectComboByText(GetField<ComboBox>(form, "_slaveTypeCombo"), "RTU");
            SelectComboPort(
                GetField<ComboBox>(form, "_slaveSerialPortCombo"),
                slavePortName);
        }
        else
        {
            port = GetFreeTcpPort();
            GetField<NumericUpDown>(form, "_slaveListenPortInput").Value = port;
        }

        slaveStart.PerformClick();
        Pump(TimeSpan.FromMilliseconds(800));
        if (!slaveState.Text.Contains("监听", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"从站未进入监听状态：{slaveState.Text}");
        }

        Button connect = GetField<Button>(form, "_connectButton");
        Label headerStatus = GetField<Label>(form, "_headerStatusLabel");
        tabs.SelectedIndex = 0;
        if (useRtu)
        {
            SelectComboByText(GetField<ComboBox>(form, "_connectionTypeCombo"), "RTU");
            SelectComboPort(GetField<ComboBox>(form, "_serialPortCombo"), masterPortName);
        }
        else
        {
            GetField<NumericUpDown>(form, "_tcpPortInput").Value = port;
        }

        connect.PerformClick();
        Pump(TimeSpan.FromMilliseconds(900));
        if (headerStatus.Text != "已连接")
        {
            throw new InvalidOperationException($"主站未连接：{headerStatus.Text}");
        }

        BindingList<PollingTaskConfiguration> tasks = GetField<
            BindingList<PollingTaskConfiguration>>(form, "_pollingTaskBindings");
        if (tasks.Count > 0)
        {
            tasks[0].IntervalMilliseconds = 100;
            tasks[0].ShowInTrend = true;
        }

        for (int index = 1; index < (useRtu ? 1 : 5); index++)
        {
            tasks.Add(new PollingTaskConfiguration
            {
                Id = $"ui-smoke-{index}",
                Name = $"冒烟曲线 {index + 1}",
                Function = ModbusFunction.ReadHoldingRegisters,
                UnitId = 1,
                StartAddress = (ushort)index,
                Count = 1,
                IntervalMilliseconds = 100,
                DataType = DataValueType.UInt16,
                ShowInTrend = true,
                Enabled = true,
                TrendColorArgb = index switch
                {
                    1 => unchecked((int)0xFF2E8B57),
                    2 => unchecked((int)0xFFD2691E),
                    3 => unchecked((int)0xFF8A2BE2),
                    _ => unchecked((int)0xFFB22222)
                }
            });
        }

        tabs.SelectedIndex = 1;
        DataGridView pollingGrid = GetField<DataGridView>(form, "_pollingGrid");
        if (!pollingGrid.MultiSelect || pollingGrid.SelectionMode != DataGridViewSelectionMode.FullRowSelect)
        {
            throw new InvalidOperationException("轮询任务表未启用整行多选。");
        }

        AssertReadOnlyFunctionOptions(pollingGrid, 2);

        pollingGrid.ClearSelection();
        if (pollingGrid.Rows.Count >= 2)
        {
            pollingGrid.Rows[0].Selected = true;
            pollingGrid.Rows[1].Selected = true;
            if (pollingGrid.SelectedRows.Count < 2)
            {
                throw new InvalidOperationException("轮询任务表无法同时选中多个任务。");
            }
        }

        if (pollingGrid.Rows.Count > 0)
        {
            pollingGrid.CurrentCell = pollingGrid.Rows[0].Cells[7];
            pollingGrid.BeginEdit(true);
            Pump(TimeSpan.FromMilliseconds(80));
            if (pollingGrid.EditingControl is ComboBox dataTypeCombo
                && dataTypeCombo.DropDownWidth < 180)
            {
                throw new InvalidOperationException("数据类型下拉框宽度不足，长文本可能被裁切。");
            }

            pollingGrid.EndEdit();
        }

        Button startAll = FindButton(tabs, "启动全部")
            ?? throw new InvalidOperationException("未找到启动全部按钮。");
        startAll.PerformClick();
        Pump(TimeSpan.FromMilliseconds(useRtu ? 1200 : 700));

        tabs.SelectedIndex = 0;
        Button execute = GetField<Button>(form, "_executeManualButton");
        TextBox request = GetField<TextBox>(form, "_requestHexBox");
        TextBox response = GetField<TextBox>(form, "_responseHexBox");
        execute.PerformClick();
        Pump(TimeSpan.FromMilliseconds(800));
        if (string.IsNullOrWhiteSpace(request.Text) || string.IsNullOrWhiteSpace(response.Text))
        {
            throw new InvalidOperationException("界面手动读取未收到完整请求和响应报文。");
        }

        if (!request.Text.Contains(" : ", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("请求历史未按“时间戳：十六进制报文”格式显示。");
        }

        if (!response.Text.Contains(" : ", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("响应历史未按“时间戳：十六进制报文”格式显示。");
        }

        tabs.SelectedIndex = 3;
        Pump(TimeSpan.FromMilliseconds(450));
        SavePage(form, outputDirectory, "6-自环趋势.png");
        tabs.SelectedIndex = 4;
        Pump(TimeSpan.FromMilliseconds(350));
        SavePage(form, outputDirectory, "7-自环日志.png");
        RenderByteOrderProbe(outputDirectory);
        RenderPointTable(outputDirectory);
    }

    private static void RenderByteOrderProbe(string outputDirectory)
    {
        Assembly assembly = Assembly.Load("ModbusHelper.WinForms");
        Type formType = assembly.GetType(
            "ModbusHelper.WinForms.ByteOrderProbeForm",
            throwOnError: true)!;
        Type serviceType = Assembly.Load("ModbusHelper.Business").GetType(
            "ModbusHelper.Business.ByteOrder.ByteOrderProbeService",
            throwOnError: true)!;
        object service = Activator.CreateInstance(
            serviceType,
            new object?[] { null })!;
        using Form form = (Form)Activator.CreateInstance(
            formType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: new[] { service, (object)AddressBase.ZeroBased },
            culture: null)!;
        form.Show();
        Pump(TimeSpan.FromMilliseconds(250));
        SavePage(form, outputDirectory, "8-字节序探测.png");
        CheckLayoutWindow(form);
        form.Close();
    }

    private static void RenderPointTable(string outputDirectory)
    {
        Assembly assembly = Assembly.Load("ModbusHelper.WinForms");
        Type formType = assembly.GetType(
            "ModbusHelper.WinForms.PointTableForm",
            throwOnError: true)!;
        using Form form = (Form)Activator.CreateInstance(
            formType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: new object[]
            {
                Enumerable.Range(1, 3)
                    .Select(index => new PointConfiguration
                    {
                        Id = $"smoke-point-{index}",
                        Name = $"冒烟点位 {index}",
                        Function = ModbusFunction.ReadHoldingRegisters,
                        StartAddress = (ushort)(index - 1)
                    })
                    .ToList(),
                AddressBase.ZeroBased
            },
            culture: null)!;
        form.Show();
        Pump(TimeSpan.FromMilliseconds(250));
        DataGridView grid = GetField<DataGridView>(form, "_grid");
        if (!grid.MultiSelect || grid.SelectionMode != DataGridViewSelectionMode.FullRowSelect)
        {
            throw new InvalidOperationException("点位表未启用整行多选。");
        }

        DataGridViewColumn trendColumn = grid.Columns
            .Cast<DataGridViewColumn>()
            .Single(column => column.HeaderText == "趋势");
        Rectangle trendBounds = grid.GetColumnDisplayRectangle(trendColumn.Index, cutOverflow: false);
        if (trendBounds.Width <= 0
            || trendBounds.Left < 0
            || trendBounds.Right > grid.ClientRectangle.Right)
        {
            throw new InvalidOperationException(
                $"点位表“趋势”列未完整显示：列区域 {trendBounds}，表格区域 {grid.ClientRectangle}。");
        }

        AssertReadOnlyFunctionOptions(grid, 2);
        SavePage(form, outputDirectory, "9-点位表.png");
        CheckLayoutWindow(form);

        grid.ClearSelection();
        grid.Rows[0].Selected = true;
        grid.Rows[2].Selected = true;
        (FindButton(form, "删除点位")
            ?? throw new InvalidOperationException("未找到删除点位按钮。")).PerformClick();
        IReadOnlyList<PointConfiguration> remaining = (IReadOnlyList<PointConfiguration>)(
            form.GetType().GetProperty(
                "Points",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)!
            .GetValue(form)!);
        if (remaining.Count != 1 || remaining[0].Id != "smoke-point-2")
        {
            throw new InvalidOperationException("点位表多选删除没有只保留未选中的点位。");
        }

        form.Close();
    }

    private static void AssertReadOnlyFunctionOptions(DataGridView grid, int columnIndex)
    {
        if (grid.Columns[columnIndex] is not DataGridViewComboBoxColumn column
            || column.DataSource is not System.Collections.IEnumerable options)
        {
            throw new InvalidOperationException("未找到读功能码下拉列表。");
        }

        ModbusFunction[] functions = options
            .Cast<object>()
            .Select(option => (ModbusFunction)option.GetType()
                .GetProperty("Value")!
                .GetValue(option)!)
            .ToArray();
        ModbusFunction[] expected =
        {
            ModbusFunction.ReadCoils,
            ModbusFunction.ReadDiscreteInputs,
            ModbusFunction.ReadHoldingRegisters,
            ModbusFunction.ReadInputRegisters
        };
        if (!functions.SequenceEqual(expected))
        {
            throw new InvalidOperationException("轮询和点位表功能码必须仅包含 01 至 04 读取功能码。");
        }
    }

    private static void SavePage(Form form, string outputDirectory, string fileName)
    {
        using Bitmap bitmap = new(form.Width, form.Height);
        form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, form.Size));
        string path = Path.Combine(outputDirectory, fileName);
        bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
        Console.WriteLine($"已渲染：{path}");
    }

    private static int GetFreeTcpPort()
    {
        TcpListener listener = new(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static T GetField<T>(object target, string fieldName)
        where T : class
    {
        FieldInfo field = target.GetType().GetField(
            fieldName,
            BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"未找到字段 {fieldName}。");
        return (T)(field.GetValue(target)
            ?? throw new InvalidOperationException($"字段 {fieldName} 为空。"));
    }

    private static void CheckAllTabs(
        Form form,
        TabControl tabs,
        string outputDirectory,
        string reportFileName)
    {
        List<string> lines = new()
        {
            $"窗口客户区：{form.ClientSize.Width}x{form.ClientSize.Height}",
            $"窗口字体：{form.Font.Name} {form.Font.SizeInPoints:F1}"
        };
        for (int index = 0; index < tabs.TabPages.Count; index++)
        {
            tabs.SelectedIndex = index;
            Pump(TimeSpan.FromMilliseconds(120));
            TabPage page = tabs.TabPages[index];
            CheckLayout(page);
            lines.Add($"页签：{page.Text}，尺寸 {page.ClientSize.Width}x{page.ClientSize.Height}");
            AppendControlReport(page, lines, 1);
        }

        File.WriteAllLines(
            Path.Combine(outputDirectory, reportFileName),
            lines,
            System.Text.Encoding.UTF8);
    }

    private static void CheckLayout(Control page)
    {
        if (page is TabPage && (page.Width < 800 || page.Height < 500))
        {
            throw new InvalidOperationException(
                $"页签“{page.Text}”可用尺寸过小：{page.Width}x{page.Height}。");
        }

        CheckLayoutCore(page);
    }

    private static void CheckLayoutWindow(Form form)
    {
        if (form.Width < 800 || form.Height < 500)
        {
            throw new InvalidOperationException(
                $"窗口“{form.Text}”可用尺寸过小：{form.Width}x{form.Height}。");
        }

        CheckLayoutCore(form);
    }

    private static void CheckLayoutCore(Control page)
    {
        List<string> issues = new();
        foreach (Control control in EnumerateControls(page))
        {
            if (IsEffectivelyVisible(control)
                && (control.Width <= 0 || control.Height <= 0))
            {
                issues.Add(
                    $"零尺寸控件：{DescribePath(control)}，位置 {control.Left},{control.Top}，"
                    + $"尺寸 {control.Width}x{control.Height}，可见 {control.Visible}，"
                    + $"停靠 {control.Dock}");
            }

            if (IsEffectivelyVisible(control) && control is Button button)
            {
                Size measured = TextRenderer.MeasureText(
                    button.Text,
                    button.Font,
                    new Size(int.MaxValue, int.MaxValue),
                    TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                if (measured.Width + 16 > button.ClientSize.Width
                    || measured.Height + 4 > button.ClientSize.Height)
                {
                    issues.Add(
                        $"按钮文字被裁切：{DescribePath(button)}，"
                        + $"控件 {button.ClientSize.Width}x{button.ClientSize.Height}，"
                        + $"文字需求 {measured.Width}x{measured.Height}");
                }
            }

            if (IsEffectivelyVisible(control) && control is GroupBox groupBox
                && !string.IsNullOrWhiteSpace(groupBox.Text))
            {
                int minimumWidth = TextRenderer.MeasureText(
                    groupBox.Text,
                    groupBox.Font,
                    new Size(int.MaxValue, int.MaxValue),
                    TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width + 28;
                if (groupBox.ClientSize.Width < minimumWidth)
                {
                    issues.Add(
                        $"分组标题空间不足：{DescribePath(groupBox)}，"
                        + $"控件宽 {groupBox.ClientSize.Width}，至少需要 {minimumWidth}");
                }
            }

            if (IsEffectivelyVisible(control) && control is Label label
                && !label.AutoSize
                && !string.IsNullOrWhiteSpace(label.Text))
            {
                Size measured = TextRenderer.MeasureText(
                    label.Text,
                    label.Font,
                    new Size(int.MaxValue, int.MaxValue),
                    TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                if (measured.Width + 4 > label.ClientSize.Width
                    || measured.Height + 2 > label.ClientSize.Height)
                {
                    issues.Add(
                        $"标签文字被裁切：{DescribePath(label)}，"
                        + $"控件 {label.ClientSize.Width}x{label.ClientSize.Height}，"
                        + $"文字需求 {measured.Width}x{measured.Height}");
                }
            }
        }

        CheckSiblingOverlaps(page, issues);
        if (issues.Count > 0)
        {
            throw new InvalidOperationException(
                $"页签“{page.Text}”存在布局问题：{Environment.NewLine}"
                + string.Join(Environment.NewLine, issues));
        }
    }

    private static bool IsEffectivelyVisible(Control control)
    {
        Control? current = control;
        while (current is not null)
        {
            if (!current.Visible)
            {
                return false;
            }

            current = current.Parent;
        }

        return true;
    }

    private static void CheckSiblingOverlaps(Control parent, List<string> issues)
    {
        List<Control> visibleSiblings = parent.Controls
            .Cast<Control>()
            .Where(IsEffectivelyVisible)
            .ToList();
        for (int firstIndex = 0; firstIndex < visibleSiblings.Count; firstIndex++)
        {
            Control first = visibleSiblings[firstIndex];
            if (!IsLeafVisual(first))
            {
                continue;
            }

            for (int secondIndex = firstIndex + 1; secondIndex < visibleSiblings.Count; secondIndex++)
            {
                Control second = visibleSiblings[secondIndex];
                if (!IsLeafVisual(second))
                {
                    continue;
                }

                Rectangle overlap = Rectangle.Intersect(first.Bounds, second.Bounds);
                if (overlap.Width > 2 && overlap.Height > 2)
                {
                    issues.Add(
                        $"控件重叠：{DescribePath(first)} 与 {DescribePath(second)}，"
                        + $"重叠区域 {overlap.Width}x{overlap.Height}");
                }
            }
        }

        foreach (Control child in parent.Controls)
        {
            if (IsEffectivelyVisible(child))
            {
                CheckSiblingOverlaps(child, issues);
            }
        }
    }

    private static bool IsLeafVisual(Control control)
    {
        return control is TextBoxBase
            or ComboBox
            or NumericUpDown
            or Button
            or CheckBox
            or RadioButton
            or DataGridView
            or ListBox
            or ListView;
    }

    private static void AppendControlReport(
        Control parent,
        List<string> lines,
        int depth)
    {
        foreach (Control control in parent.Controls)
        {
            if (!IsEffectivelyVisible(control))
            {
                continue;
            }

            lines.Add(
                $"{new string(' ', depth * 2)}{Describe(control)}，位置 {control.Left},{control.Top}，尺寸 {control.Width}x{control.Height}");
            AppendControlReport(control, lines, depth + 1);
        }
    }

    private static string Describe(Control control)
    {
        string text = control.Text.Replace(
            "\r",
            " ",
            StringComparison.Ordinal).Replace(
            "\n",
            " ",
            StringComparison.Ordinal);
        if (text.Length > 36)
        {
            text = text[..36] + "…";
        }

        return string.IsNullOrWhiteSpace(text)
            ? control.GetType().Name
            : $"{control.GetType().Name}“{text}”";
    }

    private static string DescribePath(Control control)
    {
        Stack<string> path = new();
        Control? current = control;
        while (current is not null)
        {
            path.Push(Describe(current));
            current = current.Parent;
        }

        return string.Join(" > ", path);
    }

    private static IEnumerable<Control> EnumerateControls(Control parent)
    {
        foreach (Control control in parent.Controls)
        {
            yield return control;
            foreach (Control nested in EnumerateControls(control))
            {
                yield return nested;
            }
        }
    }

    private static T? FindControl<T>(Control parent)
        where T : Control
    {
        foreach (Control control in parent.Controls)
        {
            if (control is T matched)
            {
                return matched;
            }

            T? nested = FindControl<T>(control);
            if (nested is not null)
            {
                return nested;
            }
        }

        return null;
    }

    private static Button? FindButton(Control parent, string text)
    {
        foreach (Control control in EnumerateControls(parent))
        {
            if (control is Button button && button.Text == text)
            {
                return button;
            }
        }

        return null;
    }

    private static void SelectComboByText(ComboBox comboBox, string text)
    {
        for (int index = 0; index < comboBox.Items.Count; index++)
        {
            if (comboBox.Items[index]?.ToString()?.Contains(
                    text,
                    StringComparison.OrdinalIgnoreCase) == true)
            {
                comboBox.SelectedIndex = index;
                return;
            }
        }

        throw new InvalidOperationException($"下拉框中未找到“{text}”。");
    }

    private static void SelectComboPort(ComboBox comboBox, string portName)
    {
        if (!comboBox.Items.Contains(portName))
        {
            comboBox.Items.Add(portName);
        }

        comboBox.SelectedItem = portName;
    }

    private static void Pump(TimeSpan duration)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (stopwatch.Elapsed < duration)
        {
            Application.DoEvents();
            Thread.Sleep(20);
        }
    }
}
