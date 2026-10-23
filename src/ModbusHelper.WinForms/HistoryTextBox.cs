using System.Runtime.InteropServices;

namespace ModbusHelper.WinForms;

/// <summary>
/// 将原生多行编辑框的客户区绘制到内存后一次呈现，避免历史更新时闪出空白背景。
/// </summary>
internal sealed class HistoryTextBox : TextBox
{
    private const int WmPaint = 0x000F;
    private const int WmEraseBackground = 0x0014;
    private const int WmPrintClient = 0x0318;
    private const int PrfClient = 0x0004;

    protected override void WndProc(ref Message message)
    {
        if (message.Msg == WmEraseBackground)
        {
            message.Result = new IntPtr(1);
            return;
        }

        if (message.Msg == WmPrintClient && message.WParam != IntPtr.Zero
            && ClientSize.Width > 0 && ClientSize.Height > 0)
        {
            PaintBuffered(message.WParam);
            message.Result = IntPtr.Zero;
            return;
        }

        if (message.Msg != WmPaint || ClientSize.Width == 0 || ClientSize.Height == 0)
        {
            base.WndProc(ref message);
            return;
        }

        IntPtr deviceContext = BeginPaint(Handle, out PaintState paintState);
        try
        {
            PaintBuffered(deviceContext);
        }
        finally
        {
            _ = EndPaint(Handle, ref paintState);
        }

        message.Result = IntPtr.Zero;
    }

    private void PaintBuffered(IntPtr deviceContext)
    {
        using Graphics target = Graphics.FromHdc(deviceContext);
        using BufferedGraphics buffer = BufferedGraphicsManager.Current.Allocate(target, ClientRectangle);
        buffer.Graphics.Clear(BackColor);
        IntPtr bufferContext = buffer.Graphics.GetHdc();
        try
        {
            Message printMessage = Message.Create(
                Handle,
                WmPrintClient,
                bufferContext,
                new IntPtr(PrfClient));
            base.WndProc(ref printMessage);
        }
        finally
        {
            buffer.Graphics.ReleaseHdc(bufferContext);
        }

        buffer.Render(target);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PaintState
    {
        public IntPtr DeviceContext;
        public int Erase;
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
        public int Restore;
        public int IncrementalUpdate;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] Reserved;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr BeginPaint(IntPtr window, out PaintState paintState);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool EndPaint(IntPtr window, ref PaintState paintState);
}
