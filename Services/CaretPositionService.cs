using System.Windows.Automation;
using System.Windows.Automation.Text;
using TypeSense.Infrastructure;

namespace TypeSense.Services;

public readonly record struct CaretPosition(
    int Left,
    int Top,
    int Right,
    int Bottom,
    double DpiScale,
    bool IsFallback);

public readonly record struct TextControlBounds(
    int Left,
    int Top,
    int Right,
    int Bottom);

public sealed class CaretPositionService
{
    public CaretPosition GetPosition(IntPtr foregroundWindow)
    {
        var dpiScale = GetDpiScale(foregroundWindow);

        if (TryGetGuiThreadPosition(foregroundWindow, out var guiPosition))
        {
            return guiPosition with { DpiScale = dpiScale, IsFallback = true };
        }

        if (TryGetUiAutomationPosition(out var automationPosition))
        {
            return automationPosition with { DpiScale = dpiScale };
        }

        if (foregroundWindow != IntPtr.Zero && NativeMethods.GetWindowRect(foregroundWindow, out var windowRect))
        {
            return new CaretPosition(
                windowRect.Left + 24,
                windowRect.Top + 36,
                windowRect.Left + 25,
                windowRect.Top + 56,
                dpiScale,
                true);
        }

        if (NativeMethods.GetCursorPos(out var cursorPosition))
        {
            return new CaretPosition(
                cursorPosition.X,
                cursorPosition.Y,
                cursorPosition.X + 1,
                cursorPosition.Y + 18,
                dpiScale,
                true);
        }

        return new CaretPosition(20, 20, 21, 38, dpiScale, true);
    }

    // SECTION UI Automation 光标定位

    private static bool TryGetUiAutomationPosition(out CaretPosition position)
    {
        position = default;

        try
        {
            var focusedElement = AutomationElement.FocusedElement;
            if (focusedElement is null)
            {
                return false;
            }

            if (focusedElement.TryGetCurrentPattern(TextPattern.Pattern, out var legacyPatternObject)
                && legacyPatternObject is TextPattern textPattern)
            {
                var selections = textPattern.GetSelection();
                if (selections.Length > 0 && TryGetRangePosition(selections[^1], out position))
                {
                    return true;
                }
            }
        }
        catch
        {
            // UIA 对第三方控件可能抛出 COMException，交给 Win32 fallback 处理。
        }

        return false;
    }

