using System.Windows;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Threading;
using ZCue.Infrastructure;
using ZCue.Services;
using Forms = System.Windows.Forms;

namespace ZCue.Views;

public partial class GhostPreviewWindow : Window
{
    /// <summary>
    /// 隐藏用的屏幕外坐标。窗口保持可见，只把坐标挪到屏幕外。
    /// </summary>
    private const double OffScreenCoordinate = -32000;

    private IntPtr _windowHandle;
    private IntPtr _targetWindow;
    private HwndSource? _windowSource;
    private readonly DispatcherTimer _foregroundTimer;

    public GhostPreviewWindow()
    {
        InitializeComponent();
        _foregroundTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(120)
        };
        _foregroundTimer.Tick += HandleForegroundTimerTick;

        // 启动时先显示一次并停在屏幕外，此后不再 Show/Hide。
        // 分层窗口 Hide 后再 Show 时 DWM 会回放上一次的合成表面，这是"旧预览闪回"的来源；
        // 常驻可见、只用坐标表达"隐藏"就没有这个瞬间。
        Left = OffScreenCoordinate;
        Top = OffScreenCoordinate;
        GhostClip.Opacity = 0;
        Show();
    }

    // SECTION 幽灵文字渲染与定位

    public void ShowPreview(
        string text,
        CaretPosition caretPosition,
        IntPtr targetWindow,
        TextControlBounds? controlBounds = null)
    {
        Dispatcher.VerifyAccess();

        if (string.IsNullOrEmpty(text))
        {
            HidePreview();
            return;
        }

        GhostClip.Opacity = 1;
        _targetWindow = targetWindow;
        _foregroundTimer.Start();
        var scale = caretPosition.DpiScale <= 0 ? 1 : caretPosition.DpiScale;
        var screenPoint = new System.Drawing.Point(caretPosition.Left, caretPosition.Top);
        var workArea = Forms.Screen.FromPoint(screenPoint).WorkingArea;
        var textAreaLeft = controlBounds is { } bounds
            ? bounds.Left + 4
            : caretPosition.Left;
        var rightBoundary = Math.Min(controlBounds?.Right ?? workArea.Right - 8, workArea.Right - 8);
        var bottomBoundary = Math.Min(controlBounds?.Bottom ?? workArea.Bottom - 8, workArea.Bottom - 8);
        var availableWidth = Math.Max(1, rightBoundary - textAreaLeft - 4);
        var availableHeight = Math.Max(1, bottomBoundary - caretPosition.Top - 2);
        var caretHeight = Math.Max(16, caretPosition.Bottom - caretPosition.Top) / scale;
        var fontSize = Math.Clamp(caretHeight * 0.86, 10, 48);
        var availableWidthDip = availableWidth / scale;
        var availableHeightDip = availableHeight / scale;
        var firstLineIndentDip = Math.Clamp(
            (caretPosition.Left - textAreaLeft) / scale,
            0,
            availableWidthDip);

        var previewLines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        GhostText.Inlines.Clear();
        if (firstLineIndentDip > 0)
        {
            GhostText.Inlines.Add(new InlineUIContainer(new System.Windows.Shapes.Rectangle
            {
                Width = firstLineIndentDip,
                Height = 1,
                Fill = System.Windows.Media.Brushes.Transparent
            }));
        }

        for (var index = 0; index < previewLines.Length; index++)
        {
            if (index > 0)
            {
                GhostText.Inlines.Add(new LineBreak());
            }

            if (!string.IsNullOrEmpty(previewLines[index]))
            {
                GhostText.Inlines.Add(new Run(previewLines[index]));
            }
        }

        GhostText.FontSize = fontSize;
        GhostText.LineHeight = Math.Max(fontSize * 1.1, caretHeight);
        GhostClip.Width = availableWidthDip;
        GhostClip.Height = availableHeightDip;
        GhostText.MaxWidth = availableWidthDip;
        GhostText.MaxHeight = availableHeightDip;
        Width = availableWidthDip;
        Height = availableHeightDip;

        // 窗口常驻可见，这里只原地改坐标、尺寸与内容，整段在同一个 UI 任务里完成，
        // 会一起合成，不会让上一轮的坐标或文字成帧。
        Left = textAreaLeft / scale;
        Top = caretPosition.Top / scale;

        UpdateLayout();

        if (_windowHandle != IntPtr.Zero)
        {
            NativeMethods.SetWindowPos(
                _windowHandle,
                NativeMethods.HWND_TOPMOST,
                0,
                0,
                0,
                0,
                NativeMethods.SWP_NOMOVE
                    | NativeMethods.SWP_NOSIZE
                    | NativeMethods.SWP_NOACTIVATE);
        }
    }

    /// <summary>
    /// 隐藏预览：移出屏幕，然后把窗口表面清空。
    /// 只移出屏幕不够 —— DWM 缓存着上一次提交的表面，下次移回时会先画出上一轮的幽灵文字。
    /// 清掉文本、缩到 1×1 并置为全透明，缓存里就是一张空白；顺带免去大尺寸表面的合成开销。
    /// </summary>
    public void HidePreview()
    {
        Dispatcher.VerifyAccess();
        _foregroundTimer.Stop();
        _targetWindow = IntPtr.Zero;
        Left = OffScreenCoordinate;
        Top = OffScreenCoordinate;
        GhostText.Inlines.Clear();
        GhostClip.Opacity = 0;
        Width = 1;
        Height = 1;
    }

    // !SECTION 幽灵文字渲染与定位

    // SECTION 透明窗口样式

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        _windowHandle = new WindowInteropHelper(this).Handle;

        var extendedStyle = NativeMethods.GetWindowLongPtr(_windowHandle, NativeMethods.GWL_EXSTYLE).ToInt64();
        var transparentStyle = extendedStyle
            | NativeMethods.WS_EX_NOACTIVATE
            | NativeMethods.WS_EX_TOOLWINDOW
            | NativeMethods.WS_EX_TRANSPARENT;
        NativeMethods.SetWindowLongPtr(_windowHandle, NativeMethods.GWL_EXSTYLE, new IntPtr(transparentStyle));

        _windowSource = HwndSource.FromHwnd(_windowHandle);
        _windowSource?.AddHook(WindowProc);
    }

    protected override void OnClosed(EventArgs e)
    {
        _foregroundTimer.Stop();
        _foregroundTimer.Tick -= HandleForegroundTimerTick;
        _windowSource?.RemoveHook(WindowProc);
        _windowSource = null;
        base.OnClosed(e);
    }

    private void HandleForegroundTimerTick(object? sender, EventArgs e)
    {
        // 窗口常驻可见，无法再用 IsVisible 判断是否已隐藏；_targetWindow 才是当前是否
        // 正在显示的依据（HidePreview 会把它清空，并停掉本计时器）。
        if (_targetWindow == IntPtr.Zero || NativeMethods.GetForegroundWindow() != _targetWindow)
        {
            HidePreview();
        }
    }

    private static IntPtr WindowProc(
        IntPtr hwnd,
        int message,
        IntPtr wParam,
        IntPtr lParam,
        ref bool handled)
    {
        if (message == NativeMethods.WM_NCHITTEST)
        {
            handled = true;
            return (IntPtr)NativeMethods.HTTRANSPARENT;
        }

        return IntPtr.Zero;
    }

    // !SECTION 透明窗口样式
}
