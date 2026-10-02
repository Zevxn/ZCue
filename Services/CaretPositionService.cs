using System.Windows.Automation;
using System.Runtime.InteropServices;
using System.Windows.Automation.Text;
using ZCue.Infrastructure;

namespace ZCue.Services;

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
    // SECTION 光标定位与估算位置

    public CaretPosition GetPosition(IntPtr foregroundWindow)
    {
        var dpiScale = GetDpiScale(foregroundWindow);

        if (TryGetGuiThreadPosition(foregroundWindow, out var guiPosition))
        {
            return guiPosition with { DpiScale = dpiScale };
        }

        var hasAutomationPosition = TryGetUiAutomationCaretPosition(foregroundWindow, out var automationPosition)
            || TryGetUiAutomationPosition(foregroundWindow, out automationPosition);
        if (hasAutomationPosition && !automationPosition.IsFallback)
        {
            return automationPosition with { DpiScale = dpiScale };
        }

        if (TryGetAccessiblePosition(foregroundWindow, out var accessiblePosition))
        {
            return accessiblePosition with { DpiScale = dpiScale };
        }

        if (hasAutomationPosition)
        {
            return automationPosition with { DpiScale = dpiScale };
        }

        var hasCursor = NativeMethods.GetCursorPos(out var cursorPosition);
        if (hasCursor && (foregroundWindow == IntPtr.Zero
            || IsPointInWindow(cursorPosition.X, cursorPosition.Y, foregroundWindow)))
        {
            return EstimateAt(cursorPosition.X, cursorPosition.Y, dpiScale);
        }

        // 鼠标可能停在另一个应用或显示器上；这时用当前焦点控件的中心估算。
        // 不复用上次光标，避免切换输入框或滚动后沿用旧位置。
        if (TryGetNativeControlBounds(foregroundWindow, out var bounds))
        {
            return EstimateAt(bounds.Left + (bounds.Right - bounds.Left) / 2,
                bounds.Top + (bounds.Bottom - bounds.Top) / 2, dpiScale);
        }

        if (hasCursor)
        {
            return EstimateAt(cursorPosition.X, cursorPosition.Y, dpiScale);
        }

        return new CaretPosition(20, 20, 21, 38, dpiScale, true);
    }

    private static CaretPosition EstimateAt(int left, int top, double dpiScale) =>
        new(left, top, left + 1, top + 18, dpiScale, true);

    // !SECTION 光标定位与估算位置

    // SECTION UI Automation 光标定位

    private static bool TryGetUiAutomationCaretPosition(IntPtr foregroundWindow, out CaretPosition position)
    {
        position = default;
        UiAutomationInterop.IAutomation? automation = null;
        UiAutomationInterop.IElement? element = null;
        object? patternObject = null;
        UiAutomationInterop.ITextRange? range = null;
        try
        {
            if (foregroundWindow == IntPtr.Zero)
            {
                return false;
            }

            automation = (UiAutomationInterop.IAutomation?)Activator.CreateInstance(
                Type.GetTypeFromCLSID(UiAutomationInterop.ClassId, throwOnError: true)!);
            element = automation?.GetFocusedElement();
            NativeMethods.GetWindowThreadProcessId(foregroundWindow, out var processId);
            if (element is null || processId == 0
                || element.GetCurrentPropertyValue(UiAutomationInterop.ProcessIdProperty) is not int focusedProcessId
                || (uint)focusedProcessId != processId)
            {
                return false;
            }

            patternObject = element.GetCurrentPattern(UiAutomationInterop.TextPattern2Id);
            if (patternObject is not UiAutomationInterop.ITextPattern2 pattern)
            {
                return false;
            }

            range = pattern.GetCaretRange(out var isActive);
            if (!isActive || range is null || NativeMethods.GetForegroundWindow() != foregroundWindow)
            {
                return false;
            }

            if (TryGetNativeRangePosition(range, foregroundWindow, false, false, out position))
            {
                return true;
            }

            // 零长度范围经常没有矩形。只扩展范围副本，绝不修改控件选区。
            return TryGetAdjacentNativeRangePosition(range, foregroundWindow, true, out position)
                || TryGetAdjacentNativeRangePosition(range, foregroundWindow, false, out position);
        }
        catch
        {
            // 控件可能不支持 TextPattern2，继续走旧 TextPattern 和 MSAA。
            return false;
        }
        finally
        {
            UiAutomationInterop.Release(range);
            UiAutomationInterop.Release(patternObject);
            UiAutomationInterop.Release(element);
            UiAutomationInterop.Release(automation);
        }
    }

    private static bool TryGetNativeRangePosition(
        UiAutomationInterop.ITextRange range, IntPtr foregroundWindow,
        bool useRightEdge, bool isFallback, out CaretPosition position)
    {
        position = default;
        var rectangles = range.GetBoundingRectangles();
        if (rectangles.Length < 4 || rectangles.Length % 4 != 0)
        {
            return false;
        }

        var offset = useRightEdge ? rectangles.Length - 4 : 0;
        var left = rectangles[offset];
        var top = rectangles[offset + 1];
        var width = rectangles[offset + 2];
        var height = rectangles[offset + 3];
        if (!double.IsFinite(width) || width < 0 || !double.IsFinite(height) || height <= 0)
        {
            return false;
        }

        return TryCreateCaretPosition(left + (useRightEdge ? width : 0), top,
            isFallback ? 1 : width, height, foregroundWindow, isFallback, out position);
    }

    private static bool TryGetAdjacentNativeRangePosition(
        UiAutomationInterop.ITextRange range, IntPtr foregroundWindow,
        bool forward, out CaretPosition position)
    {
        position = default;
        UiAutomationInterop.ITextRange? adjacent = null;
        try
        {
            adjacent = range.Clone();
            var endpoint = forward ? TextPatternRangeEndpoint.End : TextPatternRangeEndpoint.Start;
            if (adjacent.MoveEndpointByUnit((int)endpoint, (int)TextUnit.Character, forward ? 1 : -1) == 0)
            {
                return false;
            }

            // 换行字符的矩形可能属于上一行，不能拿来推断下一行的插入位置。
            if (adjacent.GetText(2).IndexOfAny(['\r', '\n']) >= 0)
            {
                return false;
            }

            return TryGetNativeRangePosition(adjacent, foregroundWindow, !forward, true, out position);
        }
        catch
        {
            return false;
        }
        finally
        {
            UiAutomationInterop.Release(adjacent);
        }
    }

    private static bool TryGetUiAutomationPosition(IntPtr foregroundWindow, out CaretPosition position)
    {
        position = default;

        try
        {
            var focusedElement = AutomationElement.FocusedElement;
            if (focusedElement is null || !BelongsToWindow(focusedElement, foregroundWindow))
            {
                return false;
            }

            if (focusedElement.TryGetCurrentPattern(TextPattern.Pattern, out var legacyPatternObject)
                && legacyPatternObject is TextPattern textPattern)
            {
                var selections = textPattern.GetSelection();
                // 有选区时无法从旧 TextPattern 判断活动端，不把选区左上角当成光标。
                if (selections.Length == 1
                    && selections[0].CompareEndpoints(TextPatternRangeEndpoint.Start,
                        selections[0], TextPatternRangeEndpoint.End) == 0)
                {
                    var range = selections[0];
                    return TryGetRangePosition(range, foregroundWindow, false, false, out position)
                        || TryGetAdjacentRangePosition(range, foregroundWindow, true, out position)
                        || TryGetAdjacentRangePosition(range, foregroundWindow, false, out position);
                }
            }
        }
        catch
        {
            // UIA 对第三方控件可能抛出 COMException，继续走 MSAA。
        }

        return false;
    }

    private static bool TryGetRangePosition(
        TextPatternRange range, IntPtr foregroundWindow,
        bool useRightEdge, bool isFallback, out CaretPosition position)
    {
        position = default;

        try
        {
            var rectangles = range.GetBoundingRectangles();
            if (rectangles.Length < 1)
            {
                return false;
            }

            var rectangle = useRightEdge ? rectangles[^1] : rectangles[0];
            if (!double.IsFinite(rectangle.Width) || rectangle.Width < 0
                || !double.IsFinite(rectangle.Height) || rectangle.Height <= 0)
            {
                return false;
            }

            return TryCreateCaretPosition(useRightEdge ? rectangle.Right : rectangle.Left,
                rectangle.Top, isFallback ? 1 : rectangle.Width, rectangle.Height,
                foregroundWindow, isFallback, out position);
        }
        catch
        {
            return false;
        }
    }

    private static bool TryGetAdjacentRangePosition(
        TextPatternRange range, IntPtr foregroundWindow, bool forward, out CaretPosition position)
    {
        position = default;
        try
        {
            var adjacent = range.Clone();
            var endpoint = forward ? TextPatternRangeEndpoint.End : TextPatternRangeEndpoint.Start;
            if (adjacent.MoveEndpointByUnit(endpoint, TextUnit.Character, forward ? 1 : -1) == 0)
            {
                return false;
            }

            if (adjacent.GetText(2).IndexOfAny(['\r', '\n']) >= 0)
            {
                return false;
            }

            return TryGetRangePosition(adjacent, foregroundWindow, !forward, true, out position);
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
        if (expectedWindow == IntPtr.Zero || NativeMethods.GetForegroundWindow() != expectedWindow)
        {
            return false;
        }

        NativeMethods.GetWindowThreadProcessId(expectedWindow, out var processId);
        if (processId == 0 || (uint)element.Current.ProcessId != processId)
        {
            return false;
        }

        var nativeHandle = element.Current.NativeWindowHandle;
        if (nativeHandle == 0)
        {
            return true;
        }

        var rootWindow = NativeMethods.GetAncestor(nativeHandle, NativeMethods.GA_ROOT);
        return rootWindow == expectedWindow;
    }

    // !SECTION UI Automation 输入控件边界

    // SECTION Win32 与 MSAA 光标定位

    private static bool TryGetAccessiblePosition(IntPtr foregroundWindow, out CaretPosition position)
    {
        position = default;
        if (!TryGetGuiThreadInfo(foregroundWindow, out var info))
        {
            return false;
        }

        // 自绘控件可在焦点窗口上暴露 MSAA caret；NULL 则查询系统 caret 对象。
        foreach (var window in new[] { info.HWndFocus, info.HWndCaret, IntPtr.Zero }.Distinct())
        {
            if (window != IntPtr.Zero
                && NativeMethods.GetAncestor(window, NativeMethods.GA_ROOT) != foregroundWindow)
            {
                continue;
            }

            Accessibility.IAccessible? accessible = null;
            try
            {
                var interfaceId = typeof(Accessibility.IAccessible).GUID;
                if (NativeMethods.AccessibleObjectFromWindow(window, NativeMethods.OBJID_CARET,
                        ref interfaceId, out accessible) < 0 || accessible is null)
                {
                    continue;
                }

                var state = Convert.ToInt32(accessible.get_accState(NativeMethods.CHILDID_SELF));
                if ((state & (NativeMethods.STATE_SYSTEM_INVISIBLE | NativeMethods.STATE_SYSTEM_OFFSCREEN)) != 0)
                {
                    continue;
                }

                accessible.accLocation(out var left, out var top, out var width, out var height,
                    NativeMethods.CHILDID_SELF);
                if (TryCreateCaretPosition(left, top, width, height, foregroundWindow, false, out position))
                {
                    return true;
                }
            }
            catch
            {
                // 第三方应用可能未实现 caret 对象或在查询期间销毁对象。
            }
            finally
            {
                UiAutomationInterop.Release(accessible);
            }
        }

        return false;
    }

    private static bool TryGetGuiThreadInfo(IntPtr foregroundWindow, out NativeMethods.GuiThreadInfo info)
    {
        info = new NativeMethods.GuiThreadInfo
        {
            CbSize = (uint)Marshal.SizeOf<NativeMethods.GuiThreadInfo>()
        };
        if (foregroundWindow == IntPtr.Zero || NativeMethods.GetForegroundWindow() != foregroundWindow)
        {
            return false;
        }

        var threadId = NativeMethods.GetWindowThreadProcessId(foregroundWindow, out _);
        // threadId=0 会查询调用线程，不能让失效的目标句柄变成 ZCue 自己的坐标。
        return threadId != 0 && NativeMethods.GetGUIThreadInfo(threadId, ref info);
    }

    private static bool TryGetGuiThreadPosition(IntPtr foregroundWindow, out CaretPosition position)
    {
        position = default;
        if (!TryGetGuiThreadInfo(foregroundWindow, out var info)
            || info.HWndCaret == IntPtr.Zero
            || info.RcCaret.Bottom <= info.RcCaret.Top
            || info.RcCaret.Right < info.RcCaret.Left
            || NativeMethods.GetAncestor(info.HWndCaret, NativeMethods.GA_ROOT) != foregroundWindow)
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

        return TryCreateCaretPosition(topLeft.X, topLeft.Y,
            bottomRight.X - topLeft.X, bottomRight.Y - topLeft.Y,
            foregroundWindow, false, out position);
    }

    private static bool TryGetNativeControlBounds(
        IntPtr foregroundWindow,
        out TextControlBounds bounds)
    {
        bounds = default;
        if (!TryGetGuiThreadInfo(foregroundWindow, out var info))
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

    // !SECTION Win32 与 MSAA 光标定位

    // SECTION 光标坐标校验

    private static bool TryCreateCaretPosition(
        double left, double top, double width, double height,
        IntPtr foregroundWindow, bool isFallback, out CaretPosition position)
    {
        position = default;
        if (!double.IsFinite(left) || !double.IsFinite(top)
            || !double.IsFinite(width) || !double.IsFinite(height)
            || width < 0 || height <= 0 || width > Math.Max(4, height)
            || left < int.MinValue || top < int.MinValue
            || left + Math.Max(1, width) > int.MaxValue
            || top + Math.Max(16, height) > int.MaxValue
            || !IsPointInWindow(left, top, foregroundWindow)
            || NativeMethods.GetForegroundWindow() != foregroundWindow)
        {
            return false;
        }

        var caretLeft = (int)Math.Round(left);
        var caretTop = (int)Math.Round(top);
        var caretRight = caretLeft + Math.Max(1, (int)Math.Round(width));
        var caretBottom = caretTop + Math.Max(16, (int)Math.Round(height));
        if (!System.Windows.Forms.Screen.AllScreens.Any(screen =>
                caretLeft < screen.Bounds.Right && caretRight > screen.Bounds.Left
                && caretTop < screen.Bounds.Bottom && caretBottom > screen.Bounds.Top))
        {
            return false;
        }

        position = new CaretPosition(caretLeft, caretTop, caretRight, caretBottom, 1, isFallback);
        return true;
    }

    private static bool IsPointInWindow(double x, double y, IntPtr window) =>
        window != IntPtr.Zero && NativeMethods.GetWindowRect(window, out var rectangle)
        && x >= rectangle.Left && x < rectangle.Right
        && y >= rectangle.Top && y < rectangle.Bottom;

    // !SECTION 光标坐标校验
}