    private static bool TryGetRangePosition(TextPatternRange range, out CaretPosition position)
    {
        position = default;

        try
        {
            var rectangles = range.GetBoundingRectangles();
            if (rectangles.Length < 1)
            {
                return false;
            }

            var rectangle = rectangles[0];
            var left = (int)Math.Round(rectangle.Left);
            var top = (int)Math.Round(rectangle.Top);
            var width = Math.Max(1, (int)Math.Round(rectangle.Width));
            var height = Math.Max(16, (int)Math.Round(rectangle.Height));
            position = new CaretPosition(left, top, left + width, top + height, 1, false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    // !SECTION UI Automation 光标定位

    // SECTION UI Automation 输入控件边界

    public bool TryGetTextControlBounds(
        IntPtr foregroundWindow,
        out TextControlBounds bounds)
    {
        bounds = default;
        if (foregroundWindow == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            var currentElement = AutomationElement.FocusedElement;
            while (currentElement is not null)
            {
                if (BelongsToWindow(currentElement, foregroundWindow)
                    && currentElement.TryGetCurrentPattern(TextPattern.Pattern, out _)
                    && TryGetElementBounds(currentElement, out bounds))
                {
                    return true;
                }

                currentElement = TreeWalker.ControlViewWalker.GetParent(currentElement);
            }
        }
        catch
        {
            // 某些第三方控件在读取 UI Automation 属性时会抛出 COMException。
        }

        return TryGetNativeControlBounds(foregroundWindow, out bounds);
    }

    private static bool TryGetElementBounds(
        AutomationElement element,
        out TextControlBounds bounds)
    {
        bounds = default;

        try
        {
            var rectangle = element.Current.BoundingRectangle;
            if (rectangle.Width <= 0
                || rectangle.Height <= 0
                || double.IsNaN(rectangle.Left)
                || double.IsNaN(rectangle.Top)
                || double.IsNaN(rectangle.Right)
                || double.IsNaN(rectangle.Bottom))
            {
                return false;
            }

            var left = (int)Math.Round(rectangle.Left);
            var top = (int)Math.Round(rectangle.Top);
            var right = Math.Max(left + 1, (int)Math.Round(rectangle.Right));
            var bottom = Math.Max(top + 1, (int)Math.Round(rectangle.Bottom));
            bounds = new TextControlBounds(left, top, right, bottom);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool BelongsToWindow(AutomationElement element, IntPtr expectedWindow)
    {
        var nativeHandle = element.Current.NativeWindowHandle;
        if (nativeHandle == 0)
        {
            return true;
        }

        var rootWindow = NativeMethods.GetAncestor(nativeHandle, NativeMethods.GA_ROOT);
        return rootWindow == IntPtr.Zero || rootWindow == expectedWindow;
    }

    // !SECTION UI Automation 输入控件边界

    // SECTION Win32 fallback 光标定位

    private static bool TryGetGuiThreadPosition(IntPtr foregroundWindow, out CaretPosition position)
    {
        position = default;
        if (foregroundWindow == IntPtr.Zero)
        {
            return false;
        }

        var threadId = NativeMethods.GetWindowThreadProcessId(foregroundWindow, out _);
        var info = new NativeMethods.GuiThreadInfo
        {
            CbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.GuiThreadInfo>()
        };

        if (!NativeMethods.GetGUIThreadInfo(threadId, ref info) || info.HWndCaret == IntPtr.Zero)
        {
            return false;
        }

        var topLeft = new NativeMethods.Point(info.RcCaret.Left, info.RcCaret.Top);
        var bottomRight = new NativeMethods.Point(info.RcCaret.Right, info.RcCaret.Bottom);
        if (!NativeMethods.ClientToScreen(info.HWndCaret, ref topLeft)
            || !NativeMethods.ClientToScreen(info.HWndCaret, ref bottomRight))
        {
            return false;
        }

        var height = Math.Max(16, bottomRight.Y - topLeft.Y);
        position = new CaretPosition(
            topLeft.X,
            topLeft.Y,
            Math.Max(topLeft.X + 1, bottomRight.X),
            topLeft.Y + height,
            1,
            true);
        return true;
    }

    private static bool TryGetNativeControlBounds(
        IntPtr foregroundWindow,
        out TextControlBounds bounds)
    {
        bounds = default;
        var threadId = NativeMethods.GetWindowThreadProcessId(foregroundWindow, out _);
        var info = new NativeMethods.GuiThreadInfo
        {
            CbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.GuiThreadInfo>()
        };

        if (!NativeMethods.GetGUIThreadInfo(threadId, ref info))
        {
            return false;
        }

        var textWindow = info.HWndCaret != IntPtr.Zero
            ? info.HWndCaret
            : info.HWndFocus != IntPtr.Zero ? info.HWndFocus : foregroundWindow;
        var rootWindow = NativeMethods.GetAncestor(textWindow, NativeMethods.GA_ROOT);
        if (rootWindow != IntPtr.Zero && rootWindow != foregroundWindow)
        {
            return false;
        }

        if (!NativeMethods.GetWindowRect(textWindow, out var rectangle)
            || rectangle.Right <= rectangle.Left
            || rectangle.Bottom <= rectangle.Top)
        {
            return false;
        }

        bounds = new TextControlBounds(
            rectangle.Left,
            rectangle.Top,
            rectangle.Right,
            rectangle.Bottom);
        return true;
    }

    private static double GetDpiScale(IntPtr foregroundWindow)
    {
        try
        {
            var dpi = foregroundWindow == IntPtr.Zero ? 96u : NativeMethods.GetDpiForWindow(foregroundWindow);
            return dpi == 0 ? 1 : dpi / 96d;
        }
        catch
        {
            return 1;
        }
    }

    // !SECTION Win32 fallback 光标定位
}
