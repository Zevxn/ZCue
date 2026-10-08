using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Media.Imaging;

namespace ZCue.Infrastructure;

// 独立画面窗口不连接 WPF 渲染目标，避免异步渲染重新提交上一轮的表面。
internal sealed class LayeredPreviewSurface : System.Windows.Forms.NativeWindow, IDisposable
{
    private void EnsureHandle()
    {
        if (Handle != IntPtr.Zero) return;

        CreateHandle(new System.Windows.Forms.CreateParams
        {
            Caption = "ZCue Preview",
            Style = unchecked((int)0x80000000), // WS_POPUP
            ExStyle = NativeMethods.WS_EX_LAYERED | NativeMethods.WS_EX_NOACTIVATE
                | NativeMethods.WS_EX_TOOLWINDOW | NativeMethods.WS_EX_TRANSPARENT,
            X = -32000,
            Y = -32000,
            Width = 1,
            Height = 1
        });
    }

    // SECTION 预览画面提交

    internal void Present(BitmapSource frame, int left, int top)
    {
        EnsureHandle();
        var screenDc = NativeMethods.GetDC(IntPtr.Zero);
        var memoryDc = NativeMethods.CreateCompatibleDC(screenDc);
        var bitmap = IntPtr.Zero;
        var originalBitmap = IntPtr.Zero;
        try
        {
            if (screenDc == IntPtr.Zero || memoryDc == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            var stride = checked(frame.PixelWidth * 4);
            var bufferSize = checked(stride * frame.PixelHeight);
            var header = new NativeMethods.BitmapInfoHeader
            {
                Size = (uint)Marshal.SizeOf<NativeMethods.BitmapInfoHeader>(),
                Width = frame.PixelWidth,
                Height = -frame.PixelHeight, // 自顶向下的预乘 BGRA，与 Pbgra32 一致。
                Planes = 1,
                BitCount = 32,
                SizeImage = (uint)bufferSize
            };
            bitmap = NativeMethods.CreateDIBSection(screenDc, ref header, 0, out var pixels, IntPtr.Zero, 0);
            if (bitmap == IntPtr.Zero)
                throw new Win32Exception(Marshal.GetLastWin32Error());

            frame.CopyPixels(System.Windows.Int32Rect.Empty, pixels, bufferSize, stride);
            originalBitmap = NativeMethods.SelectObject(memoryDc, bitmap);
            if (originalBitmap == IntPtr.Zero || originalBitmap == new IntPtr(-1))
                throw new Win32Exception(Marshal.GetLastWin32Error());

            var destination = new NativeMethods.Point(left, top);
            var size = new NativeMethods.Point(frame.PixelWidth, frame.PixelHeight);
            var source = new NativeMethods.Point(0, 0);
            var blend = new NativeMethods.BlendFunction { SourceConstantAlpha = 255, AlphaFormat = 1 };
            // 当前像素与位置一次提交；首次显示前也必须先替换完整画面。
            if (!NativeMethods.UpdateLayeredWindow(Handle, screenDc, ref destination, ref size,
                memoryDc, ref source, 0, ref blend, 2)) // ULW_ALPHA
                throw new Win32Exception(Marshal.GetLastWin32Error());

            NativeMethods.SetWindowPos(Handle, NativeMethods.HWND_TOPMOST, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE
                | NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
        }
        finally
        {
            if (originalBitmap != IntPtr.Zero && originalBitmap != new IntPtr(-1))
                NativeMethods.SelectObject(memoryDc, originalBitmap);
            if (bitmap != IntPtr.Zero) NativeMethods.DeleteObject(bitmap);
            if (memoryDc != IntPtr.Zero) NativeMethods.DeleteDC(memoryDc);
            if (screenDc != IntPtr.Zero) NativeMethods.ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    // !SECTION 预览画面提交

    // SECTION 窗口交互与生命周期

    // 销毁原生窗口会同时释放上一轮的分层画面；下次显示时创建全新的画面窗口。
    internal void HideSurface() => Dispose();

    protected override void WndProc(ref System.Windows.Forms.Message message)
    {
        if (message.Msg == NativeMethods.WM_NCHITTEST)
        {
            message.Result = new IntPtr(NativeMethods.HTTRANSPARENT);
            return;
        }

        base.WndProc(ref message);
    }

    public void Dispose()
    {
        if (Handle != IntPtr.Zero) DestroyHandle();
    }

    // !SECTION 窗口交互与生命周期
}
