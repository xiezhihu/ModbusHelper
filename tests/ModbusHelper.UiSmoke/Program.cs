using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
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
        GroupBox slaveUnitsGroup = GetField<GroupBox>(form, "_slaveUnitsGroup");
        int port = 0;
        tabs.SelectedIndex = 2;
        if (useRtu)
        {
            SelectComboByText(GetField<ComboBox>(form, "_slaveTypeCombo"), "RTU");
            SelectComboPort(
                GetField<ComboBox>(form, "_slaveSerialPortCombo"),
                slavePortName);
            GetField<NumericUpDown>(form, "_slaveBaudRateInput").Value = 9600;
            GetField<NumericUpDown>(form, "_slaveDataBitsInput").Value = 8;
            SelectComboByText(GetField<ComboBox>(form, "_slaveParityCombo"), "无校验");
            SelectComboByText(GetField<ComboBox>(form, "_slaveStopBitsCombo"), "1");
        }
        else
        {
            port = GetFreeTcpPort();
            SelectComboByText(GetField<ComboBox>(form, "_slaveTypeCombo"), "TCP");
            GetField<NumericUpDown>(form, "_slaveListenPortInput").Value = port;
        }

        slaveStart.PerformClick();
        Pump(TimeSpan.FromMilliseconds(800));
        if (!slaveState.Text.Contains("监听", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"从站未进入监听状态：{slaveState.Text}");
        }

        DataGridView unitGrid = GetField<DataGridView>(form, "_slaveUnitGrid");
        NumericUpDown manualSlaveUnit = GetField<NumericUpDown>(form, "_slaveManualUnitInput");
        if (unitGrid.Rows.Count == 0
            || manualSlaveUnit.Value != (byte)unitGrid.Rows[0].Cells[0].Value!)
        {
            throw new InvalidOperationException("手动模拟默认 UnitID 未与已配置的首个从站单元同步。");
        }

        if (!unitGrid.ReadOnly
            || GetField<Button>(form, "_slaveAddUnitButton").Enabled
            || GetField<Button>(form, "_slaveDeleteUnitButton").Enabled
            || !slaveUnitsGroup.Text.Contains("已锁定", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("从站运行后，初始数据表及新增/删除单元操作必须锁定并显示原因。");
        }

        string[] expectedSlaveHeaders =
        {
            "从站地址",
            "03 保持寄存器",
            "04 输入寄存器",
            "01 线圈",
            "02 离散输入"
        };
        for (int index = 0; index < expectedSlaveHeaders.Length; index++)
        {
            if (!string.Equals(
                    unitGrid.Columns[index].HeaderText,
                    expectedSlaveHeaders[index],
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"从站初始数据表第 {index + 1} 列标题不清晰：{unitGrid.Columns[index].HeaderText}");
            }
        }

        SelectComboByText(GetField<ComboBox>(form, "_addressBaseCombo"), "1 基");
        NumericUpDown manualSlaveAddress = GetField<NumericUpDown>(form, "_slaveManualAddressInput");
        if (manualSlaveAddress.Value != 40001)
        {
            throw new InvalidOperationException(
                $"1 基模式下保持寄存器地址应显示 40001，实际为 {manualSlaveAddress.Value}。");
        }

        ComboBox manualArea = GetField<ComboBox>(form, "_slaveManualAreaCombo");
        SelectComboByText(manualArea, "输入寄存器");
        if (manualSlaveAddress.Value != 30001)
        {
            throw new InvalidOperationException("切换输入寄存器后未转换为 30001 风格地址。");
        }

        SelectComboByText(manualArea, "线圈");
        if (manualSlaveAddress.Value != 1)
        {
            throw new InvalidOperationException("切换线圈后未转换为 1 基线圈地址。");
        }

        SelectComboByText(manualArea, "离散输入");
        if (manualSlaveAddress.Value != 10001)
        {
            throw new InvalidOperationException("切换离散输入后未转换为 10001 风格地址。");
        }

        SelectComboByText(manualArea, "保持寄存器");
        GetField<NumericUpDown>(form, "_manualUnitInput").Value = manualSlaveUnit.Value;
        GetField<TextBox>(form, "_slaveManualValuesInput").Text = "42";
        (FindButton(form, "写入模拟数据")
            ?? throw new InvalidOperationException("未找到写入模拟数据按钮。")).PerformClick();
        string manualSlaveResult = GetField<TextBox>(form, "_slaveManualResultBox").Text;
        if (!manualSlaveResult.Contains("协议地址：0", StringComparison.Ordinal)
            || !manualSlaveResult.Contains("0x002A", StringComparison.Ordinal)
            || !manualSlaveResult.Contains(" : 写入成功", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"从站手动模拟未写入 1 基地址 40001 对应的协议地址 0：{manualSlaveResult}");
        }

        CheckSlaveRuntimeChanges(form, slaveStart, slaveState, unitGrid);

        Button connect = GetField<Button>(form, "_connectButton");
        Label headerStatus = GetField<Label>(form, "_headerStatusLabel");
        tabs.SelectedIndex = 0;
        if (useRtu)
        {
            SelectComboByText(GetField<ComboBox>(form, "_connectionTypeCombo"), "RTU");
            SelectComboPort(GetField<ComboBox>(form, "_serialPortCombo"), masterPortName);
            GetField<NumericUpDown>(form, "_baudRateInput").Value = 9600;
            GetField<NumericUpDown>(form, "_dataBitsInput").Value = 8;
            SelectComboByText(GetField<ComboBox>(form, "_parityCombo"), "无校验");
            SelectComboByText(GetField<ComboBox>(form, "_stopBitsCombo"), "1");
        }
        else
        {
            SelectComboByText(GetField<ComboBox>(form, "_connectionTypeCombo"), "TCP");
            GetField<NumericUpDown>(form, "_tcpPortInput").Value = port;
        }

        connect.PerformClick();
        Pump(TimeSpan.FromMilliseconds(900));
        if (headerStatus.Text != "已连接")
        {
            throw new InvalidOperationException($"主站未连接：{headerStatus.Text}");
        }

        Button execute = GetField<Button>(form, "_executeManualButton");
        TextBox request = GetField<TextBox>(form, "_requestHexBox");
        TextBox response = GetField<TextBox>(form, "_responseHexBox");
        TextBox parsedValues = GetField<TextBox>(form, "_parsedValueBox");
        execute.PerformClick();
        PumpUntil(
            () => !string.IsNullOrWhiteSpace(parsedValues.Text),
            TimeSpan.FromSeconds(3));
        if (string.IsNullOrWhiteSpace(request.Text) || string.IsNullOrWhiteSpace(response.Text))
        {
            throw new InvalidOperationException("界面手动读取未收到完整请求和响应报文。");
        }

        if (!parsedValues.Text.Contains("0x002A", StringComparison.Ordinal))
        {
            tabs.SelectedIndex = 2;
            Pump(TimeSpan.FromMilliseconds(250));
            string failedReadFrames = GetField<TextBox>(form, "_slaveFrameBox").Text;
            throw new InvalidOperationException(
                $"主站没有读取到从站手动修改的值 42：{parsedValues.Text}"
                + $"{Environment.NewLine}请求历史：{request.Text}"
                + $"{Environment.NewLine}响应历史：{response.Text}"
                + $"{Environment.NewLine}从站历史：{failedReadFrames}");
        }

        if (!request.Text.Contains(" : ", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("请求历史未按“时间戳：十六进制报文”格式显示。");
        }

        if (!response.Text.Contains(" : ", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("响应历史未按“时间戳：十六进制报文”格式显示。");
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
        if (string.IsNullOrWhiteSpace(request.Text) || string.IsNullOrWhiteSpace(response.Text))
        {
            throw new InvalidOperationException("轮询任务未刷新主站收发报文历史。");
        }

        tabs.SelectedIndex = 3;
        Pump(TimeSpan.FromMilliseconds(450));
        SavePage(form, outputDirectory, "6-自环趋势.png");
        tabs.SelectedIndex = 4;
        Pump(TimeSpan.FromMilliseconds(350));
        SavePage(form, outputDirectory, "7-自环日志.png");
        tabs.SelectedIndex = 2;
        Pump(TimeSpan.FromMilliseconds(350));
        string slaveFrameHistory = GetField<TextBox>(form, "_slaveFrameBox").Text;
        if (!slaveFrameHistory.Contains(" : 接收请求", StringComparison.Ordinal)
            || !slaveFrameHistory.Contains(" : 发送响应", StringComparison.Ordinal)
            || !slaveFrameHistory.Contains(" | ", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"从站报文历史未显示带时间戳的收发记录：{slaveFrameHistory}");
        }

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
            if (index == 2)
            {
                AssertSlaveGridViewport(GetField<DataGridView>(form, "_slaveUnitGrid"));
                AssertSlaveGridViewport(GetField<DataGridView>(form, "_slaveRuleGrid"));
            }

            lines.Add($"页签：{page.Text}，尺寸 {page.ClientSize.Width}x{page.ClientSize.Height}");
            AppendControlReport(page, lines, 1);
        }

        File.WriteAllLines(
            Path.Combine(outputDirectory, reportFileName),
            lines,
            System.Text.Encoding.UTF8);
    }

    private static void CheckSlaveRuntimeChanges(
        Form form,
        Button slaveStart,
        Label slaveState,
        DataGridView unitGrid)
    {
        Button read = FindButton(form, "读取当前值")
            ?? throw new InvalidOperationException("未找到读取当前值按钮。");
        Button write = FindButton(form, "写入模拟数据")
            ?? throw new InvalidOperationException("未找到写入模拟数据按钮。");
        GroupBox slaveUnitsGroup = GetField<GroupBox>(form, "_slaveUnitsGroup");
        TextBox result = GetField<TextBox>(form, "_slaveManualResultBox");

        if (!unitGrid.ReadOnly)
        {
            throw new InvalidOperationException("从站运行时初始数据表应处于只读状态。");
        }

        read.PerformClick();
        if (!result.Text.Split(new[] { Environment.NewLine + Environment.NewLine },
                StringSplitOptions.None)[0].Contains("42 (0x002A)", StringComparison.Ordinal)
            || !result.Text.Contains(" : 写入成功", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("运行中手动模拟读写未生效，或读写历史被覆盖。");
        }

        GetField<TextBox>(form, "_slaveManualValuesInput").Text = "125";
        write.PerformClick();
        if (!result.Text.StartsWith(
                DateTime.Now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
                StringComparison.Ordinal)
            || !result.Text.Split(new[] { Environment.NewLine + Environment.NewLine },
                StringSplitOptions.None)[0].Contains("125 (0x007D)", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("运行中写入未即时生效或没有毫秒时间戳历史。");
        }

        GetField<Button>(form, "_slaveStopButton").PerformClick();
        PumpUntil(() => slaveStart.Enabled, TimeSpan.FromSeconds(5));
        if (unitGrid.ReadOnly
            || !GetField<Button>(form, "_slaveAddUnitButton").Enabled
            || !GetField<Button>(form, "_slaveDeleteUnitButton").Enabled
            || !slaveUnitsGroup.Text.Contains("已停止时可编辑", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("从站停止后初始数据表及新增/删除单元操作应恢复可用并显示状态。");
        }

        unitGrid.Rows[0].Cells[1].Value = "40001 = 100";
        unitGrid.EndEdit();
        slaveStart.PerformClick();
        PumpUntil(() => slaveState.Text.Contains("监听", StringComparison.Ordinal), TimeSpan.FromSeconds(5));
        if (!unitGrid.ReadOnly)
        {
            throw new InvalidOperationException("从站重新启动后初始数据表没有重新锁定。");
        }

        read.PerformClick();
        if (!result.Text.Split(new[] { Environment.NewLine + Environment.NewLine },
                StringSplitOptions.None)[0].Contains("100 (0x0064)", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("从站重启后没有恢复初始表中的值 100。");
        }

        ComboBox addressBase = GetField<ComboBox>(form, "_addressBaseCombo");
        SelectComboByText(addressBase, "0 基");
        if (!Convert.ToString(unitGrid.Rows[0].Cells[1].Value, CultureInfo.InvariantCulture)!
                .Contains("0 = 100", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("0 基模式下初始值没有按“地址 = 值”格式显示。");
        }

        SelectComboByText(addressBase, "1 基");
        string initialValueText = Convert.ToString(
            unitGrid.Rows[0].Cells[1].Value,
            CultureInfo.InvariantCulture)!;
        if (!initialValueText.Contains("40001 = 100", StringComparison.Ordinal)
            || initialValueText.Contains('→'))
        {
            throw new InvalidOperationException("1 基模式下初始值地址或“地址 = 值”格式错误。");
        }

        for (int index = 0; index < 105; index++)
        {
            read.PerformClick();
        }

        string[] entries = result.Text.Split(
            new[] { Environment.NewLine + Environment.NewLine },
            StringSplitOptions.None);
        if (entries.Length != 100 || entries.Any(entry => !entry.Contains(" : ", StringComparison.Ordinal)))
        {
            throw new InvalidOperationException("手动模拟结果历史未限制为最近 100 条带时间戳的记录。");
        }

        GetField<TextBox>(form, "_slaveManualValuesInput").Text = "42";
        write.PerformClick();
    }

    private static void AssertSlaveGridViewport(DataGridView grid)
    {
        Rectangle visibleBounds = grid.RectangleToScreen(grid.ClientRectangle);
        for (Control? parent = grid.Parent;
             parent is not null && parent is not TabPage;
             parent = parent.Parent)
        {
            visibleBounds.Intersect(parent.RectangleToScreen(parent.ClientRectangle));
        }

        Rectangle headerAndRow = grid.RectangleToScreen(new Rectangle(
            0,
            0,
            1,
            grid.ColumnHeadersHeight + grid.RowTemplate.Height + 2));
        if (visibleBounds.Top > headerAndRow.Top
            || visibleBounds.Bottom < headerAndRow.Bottom)
        {
            throw new InvalidOperationException(
                $"从站表格表头和首行被祖先容器裁切：{DescribePath(grid)}，"
                + $"可见区域纵向范围 {visibleBounds.Top}..{visibleBounds.Bottom}，"
                + $"表头和首行纵向范围 {headerAndRow.Top}..{headerAndRow.Bottom}。");
        }
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

    private static void PumpUntil(Func<bool> condition, TimeSpan timeout)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();
        while (!condition() && stopwatch.Elapsed < timeout)
        {
            Application.DoEvents();
            Thread.Sleep(20);
        }
    }
}
