namespace ModbusHelper.WinForms;

/// <summary>
/// 仅 UI 使用的依赖装配入口。后续在这里注入业务服务，避免在窗体中创建底层依赖。
/// </summary>
internal static class AppCompositionRoot
{
    /// <summary>
    /// 创建主窗体及其依赖。
    /// </summary>
    /// <returns>已完成依赖装配的主窗体。</returns>
    public static MainForm CreateMainForm()
    {
        return new MainForm();
    }
}
