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
    private static void Main()
    {
        string testConfigurationPath = Path.Combine(
            Path.GetTempPath(),
            $"modbus-helper-ui-smoke-{Guid.NewGuid():N}.json");
        Environment.SetEnvironmentVariable("MODBUSHELPER_CONFIG_PATH", testConfigurationPath);
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

        CheckLayout(tabs);
        RunSelfLoopInteraction(form, tabs, outputDirectory);
        form.Close();
        Pump(TimeSpan.FromSeconds(2));
        if (File.Exists(testConfigurationPath))
        {
            File.Delete(testConfigurationPath);
        }

        Console.WriteLine("界面冒烟检查通过。");
    }

    private static void RunSelfLoopInteraction(
        Form form,
        TabControl tabs,
        string outputDirectory)
    {
        int port = GetFreeTcpPort();
        NumericUpDown slavePort = GetField<NumericUpDown>(form, "_slaveListenPortInput");
        Button slaveStart = GetField<Button>(form, "_slaveStartButton");
        Label slaveState = GetField<Label>(form, "_slaveStateLabel");
        tabs.SelectedIndex = 2;
        slavePort.Value = port;
        slaveStart.PerformClick();
        Pump(TimeSpan.FromMilliseconds(600));
        if (!slaveState.Text.Contains("监听", StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"从站未进入监听状态：{slaveState.Text}");
        }

        NumericUpDown masterPort = GetField<NumericUpDown>(form, "_tcpPortInput");
        Button connect = GetField<Button>(form, "_connectButton");
        Label headerStatus = GetField<Label>(form, "_headerStatusLabel");
        tabs.SelectedIndex = 0;
        masterPort.Value = port;
        connect.PerformClick();
        Pump(TimeSpan.FromMilliseconds(650));
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

        for (int index = 1; index < 5; index++)
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
        Button startAll = FindButton(tabs, "启动全部")
            ?? throw new InvalidOperationException("未找到启动全部按钮。");
        startAll.PerformClick();
        Pump(TimeSpan.FromMilliseconds(700));

        tabs.SelectedIndex = 0;
        Button execute = GetField<Button>(form, "_executeManualButton");
        TextBox request = GetField<TextBox>(form, "_requestHexBox");
        TextBox response = GetField<TextBox>(form, "_responseHexBox");
        execute.PerformClick();
        Pump(TimeSpan.FromMilliseconds(650));
        if (string.IsNullOrWhiteSpace(request.Text) || string.IsNullOrWhiteSpace(response.Text))
        {
            throw new InvalidOperationException("界面手动读取未收到完整请求和响应报文。");
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
                new List<PointConfiguration>(),
                AddressBase.ZeroBased
            },
            culture: null)!;
        form.Show();
        Pump(TimeSpan.FromMilliseconds(250));
        SavePage(form, outputDirectory, "9-点位表.png");
        form.Close();
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

    private static void CheckLayout(TabControl tabs)
    {
        foreach (TabPage page in tabs.TabPages)
        {
            if (page.Width < 800 || page.Height < 500)
            {
                throw new InvalidOperationException(
                    $"页签“{page.Text}”可用尺寸过小：{page.Width}x{page.Height}。");
            }

            foreach (Control control in EnumerateControls(page))
            {
                if (control.Visible && (control.Width <= 0 || control.Height <= 0))
                {
                    throw new InvalidOperationException(
                        $"页签“{page.Text}”存在零尺寸控件：{control.GetType().Name}。");
                }
            }
        }
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
