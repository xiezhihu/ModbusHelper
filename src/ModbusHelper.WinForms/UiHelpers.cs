using System.Runtime.InteropServices;

using ModbusHelper.Core.Enums;

namespace ModbusHelper.WinForms;

/// <summary>
/// 仅 UI 使用的控件选择项。显示中文，值为领域枚举。
/// </summary>
/// <typeparam name="T">值类型。</typeparam>
internal sealed class UiOption<T>
{
    public UiOption(T value, string text)
    {
        Value = value;
        Text = text;
    }

    public T Value { get; }

    public string Text { get; }

    public override string ToString()
    {
        return Text;
    }
}

/// <summary>
/// 仅 UI 使用的控件构造辅助方法。
/// </summary>
internal static class UiHelpers
{
    private const uint EmGetFirstVisibleLine = 0x00CE;
    private const uint EmLineScroll = 0x00B6;

    /// <summary>
    /// 多行文本框刷新前的滚动状态。
    /// </summary>
    public readonly record struct TextBoxScrollState(bool FollowBottom, int FirstVisibleLine);

    /// <summary>
    /// 按当前控件的 DPI 将设计时像素换算为实际像素。
    /// </summary>
    public static int Scale(Control control, int logicalPixels)
    {
        int dpi = control.DeviceDpi > 0 ? control.DeviceDpi : 96;
        return Math.Max(1, (int)Math.Ceiling(logicalPixels * dpi / 96F));
    }

