using System.Windows;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Threading;
using TypeSense.Infrastructure;
using TypeSense.Services;
using Forms = System.Windows.Forms;

namespace TypeSense.Views;

public partial class GhostPreviewWindow : Window
{
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

        if (!IsVisible)
        {
            Show();
        }

        UpdateLayout();
        Left = textAreaLeft / scale;
        Top = caretPosition.Top / scale;

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
                    | NativeMethods.SWP_NOACTIVATE
                    | NativeMethods.SWP_SHOWWINDOW);
        }
    }

    public void HidePreview()
    {
        Dispatcher.VerifyAccess();
        _foregroundTimer.Stop();
        _targetWindow = IntPtr.Zero;
        Hide();
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
        if (!IsVisible)
        {
            _foregroundTimer.Stop();
            return;
        }

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
