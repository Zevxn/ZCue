using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using ZCue.Infrastructure;
using ZCue.Services;

namespace ZCue.Views;

internal partial class WindowSelectionOverlay : Window
{
    private uint _processId;
    private int _iconVersion;
    private bool _closed;

    public WindowSelectionOverlay()
    {
        InitializeComponent();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var handle = new WindowInteropHelper(this).Handle;
        var style = NativeMethods.GetWindowLongPtr(handle, NativeMethods.GWL_EXSTYLE).ToInt64();
        // 透明浮层必须穿透鼠标，不能挡住下一次按坐标查找目标窗口。
        NativeMethods.SetWindowLongPtr(handle, NativeMethods.GWL_EXSTYLE,
            new IntPtr(style | NativeMethods.WS_EX_NOACTIVATE | NativeMethods.WS_EX_TOOLWINDOW
                | NativeMethods.WS_EX_TRANSPARENT));
    }

    // SECTION 目标高亮与图标

    public void ShowTarget(WindowSelectionTarget? target)
    {
        if (target is null)
        {
            _processId = 0;
            ++_iconVersion;
            Hide();
            return;
        }

        ProcessName.Text = target.ProcessName;
        if (_processId != target.ProcessId)
        {
            _processId = target.ProcessId;
            ApplicationIcon.Source = null;
            FallbackIcon.Visibility = Visibility.Visible;
            _ = LoadIconAsync(target.ExecutablePath, ++_iconVersion);
        }

        if (!IsVisible) Show();
        var bounds = target.Bounds;
        // Win32 窗口矩形使用物理像素，避免不同 DPI 屏幕间用 DIP 定位产生偏移。
        NativeMethods.SetWindowPos(new WindowInteropHelper(this).Handle, NativeMethods.HWND_TOPMOST,
            bounds.Left, bounds.Top, bounds.Right - bounds.Left, bounds.Bottom - bounds.Top,
            NativeMethods.SWP_NOACTIVATE);
        UpdateLayout();
        if (!NativeMethods.GetCursorPos(out var cursor)) return;
        var area = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(cursor.X, cursor.Y)).WorkingArea;
        var start = PointFromScreen(new System.Windows.Point(Math.Max(bounds.Left, area.Left), Math.Max(bounds.Top, area.Top)));
        var end = PointFromScreen(new System.Windows.Point(Math.Min(bounds.Right, area.Right), Math.Min(bounds.Bottom, area.Bottom)));
        var pointer = PointFromScreen(new System.Windows.Point(cursor.X + 18, cursor.Y + 18));
        Canvas.SetLeft(Hint, Math.Clamp(pointer.X, start.X + 6, Math.Max(start.X + 6, end.X - Hint.ActualWidth - 6)));
        Canvas.SetTop(Hint, Math.Clamp(pointer.Y, start.Y + 6, Math.Max(start.Y + 6, end.Y - Hint.ActualHeight - 6)));
    }

    private async Task LoadIconAsync(string? path, int version)
    {
        var icon = await Task.Run(() => ApplicationIconLoader.Load(path));
        if (_closed || version != _iconVersion) return;
        ApplicationIcon.Source = icon;
        FallbackIcon.Visibility = icon is null ? Visibility.Visible : Visibility.Collapsed;
    }

    // !SECTION 目标高亮与图标

    protected override void OnClosed(EventArgs e)
    {
        _closed = true;
        base.OnClosed(e);
    }
}
