using System.Diagnostics;
using System.Windows.Automation;
using System.Windows.Automation.Text;
using TypeSense.Infrastructure;

namespace TypeSense.Services;

/// <summary>
/// 从当前焦点文本控件读取光标前的真实文本。
/// 这一步用于补足低级键盘 Hook 无法直接看到的 IME 提交结果。
/// </summary>
public sealed class FocusedTextService
{
    private const int MaxTextBeforeCaretLength = 30;

    public bool TryGetTextBeforeCaret(IntPtr expectedWindow, out string textBeforeCaret)
    {
        textBeforeCaret = string.Empty;
        if (expectedWindow == IntPtr.Zero)
        {
            return false;
        }

        if (IsVisualStudioCodeWindow(expectedWindow))
        {
            return false;
        }

        try
        {
            var currentElement = AutomationElement.FocusedElement;
            if (currentElement is not null
                && string.Equals(
                    currentElement.Current.AutomationId,
                    "RootWebArea",
                    StringComparison.Ordinal))
            {
                return false;
            }

            return currentElement is not null
                && BelongsToWindow(currentElement, expectedWindow)
                && TryReadTextBeforeCaret(currentElement, out textBeforeCaret);
        }
        catch
        {
            // 第三方控件可能不完整实现 TextPattern，交给物理键盘缓冲继续工作。
            return false;
        }
    }

    private static bool TryReadTextBeforeCaret(
        AutomationElement element,
        out string textBeforeCaret)
    {
        textBeforeCaret = string.Empty;
        try
        {
            if (!element.TryGetCurrentPattern(TextPattern.Pattern, out var patternObject)
                || patternObject is not TextPattern textPattern)
            {
                return false;
            }

            var selections = textPattern.GetSelection();
            if (selections.Length == 0)
            {
                return false;
            }

            var caretRange = selections[^1];
            var textBeforeRange = caretRange.Clone();
            textBeforeRange.MoveEndpointByRange(
                TextPatternRangeEndpoint.End,
                caretRange,
                TextPatternRangeEndpoint.Start);
            textBeforeRange.MoveEndpointByUnit(
                TextPatternRangeEndpoint.Start,
                TextUnit.Character,
                -MaxTextBeforeCaretLength);
            textBeforeCaret = textBeforeRange.GetText(MaxTextBeforeCaretLength);
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

    private static bool IsVisualStudioCodeWindow(IntPtr window)
    {
        try
        {
            NativeMethods.GetWindowThreadProcessId(window, out var processId);
            if (processId == 0)
            {
                return false;
            }

            using var process = Process.GetProcessById((int)processId);
            return string.Equals(process.ProcessName, "Code", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }
}