    /// <summary>
    /// 测量单行文字所需的宽度，供表格列和下拉框在高 DPI 下留出足够空间。
    /// </summary>
    public static int MeasureTextWidth(IEnumerable<string> texts, Font font, int padding = 0)
    {
        int width = texts
            .Select(text => TextRenderer.MeasureText(
                text,
                font,
                new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width)
            .DefaultIfEmpty(0)
            .Max();
        return width + padding;
    }

    /// <summary>
    /// 为可编辑表格设置统一的行高和表头高度，避免字体放大后文字被边界裁切。
    /// </summary>
    public static void ConfigureGridMetrics(DataGridView grid)
    {
        grid.AutoSizeRowsMode = DataGridViewAutoSizeRowsMode.None;
        using ComboBox comboProbe = new()
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Font = grid.Font,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        int comboHeight = comboProbe.GetPreferredSize(new Size(0, 0)).Height;
        grid.RowTemplate.Height = Math.Max(
            Scale(grid, 26),
            comboHeight + Scale(grid, 2));
        grid.ColumnHeadersHeight = Scale(grid, 34);
        grid.ColumnHeadersHeightSizeMode =
            DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
        grid.ColumnHeadersDefaultCellStyle.Alignment =
            DataGridViewContentAlignment.MiddleCenter;
        grid.ColumnHeadersDefaultCellStyle.WrapMode = DataGridViewTriState.False;
        grid.DefaultCellStyle.WrapMode = DataGridViewTriState.False;
        grid.DefaultCellStyle.Padding = Padding.Empty;
    }

    public static Button CreateButton(string text, Action action, int width = 96)
    {
        Font font = new("Microsoft YaHei UI", 9F);
        Size textSize = TextRenderer.MeasureText(
            text,
            font,
            new Size(int.MaxValue, int.MaxValue),
            TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        Button button = new()
        {
            Text = text,
            Width = Math.Max(width, textSize.Width + 28),
            Height = Math.Max(30, textSize.Height + 12),
            Font = font,
            Margin = new Padding(4),
            UseVisualStyleBackColor = true
        };
        button.Click += (_, _) => action();
        return button;
    }

    public static Label CreateLabel(
        string text,
        bool bold = false,
        ContentAlignment alignment = ContentAlignment.MiddleLeft)
    {
        Label label = new()
        {
            Text = text,
            AutoSize = true,
            UseCompatibleTextRendering = false,
            Anchor = AnchorStyles.Left,
            TextAlign = alignment,
            Margin = new Padding(4, 7, 4, 4),
            Font = bold
                ? new Font("Microsoft YaHei UI", 9F, FontStyle.Bold)
                : new Font("Microsoft YaHei UI", 9F)
        };
        return label;
    }

    public static TextBox CreateTextBox(string text = "")
    {
        return new TextBox
        {
            Text = text,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            MinimumSize = new Size(0, 28),
            Margin = new Padding(4)
        };
    }

    /// <summary>
    /// 判断多行文本框当前是否已经在末尾。用户主动滚到旧记录后，
    /// 新记录不会强行夺回滚动位置。
    /// </summary>
    public static bool IsTextBoxAtBottom(TextBox textBox)
    {
        if (textBox.IsDisposed || textBox.TextLength == 0)
        {
            return true;
        }

        try
        {
            Point lastCharacterPosition = textBox.GetPositionFromCharIndex(textBox.TextLength - 1);
            int visibleHeight = textBox.ClientSize.Height;
            if (textBox.ScrollBars is ScrollBars.Horizontal or ScrollBars.Both)
            {
                visibleHeight -= SystemInformation.HorizontalScrollBarHeight;
            }

            return lastCharacterPosition.Y + textBox.Font.Height <= visibleHeight + Scale(textBox, 2);
        }
        catch (ArgumentOutOfRangeException)
        {
            return true;
        }
    }

    /// <summary>
    /// 保存多行文本框当前的滚动位置。除了是否跟随底部外，
    /// 还记录顶部可见行，供刷新文本后恢复用户正在查看的旧记录。
    /// </summary>
    public static TextBoxScrollState CaptureTextBoxScrollState(TextBox textBox)
    {
        if (textBox.IsDisposed)
        {
            return new TextBoxScrollState(true, 0);
        }

        int firstVisibleLine = GetFirstVisibleLine(textBox);

        TextBoxScrollState state = new(
            IsTextBoxAtBottom(textBox),
            firstVisibleLine);
        return state;
    }

    /// <summary>
    /// 恢复多行文本框刷新前的滚动位置。用户原本在底部时滚到最新记录，
    /// 否则恢复原先顶部可见行，避免刷新历史时跳到顶部或强行跟随到底部。
    /// </summary>
    public static void RestoreTextBoxScroll(
        TextBox textBox,
        TextBoxScrollState state)
    {
        if (textBox.IsDisposed)
        {
            return;
        }

        if (state.FollowBottom)
        {
            ScrollTextBoxToEnd(textBox);
            return;
        }

        try
        {
            int maxLine = Math.Max(0, textBox.Lines.Length - 1);
            int targetLine = Math.Clamp(state.FirstVisibleLine, 0, maxLine);
            ScrollTextBoxToLine(textBox, targetLine);
        }
        catch (ArgumentOutOfRangeException)
        {
            // 文本框可能正在销毁或重新布局，无法恢复滚动位置时保持默认状态。
        }
    }

    private static int GetFirstVisibleLine(TextBox textBox)
    {
        if (textBox.IsHandleCreated)
        {
            return SendMessage(
                textBox.Handle,
                EmGetFirstVisibleLine,
                IntPtr.Zero,
                IntPtr.Zero).ToInt32();
        }

        try
        {
            int charIndex = textBox.GetCharIndexFromPosition(new Point(1, 1));
            return charIndex < 0 ? 0 : textBox.GetLineFromCharIndex(charIndex);
        }
        catch (ArgumentOutOfRangeException)
        {
            return 0;
        }
    }

    private static void ScrollTextBoxToLine(TextBox textBox, int targetLine)
    {
        int currentLine = GetFirstVisibleLine(textBox);
        int delta = targetLine - currentLine;
        if (delta != 0 && textBox.IsHandleCreated)
        {
            SendMessage(
                textBox.Handle,
                EmLineScroll,
                IntPtr.Zero,
                new IntPtr(delta));
        }

        if (GetFirstVisibleLine(textBox) != targetLine)
        {
            int charIndex = textBox.GetFirstCharIndexFromLine(targetLine);
            if (charIndex < 0)
            {
                charIndex = 0;
            }

            textBox.SelectionStart = charIndex;
            textBox.SelectionLength = 0;
            textBox.ScrollToCaret();
        }
    }

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(
        IntPtr handle,
        uint message,
        IntPtr wParam,
        IntPtr lParam);

    /// <summary>
    /// 按调用前记录的底部状态滚动到最新内容。历史区域统一按时间正序追加，
    /// 因此最新记录位于末尾；使用控件自身的滚动逻辑，不调用逐条 Invoke。
    /// </summary>
    public static void ScrollTextBoxToEnd(TextBox textBox, bool followBottom = true)
    {
        if (textBox.IsDisposed || !followBottom)
        {
            return;
        }

        textBox.SelectionStart = textBox.TextLength;
        textBox.SelectionLength = 0;
        textBox.ScrollToCaret();
    }

    /// <summary>
    /// 判断数据表当前是否已经显示最后一行。
    /// </summary>
    public static bool IsGridAtBottom(DataGridView grid)
    {
        return grid.IsDisposed
            || grid.Rows.Count == 0
            || grid.FirstDisplayedScrollingRowIndex < 0
            || grid.FirstDisplayedScrollingRowIndex + grid.DisplayedRowCount(false) >= grid.Rows.Count;
    }

    /// <summary>
    /// 按调用前记录的底部状态滚动到最后一行，适用于动态追加的最近值表。
    /// </summary>
    public static void ScrollGridToBottom(DataGridView grid, bool followBottom = true)
    {
        if (grid.IsDisposed || grid.Rows.Count == 0 || !followBottom)
        {
            return;
        }

        int lastRowIndex = grid.Rows.Count - 1;
        if (grid.Rows[lastRowIndex].Visible)
        {
            grid.FirstDisplayedScrollingRowIndex = lastRowIndex;
        }
    }

    public static NumericUpDown CreateNumber(decimal minimum, decimal maximum, decimal value)
    {
        return new NumericUpDown
        {
            Minimum = minimum,
            Maximum = maximum,
            Value = Math.Clamp(value, minimum, maximum),
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            MinimumSize = new Size(0, 28),
            Margin = new Padding(4)
        };
    }

    public static ComboBox CreateCombo()
    {
        return new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            MinimumSize = new Size(0, 28),
            Margin = new Padding(4)
        };
    }

    public static GroupBox CreateGroup(string text)
    {
        return new GroupBox
        {
            Text = text,
            Dock = DockStyle.Fill,
            Padding = new Padding(8),
            Margin = new Padding(4)
        };
    }

    public static GroupBox CreateParameterGroup(string text)
    {
        GroupBox group = CreateGroup(text);
        group.AutoSize = true;
        group.AutoSizeMode = AutoSizeMode.GrowAndShrink;
        group.Dock = DockStyle.Top;
        group.Padding = new Padding(8, 4, 8, 4);
        group.Margin = new Padding(4, 6, 4, 6);
        return group;
    }

    public static TableLayoutPanel CreateFieldTable(int rows, bool autoSize = false)
    {
        TableLayoutPanel table = new()
        {
            Dock = autoSize ? DockStyle.Top : DockStyle.Fill,
            ColumnCount = 2,
            RowCount = rows,
            Padding = new Padding(2, 0, 2, 0),
            AutoSize = autoSize,
            AutoSizeMode = AutoSizeMode.GrowAndShrink
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116F));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        for (int index = 0; index < rows; index++)
        {
            // 使用首选高度而不是固定 34 像素，避免高 DPI 或中文字体放大后裁切。
            table.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        }

        return table;
    }

