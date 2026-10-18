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
    public static Button CreateButton(string text, Action action, int width = 96)
    {
        Button button = new()
        {
            Text = text,
            Width = width,
            Height = 30,
            Margin = new Padding(4),
            UseVisualStyleBackColor = true
        };
        button.Click += (_, _) => action();
        return button;
    }

    public static Label CreateLabel(string text, bool bold = false)
    {
        return new Label
        {
            Text = text,
            AutoSize = true,
            Anchor = AnchorStyles.Left,
            Margin = new Padding(4, 7, 4, 4),
            Font = bold
                ? new Font("Microsoft YaHei UI", 9F, FontStyle.Bold)
                : new Font("Microsoft YaHei UI", 9F)
        };
    }

    public static TextBox CreateTextBox(string text = "")
    {
        return new TextBox
        {
            Text = text,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Margin = new Padding(4)
        };
    }

    public static NumericUpDown CreateNumber(decimal minimum, decimal maximum, decimal value)
    {
        return new NumericUpDown
        {
            Minimum = minimum,
            Maximum = maximum,
            Value = Math.Clamp(value, minimum, maximum),
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
            Margin = new Padding(4)
        };
    }

    public static ComboBox CreateCombo()
    {
        return new ComboBox
        {
            DropDownStyle = ComboBoxStyle.DropDownList,
            Anchor = AnchorStyles.Left | AnchorStyles.Right,
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

    public static TableLayoutPanel CreateFieldTable(int rows)
    {
        TableLayoutPanel table = new()
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = rows,
            Padding = new Padding(4)
        };
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 116F));
        table.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        for (int index = 0; index < rows; index++)
        {
            table.RowStyles.Add(new RowStyle(SizeType.Absolute, 34F));
        }

        return table;
    }

    public static void AddField(TableLayoutPanel table, int row, string label, Control control)
    {
        table.Controls.Add(CreateLabel(label), 0, row);
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
}
