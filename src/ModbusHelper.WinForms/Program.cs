namespace ModbusHelper.WinForms;

internal static class Program
{
    /// <summary>
    /// 应用程序入口。仅负责选择运行模式并启动对应宿主。
    /// </summary>
    [STAThread]
    private static void Main(string[] args)
    {
        if (args.Contains("--headless", StringComparer.OrdinalIgnoreCase))
        {
            RunHeadless(args);
            return;
        }

        ApplicationConfiguration.Initialize();
        RunGui();
    }

    /// <summary>
    /// 启动图形界面宿主。
    /// </summary>
    private static void RunGui()
    {
        Application.Run(AppCompositionRoot.CreateMainForm());
    }

    /// <summary>
    /// 启动无头宿主，供二期命令行模式和自动化脚本复用。
    /// </summary>
    /// <param name="args">完整的命令行参数。</param>
    /// <exception cref="NotImplementedException">无头模式尚未实现。</exception>
    private static void RunHeadless(string[] args)
    {
        throw new NotImplementedException("无头模式将在二期实现。");
    }
}
