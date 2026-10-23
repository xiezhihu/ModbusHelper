using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Reflection;

using ModbusHelper.Business.Logging;
using ModbusHelper.Core.Configuration;
using ModbusHelper.Core.Enums;
using ModbusHelper.Core.Models;

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
        Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
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
        CheckAddressBasePersistence(factory);
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
        CheckIndependentSlaveAddressBases(form);
        CheckDuplicateSlaveUnitIds(form, outputDirectory);
        CheckSlaveInitialDataEditor(form, outputDirectory);
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
            || GetField<Button>(form, "_slaveEditInitialDataButton").Enabled
            || !slaveUnitsGroup.Text.Contains("已锁定", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("从站运行后，初始数据表及新增、删除、编辑操作必须锁定并显示原因。");
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
            || !manualSlaveResult.Contains("】 写入成功", StringComparison.Ordinal))
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

        if (!request.Text.Contains("】 ", StringComparison.Ordinal)
            || request.SelectionStart != request.TextLength)
        {
            throw new InvalidOperationException("请求历史未按“【时间】十六进制报文”格式显示，或没有自动跟随底部。");
        }

        if (!response.Text.Contains("】 ", StringComparison.Ordinal)
            || response.SelectionStart != response.TextLength)
        {
            throw new InvalidOperationException("响应历史未按“【时间】十六进制报文”格式显示，或没有自动跟随底部。");
        }

        CheckHistoryScrollPreservation(form, execute, request);

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
        if (!slaveFrameHistory.Contains("】 接收请求", StringComparison.Ordinal)
            || !slaveFrameHistory.Contains("】 发送响应", StringComparison.Ordinal)
            || !slaveFrameHistory.Contains(" | ", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"从站报文历史未显示“【时间】方向 | 报文”格式：{slaveFrameHistory}");
        }

        SavePage(form, outputDirectory, "11-从站独立地址基准.png");
        RenderByteOrderProbe(outputDirectory);
        RenderPointTable(outputDirectory);
    }

    private static void CheckHistoryScrollPreservation(
        Form form,
        Button execute,
        TextBox request)
    {
        object services = GetField<object>(form, "_services");
        ModbusLogService logService = services.GetType().GetProperty("LogService") is { } logProperty
            ? (ModbusLogService)logProperty.GetValue(services)!
            : throw new InvalidOperationException("未找到报文日志服务。");
        for (int index = 0; index < 90; index++)
        {
            logService.AddFrame(new RawFrameRecord(
                DateTimeOffset.Now.AddMilliseconds(index),
                ModbusRole.Master,
                DataDirection.Tx,
                "01 03 00 00 00 01 84 0A",
                "滚动位置回归测试请求",
                null));
        }

        PumpUntil(
            () => request.Lines.Length >= 60,
            TimeSpan.FromSeconds(5));
        if (request.Lines.Length < 60)
        {
            throw new InvalidOperationException("滚动位置回归测试未生成足够的请求历史。");
        }

        Type uiHelpers = form.GetType().Assembly.GetType(
            "ModbusHelper.WinForms.UiHelpers",
            throwOnError: true)!;
        MethodInfo isTextBoxAtBottom = uiHelpers.GetMethod(
            "IsTextBoxAtBottom",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)!;
        request.SelectionStart = request.TextLength;
        request.SelectionLength = 0;
        request.ScrollToCaret();
        Pump(TimeSpan.FromMilliseconds(100));
        if (!(bool)isTextBoxAtBottom.Invoke(null, [request])!
            || !IsLastCharacterVisible(request))
        {
            throw new InvalidOperationException(
                $"滚动到底部后，界面未识别出当前位于底部。"
                + $"首行={GetFirstVisibleLine(request)}，行数={request.Lines.Length}。");
        }

        int beforeBottomLine = GetFirstVisibleLine(request);
        int previousLength = request.TextLength;
        AddTestRequest(logService);
        PumpUntil(
            () => request.TextLength > previousLength,
            TimeSpan.FromSeconds(3));
        Pump(TimeSpan.FromMilliseconds(100));
        if (!IsLastCharacterVisible(request))
        {
            throw new InvalidOperationException("新请求到达后，历史没有自动滚动到底部。");
        }

        Console.WriteLine(
            $"已验证请求历史底部自动跟随：刷新前首行 {beforeBottomLine}，刷新后首行 {GetFirstVisibleLine(request)}。");

        int targetLine = Math.Min(30, request.Lines.Length - 1);
        request.SelectionStart = request.GetFirstCharIndexFromLine(targetLine);
        request.SelectionLength = 0;
        request.ScrollToCaret();
        Pump(TimeSpan.FromMilliseconds(100));
        int beforeFirstLine = GetFirstVisibleLine(request);
        previousLength = request.TextLength;

        AddTestRequest(logService);
        PumpUntil(
            () => request.TextLength > previousLength,
            TimeSpan.FromSeconds(3));
        Pump(TimeSpan.FromMilliseconds(100));
        int afterFirstLine = GetFirstVisibleLine(request);
        if (beforeFirstLine < 10 || afterFirstLine < 10)
        {
            throw new InvalidOperationException(
                $"用户查看旧请求历史时位置被重置到顶部：刷新前首行 {beforeFirstLine}，刷新后首行 {afterFirstLine}。");
        }

        Console.WriteLine(
            $"已验证请求历史非底部刷新保持位置：刷新前首行 {beforeFirstLine}，刷新后首行 {afterFirstLine}。");
    }

    private static void AddTestRequest(ModbusLogService logService)
    {
        logService.AddFrame(new RawFrameRecord(
            DateTimeOffset.Now,
            ModbusRole.Master,
            DataDirection.Tx,
            "01 03 00 00 00 01 84 0A",
            "滚动位置回归测试请求",
            null));
    }

    private static int GetFirstVisibleLine(TextBox textBox)
    {
        int charIndex = textBox.GetCharIndexFromPosition(new Point(1, 1));
        return charIndex < 0 ? 0 : textBox.GetLineFromCharIndex(charIndex);
    }

    private static bool IsLastCharacterVisible(TextBox textBox)
    {
        if (textBox.TextLength == 0)
        {
            return true;
        }

        Point lastCharacterPosition =
            textBox.GetPositionFromCharIndex(textBox.TextLength - 1);
        return lastCharacterPosition.Y >= 0
            && lastCharacterPosition.Y + textBox.Font.Height <= textBox.ClientSize.Height;
    }

    private static void CheckIndependentSlaveAddressBases(Form form)
    {
        ComboBox masterBase = GetField<ComboBox>(form, "_addressBaseCombo");
        ComboBox slaveBase = GetField<ComboBox>(form, "_slaveAddressBaseCombo");
        NumericUpDown masterAddress = GetField<NumericUpDown>(form, "_manualAddressInput");
        NumericUpDown slaveAddress = GetField<NumericUpDown>(form, "_slaveManualAddressInput");
        SelectComboByText(masterBase, "0 基");
        SelectComboByText(slaveBase, "0 基");
        SelectComboByText(GetField<ComboBox>(form, "_manualFunctionCombo"), "03");
        masterAddress.Value = 0;

        DataGridView units = GetField<DataGridView>(form, "_slaveUnitGrid");
        if (units.Rows.Count == 0)
        {
            GetField<Button>(form, "_slaveAddUnitButton").PerformClick();
        }

        string[] zeroBased = { "0 = 0, 7 = 123", "0 = 11", "0 = ON", "0 = OFF" };
        string[] oneBased = { "40001 = 0, 40008 = 123", "30001 = 11", "1 = ON", "10001 = OFF" };
        for (int index = 0; index < zeroBased.Length; index++)
        {
            units.Rows[0].Cells[index + 1].Value = zeroBased[index];
        }

        GetField<System.Collections.IList>(form, "_slaveRuleEditors").Clear();
        (FindButton(form, "新增规则")
            ?? throw new InvalidOperationException("未找到新增规则按钮。")).PerformClick();
        DataGridView rules = GetField<DataGridView>(form, "_slaveRuleGrid");
        rules.Rows[0].Cells[0].Value = false;
        rules.CurrentCell = rules.Rows[0].Cells[3];
        rules.BeginEdit(true);
        ((TextBox)rules.EditingControl).Text = "7";
        rules.EndEdit();
        SelectComboByText(slaveBase, "1 基");
        for (int index = 0; index < oneBased.Length; index++)
        {
            if (!string.Equals(
                    Convert.ToString(units.Rows[0].Cells[index + 1].Value, CultureInfo.InvariantCulture),
                    oneBased[index],
                    StringComparison.Ordinal))
            {
                throw new InvalidOperationException("从站独立基准未同步换算全部四种存储区的初始值地址。");
            }
        }

        if (masterAddress.Value != 0 || slaveAddress.Value != 40001
            || !masterBase.Text.StartsWith("0 基", StringComparison.Ordinal)
            || Convert.ToUInt16(rules.Rows[0].Cells[3].Value, CultureInfo.InvariantCulture) != 7
            || Convert.ToString(rules.Rows[0].Cells[3].FormattedValue, CultureInfo.InvariantCulture) != "40008")
        {
            throw new InvalidOperationException("从站基准切换影响了主站地址，或未保持异常规则的协议地址。");
        }

        SelectComboByText(masterBase, "1 基");
        SelectComboByText(masterBase, "0 基");
        if (slaveAddress.Value != 40001
            || !slaveBase.Text.StartsWith("1 基", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("主站基准切换不应影响从站独立基准或手动地址。");
        }

        rules.CurrentCell = rules.Rows[0].Cells[3];
        rules.BeginEdit(true);
        ((TextBox)rules.EditingControl).Text = "40010";
        SelectComboByText(slaveBase, "0 基");
        if (Convert.ToUInt16(rules.Rows[0].Cells[3].Value, CultureInfo.InvariantCulture) != 9
            || Convert.ToString(rules.Rows[0].Cells[3].FormattedValue, CultureInfo.InvariantCulture) != "9")
        {
            throw new InvalidOperationException("切换从站基准时，尚在编辑的异常规则地址没有按旧基准提交。");
        }

        units.Rows[0].Cells[2].Value = "无效初始值";
        SelectComboByText(slaveBase, "1 基");
        if (!slaveBase.Text.StartsWith("0 基", StringComparison.Ordinal)
            || Convert.ToString(units.Rows[0].Cells[1].Value, CultureInfo.InvariantCulture) != zeroBased[0]
            || GetField<ToolStripStatusLabel>(form, "_statusConnectionLabel").Text?
                .Contains("未切换地址基准", StringComparison.Ordinal) != true)
        {
            throw new InvalidOperationException("初始值无效时应拒绝切换从站基准，且不能部分修改其他存储区。");
        }

        units.Rows[0].Cells[2].Value = zeroBased[1];
        SelectComboByText(slaveBase, "1 基");
        Console.WriteLine("已验证：主从独立地址基准、四区换算、规则编辑和无效初始值回滚。");
    }

    private static void CheckSlaveInitialDataEditor(Form owner, string outputDirectory)
    {
        SlaveUnitConfiguration source = new()
        {
            UnitId = 7,
            HoldingRegisters = Enumerable.Range(0, 1000)
                .Select(index => new RegisterInitialValue { Address = (ushort)index, Value = (ushort)index })
                .ToList(),
            InputRegisters = Enumerable.Range(0, 125)
                .Select(index => new RegisterInitialValue { Address = (ushort)index, Value = 10 })
                .ToList(),
            Coils = new List<CoilInitialValue> { new() { Address = 0, Value = true } },
            DiscreteInputs = new List<CoilInitialValue> { new() { Address = 0, Value = false } },
            ExceptionRules = new List<ExceptionInjectionRule>
            {
                new()
                {
                    Enabled = true,
                    UnitId = 7,
                    Function = ModbusFunction.ReadHoldingRegisters,
                    StartAddress = 20,
                    Count = 1,
                    ExceptionCode = 2
                }
            }
        };
        using (Form dialog = CreateSlaveInitialDataEditor(source, AddressBase.OneBased, SlaveArea.InputRegisters))
        {
            dialog.Show(owner);
            Pump(TimeSpan.FromMilliseconds(150));
            TabControl tabs = GetField<TabControl>(dialog, "_areaTabs");
            if (tabs.SelectedIndex != 1)
            {
                throw new InvalidOperationException("明细窗口未打开指定的输入寄存器页签。");
            }

            SlaveArea[] areas =
            {
                SlaveArea.HoldingRegisters, SlaveArea.InputRegisters, SlaveArea.Coils, SlaveArea.DiscreteInputs
            };
            int[] firstAddresses = { 40001, 30001, 1, 10001 };
            for (int index = 0; index < areas.Length; index++)
            {
                tabs.SelectedIndex = index;
                Pump(TimeSpan.FromMilliseconds(80));
                DataGridView grid = GetInitialDataGrid(dialog, areas[index]);
                if (Convert.ToInt32(grid.Rows[0].Cells[0].Value, CultureInfo.InvariantCulture) != firstAddresses[index]
                    || !grid.MultiSelect)
                {
                    throw new InvalidOperationException("明细窗口未正确换算四区地址，或未启用多选。");
                }

                CheckLayoutCore(dialog);
                AssertSlaveGridViewport(grid);
                AssertInitialDataColumnsVisible(grid);
            }

            tabs.SelectedIndex = 0;
            DataGridView holding = GetInitialDataGrid(dialog, SlaveArea.HoldingRegisters);
            if (holding.Rows.Count != 1000)
            {
                throw new InvalidOperationException("大量初始数据未全部加载到明细表。");
            }

            SavePage(dialog, outputDirectory, "12-从站初始数据明细.png");
            dialog.ClientSize = new Size(620, 440);
            Pump(TimeSpan.FromMilliseconds(120));
            CheckLayoutCore(dialog);
            AssertSlaveGridViewport(holding);
            AssertInitialDataColumnsVisible(holding);
            SavePage(dialog, outputDirectory, "13-初始数据明细小窗口.png");

            holding.ClearSelection();
            holding.Rows[0].Selected = true;
            holding.Rows[2].Selected = true;
            FindButton(tabs.SelectedTab!, "删除行")!.PerformClick();
            if (holding.Rows.Count != 998
                || Convert.ToInt32(holding.Rows[0].Cells[0].Value, CultureInfo.InvariantCulture) != 40002
                || Convert.ToInt32(holding.Rows[1].Cells[0].Value, CultureInfo.InvariantCulture) != 40004
                || source.HoldingRegisters.Count != 1000)
            {
                throw new InvalidOperationException("明细表未按多选删除，或在应用前修改了原配置。");
            }

            FindButton(tabs.SelectedTab!, "新增行")!.PerformClick();
            if (holding.CurrentCell?.RowIndex != holding.Rows.Count - 1
                || holding.EditingControl is not TextBox newValue)
            {
                throw new InvalidOperationException("新增初始值后应滚动到新行并编辑其值。");
            }

            newValue.Text = "0x2A";
            holding.EndEdit();
            tabs.SelectedIndex = 1;
            DataGridView input = GetInitialDataGrid(dialog, SlaveArea.InputRegisters);
            input.CurrentCell = input.Rows[0].Cells[1];
            input.BeginEdit(true);
            ((TextBox)input.EditingControl).Text = "65535";
            GetField<Button>(dialog, "_applyButton").PerformClick();
            SlaveUnitConfiguration updated = GetUpdatedSlaveInitialData(dialog)
                ?? throw new InvalidOperationException("明细窗口应用后没有返回配置。");
            if (updated.UnitId != 7
                || updated.HoldingRegisters.Count != 999
                || updated.HoldingRegisters[^1].Address != 1000
                || updated.HoldingRegisters[^1].Value != 42
                || updated.InputRegisters[0].Value != 65535
                || !updated.Coils[0].Value
                || updated.DiscreteInputs[0].Value
                || updated.ExceptionRules.Single().ExceptionCode != 2
                || source.InputRegisters[0].Value != 10)
            {
                throw new InvalidOperationException("明细编辑未正确提交末次输入、十六进制值、四区数据或异常规则。");
            }

            dialog.Close();
        }

        CheckSlaveInitialDataValidation(owner, source);
        CheckSlaveInitialDataMainEntry(owner, outputDirectory);
        Console.WriteLine("已验证：1000 行初始值、四区明细、小窗口、多选删除、末次输入、无效值拒绝、取消和主表回填。");
    }

    private static void CheckDuplicateSlaveUnitIds(Form form, string outputDirectory)
    {
        DataGridView grid = GetField<DataGridView>(form, "_slaveUnitGrid");
        if (grid.Rows.Count < 2)
        {
            GetField<Button>(form, "_slaveAddUnitButton").PerformClick();
        }

        if (grid.Rows.Count < 2)
        {
            throw new InvalidOperationException("无法创建第二个从站单元进行重复地址校验。");
        }

        byte firstId = Convert.ToByte(grid.Rows[0].Cells[0].Value, CultureInfo.InvariantCulture);
        DataGridViewCell cell = grid.Rows[1].Cells[0];
        byte previousId = Convert.ToByte(cell.Value, CultureInfo.InvariantCulture);
        Label feedback = GetField<Label>(form, "_slaveUnitValidationLabel");
        foreach (string text in new[]
                 {
                     firstId.ToString(CultureInfo.InvariantCulture), "0", "248", "256", "无效地址", string.Empty
                 })
        {
            BeginSlaveUnitAddressEdit(grid, 1, text);
            if (!grid.EndEdit()
                || Convert.ToByte(cell.Value, CultureInfo.InvariantCulture) != previousId
                || !feedback.Visible
                || !feedback.Text.Contains("本次修改未保存", StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(cell.ErrorText))
            {
                throw new InvalidOperationException(
                    $"无效从站地址“{text}”应保留原地址并显示提示，且不能阻止结束编辑。");
            }
        }

        CheckLayoutCore(form);
        SavePage(form, outputDirectory, "15-从站地址输入校验.png");
        GetField<TabControl>(form, "_mainTabs").SelectedIndex = 0;
        Pump(TimeSpan.FromMilliseconds(150));
        GetField<TabControl>(form, "_mainTabs").SelectedIndex = 2;
        grid.CurrentCell = grid.Rows[0].Cells[0];

        BeginSlaveUnitAddressEdit(grid, 1, "3");
        grid.EndEdit();
        if (feedback.Visible || Convert.ToByte(cell.Value, CultureInfo.InvariantCulture) != 3)
        {
            throw new InvalidOperationException("有效从站地址未保存，或修正后仍显示旧错误。");
        }

        BeginSlaveUnitAddressEdit(grid, 1, firstId.ToString(CultureInfo.InvariantCulture));
        form.GetType().GetMethod("SetSlaveInitialDataLocked", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(form, new object[] { true });
        if (!grid.ReadOnly || Convert.ToByte(cell.Value, CultureInfo.InvariantCulture) != 3)
        {
            throw new InvalidOperationException("结束无效输入并锁定表格时应保留原地址。");
        }

        form.GetType().GetMethod("SetSlaveInitialDataLocked", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(form, new object[] { false });
        BeginSlaveUnitAddressEdit(grid, 1, firstId.ToString(CultureInfo.InvariantCulture));
        grid.CancelEdit();
        grid.CurrentCell = grid.Rows[0].Cells[0];
        if (Convert.ToByte(cell.Value, CultureInfo.InvariantCulture) != 3)
        {
            throw new InvalidOperationException("取消地址编辑后应保留原值，且允许切换单元格。");
        }

        BeginSlaveUnitAddressEdit(grid, 1, "4");
        form.GetType().GetMethod("UpdateSlaveControls", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(form, null);
        if (!grid.IsCurrentCellInEditMode || Convert.ToByte(cell.Value, CultureInfo.InvariantCulture) != 3)
        {
            throw new InvalidOperationException("普通状态刷新不应打断正在输入的从站地址。");
        }

        GetField<Button>(form, "_slaveAddUnitButton").PerformClick();
        if (grid.Rows.Count != 3
            || Convert.ToByte(cell.Value, CultureInfo.InvariantCulture) != 4
            || grid.Rows.Cast<DataGridViewRow>().Select(row => row.Cells[0].Value).Distinct().Count() != 3)
        {
            throw new InvalidOperationException("新增单元前应先提交当前地址，再分配未使用地址。");
        }

        BeginSlaveUnitAddressEdit(grid, 2, firstId.ToString(CultureInfo.InvariantCulture));
        GetField<Button>(form, "_slaveDeleteUnitButton").PerformClick();
        if (grid.Rows.Count != 2)
        {
            throw new InvalidOperationException("正在输入无效地址时应仍能删除当前单元。");
        }

        // 模拟旧配置中已有重复地址：允许导航、修改和删除，但启动校验必须拒绝。
        cell.Value = firstId;
        bool valid = (bool)form.GetType()
            .GetMethod("ValidateConfiguredSlaveUnitIds", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(form, new object[] { false })!;
        if (valid || string.IsNullOrWhiteSpace(grid.Rows[1].ErrorText))
        {
            throw new InvalidOperationException("重复从站地址应被拦截并显示错误。");
        }

        grid.CurrentCell = cell;
        grid.CurrentCell = grid.Rows[0].Cells[0];
        BeginSlaveUnitAddressEdit(grid, 1, "2");
        grid.EndEdit();
        valid = (bool)form.GetType()
            .GetMethod("ValidateConfiguredSlaveUnitIds", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(form, new object[] { false })!;
        if (!valid
            || !string.IsNullOrWhiteSpace(grid.Rows[1].ErrorText)
            || grid.Rows[1].Cells[0].Value is not byte)
        {
            throw new InvalidOperationException("修正重复从站地址后，表格仍处于错误状态。");
        }

        GetField<NumericUpDown>(form, "_slaveManualUnitInput").Value = firstId;
        Console.WriteLine("已验证：重复及非法地址保留原值，页面切换、取消、锁定、新增、删除和旧配置修正均正常。");
    }

    private static void BeginSlaveUnitAddressEdit(DataGridView grid, int rowIndex, string text)
    {
        grid.Focus();
        grid.CurrentCell = grid.Rows[rowIndex].Cells[0];
        if (!grid.BeginEdit(true) || grid.EditingControl is not TextBox addressEditor)
        {
            throw new InvalidOperationException("无法通过实际编辑控件输入从站地址。");
        }

        addressEditor.Text = text;
        grid.NotifyCurrentCellDirty(true);
    }

    private static void CheckSlaveInitialDataValidation(Form owner, SlaveUnitConfiguration source)
    {
        using Form dialog = CreateSlaveInitialDataEditor(source, AddressBase.ZeroBased, SlaveArea.HoldingRegisters);
        dialog.Show(owner);
        Pump(TimeSpan.FromMilliseconds(100));
        DataGridView grid = GetInitialDataGrid(dialog, SlaveArea.HoldingRegisters);
        Button apply = GetField<Button>(dialog, "_applyButton");
        Label error = GetField<Label>(dialog, "_validationLabel");
        grid.Rows[1].Cells[0].Value = 0;
        apply.PerformClick();
        if (!error.Visible || !error.Text.Contains("重复地址", StringComparison.Ordinal)
            || GetUpdatedSlaveInitialData(dialog) is not null)
        {
            throw new InvalidOperationException("重复地址应定位并提示，不得应用。");
        }

        grid.Rows[1].Cells[0].Value = 1;
        grid.Rows[1].Cells[1].Value = "65536";
        apply.PerformClick();
        if (!error.Text.Contains("65535", StringComparison.Ordinal)
            || GetUpdatedSlaveInitialData(dialog) is not null)
        {
            throw new InvalidOperationException("超出 ushort 范围的寄存器初始值未被拒绝。");
        }

        grid.Rows[1].Cells[1].Value = null;
        apply.PerformClick();
        if (GetUpdatedSlaveInitialData(dialog) is not null || !error.Visible)
        {
            throw new InvalidOperationException("空寄存器值应给出提示而不是崩溃或应用。");
        }

        grid.Rows[1].Cells[1].Value = "1";
        grid.CurrentCell = grid.Rows[1].Cells[0];
        grid.BeginEdit(true);
        ((TextBox)grid.EditingControl).Text = "无效地址";
        if (grid.EndEdit() || !error.Text.Contains("整数", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("非整数地址应拦截绑定错误并显示中文提示。");
        }

        grid.CancelEdit();
        grid.Rows[1].Cells[0].Value = -1;
        apply.PerformClick();
        if (!error.Text.Contains("有效地址", StringComparison.Ordinal)
            || GetUpdatedSlaveInitialData(dialog) is not null)
        {
            throw new InvalidOperationException("负数地址未被拒绝。");
        }

        grid.Rows[1].Cells[0].Value = 1;
        GetField<TabControl>(dialog, "_areaTabs").SelectedIndex = 2;
        DataGridView coils = GetInitialDataGrid(dialog, SlaveArea.Coils);
        coils.Rows[0].Cells[1].Value = "不是布尔值";
        apply.PerformClick();
        if (!error.Text.Contains("ON/OFF", StringComparison.Ordinal)
            || GetUpdatedSlaveInitialData(dialog) is not null)
        {
            throw new InvalidOperationException("无效线圈值未被拒绝。");
        }

        FindButton(dialog, "取消")!.PerformClick();
        if (GetUpdatedSlaveInitialData(dialog) is not null
            || source.HoldingRegisters[1].Address != 1
            || source.HoldingRegisters[1].Value != 1
            || !source.Coils[0].Value)
        {
            throw new InvalidOperationException("取消编辑不得改变来源配置。");
        }

        dialog.Close();
    }

    private static void CheckSlaveInitialDataMainEntry(Form owner, string outputDirectory)
    {
        DataGridView units = GetField<DataGridView>(owner, "_slaveUnitGrid");
        string original = (string)units.Rows[0].Cells[1].Value;
        units.Rows[0].Cells[1].Value = string.Join(
            ", ",
            Enumerable.Range(0, 125).Select(index => $"{40001 + index} = {index}"));
        units.CurrentCell = units.Rows[0].Cells[1];
        Pump(TimeSpan.FromMilliseconds(100));
        if (Convert.ToString(units.Rows[0].Cells[1].FormattedValue, CultureInfo.InvariantCulture) != "共 125 项"
            || units.Columns[0].ReadOnly
            || units.Columns.Cast<DataGridViewColumn>().Skip(1).Any(column => !column.ReadOnly))
        {
            throw new InvalidOperationException(
                $"主表应显示紧凑数量摘要，初始值必须在明细窗口编辑。"
                + $"摘要：{units.Rows[0].Cells[1].FormattedValue}，整表只读：{units.ReadOnly}，"
                + $"各列只读：{string.Join(",", units.Columns.Cast<DataGridViewColumn>().Select(column => column.ReadOnly))}");
        }

        SavePage(owner, outputDirectory, "14-初始数据摘要.png");
        WithSlaveInitialDataDialog(
            () => GetField<Button>(owner, "_slaveEditInitialDataButton").PerformClick(),
            dialog =>
            {
                DataGridView grid = GetInitialDataGrid(dialog, SlaveArea.HoldingRegisters);
                if (grid.Rows.Count != 125)
                {
                    throw new InvalidOperationException("主表打开明细后丢失了摘要之外的数据。");
                }

                grid.Rows[0].Cells[1].Value = "321";
                GetField<Button>(dialog, "_applyButton").PerformClick();
            });
        if (!((string)units.Rows[0].Cells[1].Value).StartsWith("40001 = 321", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("明细应用后未回填主表完整配置。");
        }

        WithSlaveInitialDataDialog(
            () => owner.GetType().GetMethod(
                "OnSlaveUnitGridCellDoubleClick",
                BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(owner, new object?[] { units, new DataGridViewCellEventArgs(2, 0) }),
            dialog =>
            {
                if (GetField<TabControl>(dialog, "_areaTabs").SelectedIndex != 1)
                {
                    throw new InvalidOperationException("双击输入寄存器摘要未直接打开对应存储区。");
                }

                dialog.DialogResult = DialogResult.Cancel;
            });
        units.Rows[0].Cells[1].Value = original;
    }

    private static void WithSlaveInitialDataDialog(Action open, Action<Form> interact)
    {
        Exception? failure = null;
        bool visited = false;
        using System.Windows.Forms.Timer timer = new() { Interval = 80 };
        timer.Tick += (_, _) =>
        {
            Form? dialog = Application.OpenForms.Cast<Form>()
                .FirstOrDefault(form => form.GetType().Name == "SlaveInitialDataForm" && form.Modal);
            if (dialog is null)
            {
                return;
            }

            timer.Stop();
            visited = true;
            try
            {
                interact(dialog);
            }
            catch (Exception exception)
            {
                failure = exception;
                dialog.DialogResult = DialogResult.Cancel;
            }
        };
        timer.Start();
        open();
        if (!visited || failure is not null)
        {
            throw new InvalidOperationException("从站初始数据明细交互失败。", failure);
        }
    }

    private static Form CreateSlaveInitialDataEditor(
        SlaveUnitConfiguration source,
        AddressBase addressBase,
        SlaveArea area)
    {
        Type formType = Assembly.Load("ModbusHelper.WinForms")
            .GetType("ModbusHelper.WinForms.SlaveInitialDataForm", throwOnError: true)!;
        return (Form)Activator.CreateInstance(
            formType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: new object[] { source, addressBase, area },
            culture: null)!;
    }

    private static DataGridView GetInitialDataGrid(Form dialog, SlaveArea area)
    {
        return (DataGridView)GetField<System.Collections.IDictionary>(dialog, "_grids")[area]!;
    }

    private static SlaveUnitConfiguration? GetUpdatedSlaveInitialData(Form dialog)
    {
        return (SlaveUnitConfiguration?)dialog.GetType()
            .GetProperty("UpdatedConfiguration")!.GetValue(dialog);
    }

    private static void AssertInitialDataColumnsVisible(DataGridView grid)
    {
        for (int index = 0; index < grid.Columns.Count; index++)
        {
            Rectangle bounds = grid.GetColumnDisplayRectangle(index, cutOverflow: false);
            int textWidth = TextRenderer.MeasureText(
                grid.Columns[index].HeaderText,
                grid.ColumnHeadersDefaultCellStyle.Font ?? grid.Font).Width;
            if (bounds.Left < 0 || bounds.Right > grid.ClientSize.Width || bounds.Width < textWidth)
            {
                throw new InvalidOperationException("初始数据明细的地址或值列没有完整显示。");
            }
        }
    }

    private static void CheckAddressBasePersistence(MethodInfo factory)
    {
        using Form restored = (Form)factory.Invoke(null, null)!;
        restored.Show();
        Pump(TimeSpan.FromMilliseconds(350));
        if (!GetField<ComboBox>(restored, "_addressBaseCombo").Text
                .StartsWith("0 基", StringComparison.Ordinal)
            || !GetField<ComboBox>(restored, "_slaveAddressBaseCombo").Text
                .StartsWith("1 基", StringComparison.Ordinal)
            || GetField<NumericUpDown>(restored, "_manualAddressInput").Value != 0
            || GetField<NumericUpDown>(restored, "_slaveManualAddressInput").Value != 40001
            || GetField<Label>(restored, "_headerStatusLabel").Text != "未连接"
            || GetField<Button>(restored, "_slaveStopButton").Enabled)
        {
            throw new InvalidOperationException("重新打开程序后主从基准应分别恢复，且不能自动启动连接或从站。");
        }

        GetField<TabControl>(restored, "_mainTabs").SelectedIndex = 2;
        BeginSlaveUnitAddressEdit(GetField<DataGridView>(restored, "_slaveUnitGrid"), 0, "无效地址");
        restored.Close();
        Pump(TimeSpan.FromSeconds(2));
        if (!restored.IsDisposed)
        {
            throw new InvalidOperationException("存在无效地址草稿时仍应能正常关闭并释放窗体。");
        }

        Console.WriteLine("已验证：主从基准分别持久化、重启不自动连接、无效地址草稿不阻止关闭。");
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
        string latestReadEntry = result.Text.Split(
                new[] { Environment.NewLine + Environment.NewLine },
                StringSplitOptions.None)
            .Last();
        if (!latestReadEntry.Contains("42 (0x002A)", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("运行中手动模拟读写未生效，或读写历史被覆盖。");
        }

        GetField<TextBox>(form, "_slaveManualValuesInput").Text = "125";
        write.PerformClick();
        string latestWriteEntry = result.Text.Split(
                new[] { Environment.NewLine + Environment.NewLine },
                StringSplitOptions.None)
            .Last();
        if (!latestWriteEntry.StartsWith('【')
            || !latestWriteEntry.Contains("125 (0x007D)", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("运行中写入未即时生效或没有【时间】格式的历史。");
        }

        GetField<Button>(form, "_slaveStopButton").PerformClick();
        PumpUntil(() => slaveStart.Enabled, TimeSpan.FromSeconds(5));
        if (unitGrid.ReadOnly
            || !GetField<Button>(form, "_slaveAddUnitButton").Enabled
            || !GetField<Button>(form, "_slaveDeleteUnitButton").Enabled
            || !GetField<Button>(form, "_slaveEditInitialDataButton").Enabled
            || !slaveUnitsGroup.Text.Contains("已停止时可编辑", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("从站停止后初始数据表及新增、删除、编辑操作应恢复可用并显示状态。");
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
        if (!result.Text.Split(
                new[] { Environment.NewLine + Environment.NewLine },
                StringSplitOptions.None)
                .Last()
                .Contains("100 (0x0064)", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("从站重启后没有恢复初始表中的值 100。");
        }

        ComboBox addressBase = GetField<ComboBox>(form, "_slaveAddressBaseCombo");
        SelectComboByText(addressBase, "0 基");
        if (!Convert.ToString(unitGrid.Rows[0].Cells[1].Value, CultureInfo.InvariantCulture)!
                .Contains("0 = 100", StringComparison.Ordinal)
            || GetField<NumericUpDown>(form, "_slaveManualAddressInput").Value != 0
            || GetField<NumericUpDown>(form, "_manualAddressInput").Value != 0
            || !unitGrid.ReadOnly)
        {
            throw new InvalidOperationException("运行时切换从站 0 基应只换算从站显示，并保持初始值表锁定。");
        }

        read.PerformClick();
        if (!result.Text.Split(
                new[] { Environment.NewLine + Environment.NewLine },
                StringSplitOptions.None)
                .Last()
                .Contains("100 (0x0064)", StringComparison.Ordinal))
        {
            throw new InvalidOperationException("从站切换地址基准后没有读取到同一个协议地址的数据。");
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
        if (entries.Length != 100
            || entries.Any(entry => !entry.Contains('【')
                || !entry.Contains('】')))
        {
            throw new InvalidOperationException("手动模拟结果历史未限制为最近 100 条【时间】记录。");
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