    public static void AddField(TableLayoutPanel table, int row, string label, Control control)
    {
        Label fieldLabel = CreateLabel(label);
        fieldLabel.AutoSize = false;
        fieldLabel.Dock = DockStyle.Fill;
        fieldLabel.TextAlign = ContentAlignment.MiddleLeft;
        fieldLabel.Margin = new Padding(4, 2, 4, 2);
        table.Controls.Add(fieldLabel, 0, row);
        table.Controls.Add(control, 1, row);
    }

    public static void SetInitialSplitterLayout(
        SplitContainer split,
        int desiredDistance,
        int panel1Minimum,
        int panel2Minimum)
    {
        EventHandler? apply = null;
        apply = (_, _) =>
        {
            int available = split.ClientSize.Width - split.SplitterWidth;
            if (available <= panel1Minimum + panel2Minimum)
            {
                return;
            }

            split.Panel1MinSize = 0;
            split.Panel2MinSize = 0;
            int maximum = available - panel2Minimum;
            int distance = Math.Clamp(desiredDistance, panel1Minimum, maximum);
            split.SplitterDistance = distance;
            split.Panel1MinSize = panel1Minimum;
            split.Panel2MinSize = panel2Minimum;
            split.SizeChanged -= apply;
        };
        split.SizeChanged += apply;
        split.HandleCreated += (_, _) =>
            split.BeginInvoke((Action)(() => apply(split, EventArgs.Empty)));
    }

