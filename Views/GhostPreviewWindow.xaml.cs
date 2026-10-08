using System.Windows;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
    private readonly LayeredPreviewSurface _surface = new();

    public GhostPreviewWindow()
    {
        InitializeComponent();
        _foregroundTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(120)
        };
        _foregroundTimer.Tick += HandleForegroundTimerTick;

        // WPF 窗口只在屏幕外排版；实际预览由独立原生窗口提交完整画面。
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

        if (string.IsNullOrEmpty(text) || targetWindow == IntPtr.Zero
            || NativeMethods.GetForegroundWindow() != targetWindow)
        {
            HidePreview();
            return;
        }

        GhostClip.Opacity = 1;
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

        UpdateLayout();

        var pixelWidth = Math.Max(1, (int)Math.Ceiling(GhostClip.ActualWidth * scale));
        var pixelHeight = Math.Max(1, (int)Math.Ceiling(
            Math.Min(GhostClip.ActualHeight, GhostText.DesiredSize.Height) * scale));
        var frame = new RenderTargetBitmap(pixelWidth, pixelHeight, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        frame.Render(GhostClip);
        if (NativeMethods.GetForegroundWindow() != targetWindow)
        {
            HidePreview();
            return;
        }

        _surface.Present(frame, textAreaLeft, caretPosition.Top);
        _targetWindow = targetWindow;
        _foregroundTimer.Start();
    }

    /// <summary>
    /// 释放原生画面并清空排版内容；下次显示时创建新窗口并提交当前画面。
    /// </summary>
    public void HidePreview()
    {
        Dispatcher.VerifyAccess();
        _surface.HideSurface();
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
        _surface.Dispose();
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
