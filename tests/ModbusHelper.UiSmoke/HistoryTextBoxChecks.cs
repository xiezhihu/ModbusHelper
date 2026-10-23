using System.Reflection;
using System.Runtime.InteropServices;

using ModbusHelper.Business.Logging;
using ModbusHelper.Core.Enums;
using ModbusHelper.Core.Models;

namespace ModbusHelper.UiSmoke;

internal static class HistoryTextBoxChecks
{
    public static void Run(Form form, TabControl tabs, ModbusLogService logService)
    {
        tabs.SelectedIndex = 0;
        foreach ((string field, DataDirection direction) in new[]
        {
            ("_requestHexBox", DataDirection.Tx),
            ("_responseHexBox", DataDirection.Rx)
        })
        {
            TextBox textBox = GetTextBox(form, field);
            logService.Clear();
            Refresh(form, "RefreshCommunicationHistory");
            AddFrames(logService, ModbusRole.Master, direction, 0, 200);
            Refresh(form, "RefreshCommunicationHistory");
            using EditMessageMonitor monitor = new(textBox);
            textBox.Select(textBox.GetFirstCharIndexFromLine(60), 20);
            textBox.ScrollToCaret();
            _ = SendMessage(textBox.Handle, 0x00B6, new IntPtr(30), IntPtr.Zero);
            int previousFirstLine = FirstVisibleLine(textBox);
            int previousHorizontal = GetScrollPos(textBox.Handle, 0);
            int previousTextX = textBox.GetPositionFromCharIndex(textBox.SelectionStart).X;
            string previousTopText = textBox.Lines[previousFirstLine];
            string previousSelection = textBox.SelectedText;
            textBox.Update();
            monitor.Reset();

            AddFrames(logService, ModbusRole.Master, direction, 200, 3);
            Refresh(form, "RefreshCommunicationHistory");
            textBox.Update();
            if (monitor.FullTextReplacements != 0)
            {
                throw new InvalidOperationException(
                    $"{field} 追加记录时发送了 {monitor.FullTextReplacements} 次全量替换文本消息。");
            }

            if (textBox.Lines.Length != 200
                || textBox.Lines[FirstVisibleLine(textBox)] != previousTopText
                || textBox.SelectedText != previousSelection
                || GetScrollPos(textBox.Handle, 0) != previousHorizontal
                || textBox.GetPositionFromCharIndex(textBox.SelectionStart).X != previousTextX)
            {
                throw new InvalidOperationException(
                    $"{field} 淘汰旧记录后，查看位置、水平位置或所选文字发生变化。"
                    + $"首行={previousFirstLine}/{FirstVisibleLine(textBox)}，"
                    + $"水平={previousHorizontal}/{GetScrollPos(textBox.Handle, 0)}。");
            }

            if (monitor.BackgroundErases != 0)
            {
                throw new InvalidOperationException(
                    $"{field} 追加记录时发生了 {monitor.BackgroundErases} 次单独背景擦除。"
                    + $"消息顺序：{string.Join(", ", monitor.Messages)}");
            }

            textBox.Select(textBox.TextLength, 0);
            textBox.ScrollToCaret();
            _ = SendMessage(textBox.Handle, 0x0114, new IntPtr(6), IntPtr.Zero);
            AddFrames(logService, ModbusRole.Master, direction, 203, 3);
            Refresh(form, "RefreshCommunicationHistory");
            if (!textBox.Lines[^1].EndsWith("数据 205", StringComparison.Ordinal)
                || textBox.GetPositionFromCharIndex(textBox.TextLength - 1).Y
                    + textBox.Font.Height > textBox.ClientSize.Height
                || GetScrollPos(textBox.Handle, 0) != 0)
            {
                throw new InvalidOperationException($"{field} 追加后未纵向跟随底部，或发生水平自动滚动。");
            }

            CheckRenderedText(textBox, field);
            int previousAppends = monitor.SelectionReplacements;
            Refresh(form, "RefreshCommunicationHistory");
            if (monitor.SelectionReplacements != previousAppends)
            {
                throw new InvalidOperationException($"{field} 内容未变时仍修改了控件。");
            }

            logService.Clear();
            Refresh(form, "RefreshCommunicationHistory");
            if (textBox.TextLength != 0 || monitor.FullTextReplacements != 0)
            {
                throw new InvalidOperationException($"{field} 清空历史后内容不正确，或退回全量文本赋值。");
            }

            AddFrames(logService, ModbusRole.Master, direction, 0, 1);
            Refresh(form, "RefreshCommunicationHistory");
            string firstEntry = textBox.Text;
            AddFrames(logService, ModbusRole.Master, direction, 1, 1);
            Refresh(form, "RefreshCommunicationHistory");
            if (textBox.Lines.Length != 2
                || !textBox.Text.StartsWith(firstEntry + Environment.NewLine, StringComparison.Ordinal)
                || monitor.FullTextReplacements != 0)
            {
                throw new InvalidOperationException($"{field} 未达到容量上限时也必须直接追加新记录。");
            }

            Console.WriteLine(
                $"已验证 {field} 增量更新：全量替换 {monitor.FullTextReplacements} 次，"
                + $"单独擦背景 {monitor.BackgroundErases} 次；保留上限、选区及双向滚动位置。");
        }

        tabs.SelectedIndex = 2;
        TextBox slaveFrames = GetTextBox(form, "_slaveFrameBox");
        Refresh(form, "RefreshSlaveFrames");
        using (EditMessageMonitor monitor = new(slaveFrames))
        {
            AddFrames(logService, ModbusRole.Slave, DataDirection.Rx, 0, 2);
            Refresh(form, "RefreshSlaveFrames");
            AddFrames(logService, ModbusRole.Slave, DataDirection.Tx, 2, 2);
            Refresh(form, "RefreshSlaveFrames");
            if (monitor.FullTextReplacements != 0 || slaveFrames.Lines.Length != 4)
            {
                throw new InvalidOperationException("从站报文未使用增量追加，或占位文字未正确移除。");
            }
        }

        TextBox manualHistory = GetTextBox(form, "_slaveManualResultBox");
        using (EditMessageMonitor monitor = new(manualHistory))
        {
            for (int index = 0; index < 105; index++)
            {
                Invoke(form, "AppendSlaveManualHistory", $"多行操作 {index}\r\n值：{index}");
            }

            if (monitor.FullTextReplacements != 0
                || !manualHistory.Text.Contains("】 多行操作 5\r\n值：5", StringComparison.Ordinal)
                || manualHistory.Text.Contains("】 多行操作 4\r\n值：4", StringComparison.Ordinal)
                || !manualHistory.Text.EndsWith("值：104", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("从站手动操作未增量保留最近 100 条多行结果。");
            }
        }

        Console.WriteLine("已验证从站报文与手动操作多行历史均增量更新。");
        tabs.SelectedIndex = 0;
    }

    private static void CheckRenderedText(TextBox textBox, string field)
    {
        textBox.Refresh();
        using Bitmap snapshot = new(textBox.ClientSize.Width, textBox.ClientSize.Height);
        using (Graphics graphics = Graphics.FromImage(snapshot))
        {
            graphics.Clear(Color.Magenta);
            IntPtr deviceContext = graphics.GetHdc();
            try
            {
                _ = SendMessage(textBox.Handle, 0x0318, deviceContext, new IntPtr(4));
            }
            finally
            {
                graphics.ReleaseHdc(deviceContext);
            }
        }

        int darkPixels = 0;
        int whitePixels = 0;
        for (int y = 0; y < snapshot.Height; y++)
        {
            for (int x = 0; x < snapshot.Width; x++)
            {
                Color pixel = snapshot.GetPixel(x, y);
                if (pixel.R < 100 && pixel.G < 100 && pixel.B < 100)
                {
                    darkPixels++;
                }
                else if (pixel.R > 240 && pixel.G > 240 && pixel.B > 240)
                {
                    whitePixels++;
                }
            }
        }

        if (darkPixels < 100 || whitePixels < snapshot.Width * snapshot.Height / 5)
        {
            throw new InvalidOperationException($"{field} 内存绘制未呈现白色背景和历史文字。");
        }

        string path = Path.Combine(AppContext.BaseDirectory, "ui-smoke", $"16-{field}-增量绘制.png");
        snapshot.Save(path, System.Drawing.Imaging.ImageFormat.Png);
    }

    private static TextBox GetTextBox(Form form, string field)
    {
        return (TextBox)form.GetType().GetField(
            field,
            BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(form)!;
    }

    private static void Refresh(Form form, string method)
    {
        if (method == "RefreshCommunicationHistory")
        {
            Invoke(form, method, new object?[] { null });
            return;
        }

        Invoke(form, method);
    }

    private static void Invoke(Form form, string method, params object?[] arguments)
    {
        form.GetType().GetMethod(
            method,
            BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(form, arguments);
    }

    private static void AddFrames(
        ModbusLogService service,
        ModbusRole role,
        DataDirection direction,
        int start,
        int count)
    {
        for (int index = start; index < start + count; index++)
        {
            service.AddFrame(new RawFrameRecord(
                DateTimeOffset.Now,
                role,
                direction,
                $"{new string('0', 300)} 数据 {index}",
                "历史增量更新验证",
                null));
        }
    }

    private static int FirstVisibleLine(TextBox textBox)
    {
        return SendMessage(textBox.Handle, 0x00CE, IntPtr.Zero, IntPtr.Zero).ToInt32();
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr handle, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern int GetScrollPos(IntPtr handle, int bar);

    private sealed class EditMessageMonitor : NativeWindow, IDisposable
    {
        public EditMessageMonitor(TextBox textBox)
        {
            AssignHandle(textBox.Handle);
        }

        public int FullTextReplacements { get; private set; }

        public int SelectionReplacements { get; private set; }

        public int BackgroundErases { get; private set; }

        private bool _painting;

        public List<string> Messages { get; } = new();

        public void Reset()
        {
            FullTextReplacements = 0;
            SelectionReplacements = 0;
            BackgroundErases = 0;
            Messages.Clear();
        }

        protected override void WndProc(ref Message message)
        {
            if (message.Msg is 0x000B or 0x000F or 0x0014 or 0x00C2 or 0x0114)
            {
                Messages.Add($"{message.Msg:X4}({message.WParam})");
            }

            if (message.Msg == 0x000C)
            {
                FullTextReplacements++;
            }
            else if (message.Msg == 0x00C2)
            {
                SelectionReplacements++;
            }
            else if (message.Msg == 0x0014)
            {
                if (!_painting)
                {
                    BackgroundErases++;
                }
            }

            bool wasPainting = _painting;
            _painting |= message.Msg == 0x000F;
            try
            {
                base.WndProc(ref message);
            }
            finally
            {
                _painting = wasPainting;
            }
        }

        public void Dispose()
        {
            ReleaseHandle();
        }
    }
}
