namespace ModbusHelper.WinForms;

/// <summary>
/// 仅 UI 使用的主窗体。当前仅提供工程骨架，业务功能按任务清单逐步接入。
/// </summary>
public sealed class MainForm : Form
{
    /// <summary>
    /// 初始化主窗体。
    /// </summary>
    public MainForm()
    {
        Text = "Modbus 调试工具";
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(1280, 800);
        MinimumSize = new Size(1024, 720);
        AutoScaleMode = AutoScaleMode.Dpi;
    }
}
