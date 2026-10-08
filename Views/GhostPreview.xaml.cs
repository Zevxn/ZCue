using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ZCue.Infrastructure;
using ZCue.Services;
using Forms = System.Windows.Forms;

namespace ZCue.Views;

public sealed partial class GhostPreview : Border, IDisposable
{
    private IntPtr _targetWindow;
    private readonly DispatcherTimer _foregroundTimer;
    private readonly LayeredPreviewSurface _surface = new();

    public GhostPreview()
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

        if (string.IsNullOrEmpty(text) || targetWindow == IntPtr.Zero
            || NativeMethods.GetForegroundWindow() != targetWindow)
        {
            HidePreview();
            return;
        }

        var scale = caretPosition.DpiScale <= 0 ? 1 : caretPosition.DpiScale;
        // 离屏控件没有 HWND 提供 DPI，按目标输入框的 DPI 排版。
        VisualTreeHelper.SetRootDpi(this, new DpiScale(scale, scale));
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
        GhostText.MaxWidth = availableWidthDip;
        GhostText.MaxHeight = availableHeightDip;
        Width = availableWidthDip;
        Height = availableHeightDip;

        var layoutSize = new System.Windows.Size(availableWidthDip, availableHeightDip);
        Measure(layoutSize);
        Arrange(new Rect(layoutSize));
        // 子控件的布局更新可能仍在队列中，生成位图前必须完成本轮排版。
        UpdateLayout();

        var pixelWidth = Math.Max(1, (int)Math.Ceiling(ActualWidth * scale));
        var pixelHeight = Math.Max(1, (int)Math.Ceiling(
            Math.Min(ActualHeight, GhostText.DesiredSize.Height) * scale));
        var frame = new RenderTargetBitmap(pixelWidth, pixelHeight, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
        frame.Render(this);
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
        GhostText.Inlines.Clear();
    }

    // !SECTION 幽灵文字渲染与定位

    // SECTION 预览生命周期

    public void Dispose()
    {
        HidePreview();
        _foregroundTimer.Tick -= HandleForegroundTimerTick;
    }

    private void HandleForegroundTimerTick(object? sender, EventArgs e)
    {
        if (_targetWindow == IntPtr.Zero || NativeMethods.GetForegroundWindow() != _targetWindow)
        {
            HidePreview();
        }
    }

    // !SECTION 预览生命周期
}
