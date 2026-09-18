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

public sealed class CaretPositionService
{
    public CaretPosition GetPosition(IntPtr foregroundWindow)
    {
        var dpiScale = GetDpiScale(foregroundWindow);

        if (TryGetUiAutomationPosition(out var automationPosition))
        {
            return automationPosition with { DpiScale = dpiScale };
        }

        if (TryGetGuiThreadPosition(foregroundWindow, out var guiPosition))
        {
            return guiPosition with { DpiScale = dpiScale, IsFallback = true };
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