    public static void SetEnumOptions<T>(
        ComboBox comboBox,
        IEnumerable<T> values,
        Func<T, string> text)
        where T : struct, Enum
    {
        comboBox.Items.Clear();
        foreach (T value in values)
        {
            comboBox.Items.Add(new UiOption<T>(value, text(value)));
        }
    }

    public static void SelectOption<T>(ComboBox comboBox, T value)
        where T : struct, Enum
    {
        for (int index = 0; index < comboBox.Items.Count; index++)
        {
            if (comboBox.Items[index] is UiOption<T> option
                && EqualityComparer<T>.Default.Equals(option.Value, value))
            {
                comboBox.SelectedIndex = index;
                return;
            }
        }
    }

    public static T GetSelectedValue<T>(ComboBox comboBox, T fallback)
        where T : struct, Enum
    {
        return comboBox.SelectedItem is UiOption<T> option ? option.Value : fallback;
    }

    public static string DataTypeText(DataValueType type)
    {
        return type switch
        {
            DataValueType.Int16 => "Int16",
            DataValueType.UInt16 => "UInt16",
            DataValueType.Int32 => "Int32",
            DataValueType.UInt32 => "UInt32",
            DataValueType.Float => "Float",
            DataValueType.Double => "Double",
            DataValueType.Ascii => "ASCII 字符串",
            DataValueType.Utf8 => "UTF-8 字符串",
            _ => type.ToString()
        };
    }

    public static string ShortFunctionText(ModbusFunction function)
    {
        return function switch
        {
            ModbusFunction.ReadCoils => "01 线圈",
            ModbusFunction.ReadDiscreteInputs => "02 离散输入",
            ModbusFunction.ReadHoldingRegisters => "03 保持寄存器",
            ModbusFunction.ReadInputRegisters => "04 输入寄存器",
            ModbusFunction.WriteSingleCoil => "05 写线圈",
            ModbusFunction.WriteSingleRegister => "06 写寄存器",
            ModbusFunction.WriteMultipleCoils => "0F 写多线圈",
            ModbusFunction.WriteMultipleRegisters => "10 写多寄存器",
            _ => function.ToString()
        };
    }

    public static string ByteOrderText(RegisterByteOrder order)
    {
        return order switch
        {
            RegisterByteOrder.ABCD => "ABCD（标准）",
            RegisterByteOrder.CDAB => "CDAB（交换字序）",
            RegisterByteOrder.BADC => "BADC（寄存器内字节反转）",
            RegisterByteOrder.DCBA => "DCBA（交换字序 + 字节反转）",
            _ => order.ToString()
        };
    }

    public static string ShortByteOrderText(RegisterByteOrder order)
    {
        return order.ToString();
    }
}
