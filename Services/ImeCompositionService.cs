using TypeSense.Infrastructure;
using System.Text;

namespace TypeSense.Services;

public sealed class ImeCompositionService
{
    public bool HasVisibleCandidateWindow()
    {
        var found = false;
        NativeMethods.EnumWindows((window, _) =>
        {
            if (!NativeMethods.IsWindowVisible(window)
                || !NativeMethods.GetWindowRect(window, out var rectangle)
                || rectangle.Right <= rectangle.Left
                || rectangle.Bottom <= rectangle.Top)
            {
                return true;
            }

            var className = new StringBuilder(256);
            NativeMethods.GetClassName(window, className, className.Capacity);
            var name = className.ToString();
            if (name.Equals("OimeDirectUIWindow", StringComparison.Ordinal)
                || name.Contains("CandidateUI", StringComparison.OrdinalIgnoreCase))
            {
                found = true;
                return false;
            }

            return true;
        }, IntPtr.Zero);
        return found;
    }

    public bool IsComposing(IntPtr targetWindow)
    {
        return CheckFocusedWindowOrTarget(targetWindow, IsComposingOn);
    }

    private static bool CheckFocusedWindowOrTarget(
        IntPtr targetWindow,
        Func<IntPtr, bool> checkWindow)
    {
        if (targetWindow == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            var threadId = NativeMethods.GetWindowThreadProcessId(targetWindow, out _);
            var focusedWindow = targetWindow;
            if (threadId != 0)
            {
                var threadInfo = new NativeMethods.GuiThreadInfo
                {
                    CbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.GuiThreadInfo>()
                };
                if (NativeMethods.GetGUIThreadInfo(threadId, ref threadInfo)
                    && threadInfo.HWndFocus != IntPtr.Zero)
                {
                    focusedWindow = threadInfo.HWndFocus;
                }
            }

            return checkWindow(focusedWindow)
                || focusedWindow != targetWindow && checkWindow(targetWindow);
        }
        catch
        {
            // 不支持 IMM32 的控件继续使用键盘缓冲区和 UI Automation 同步。
            return false;
        }
    }

    private static bool IsComposingOn(IntPtr window)
    {
        var inputContext = NativeMethods.ImmGetContext(window);
        if (inputContext == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            return NativeMethods.ImmGetCompositionString(
                inputContext,
                NativeMethods.GCS_COMPSTR,
                IntPtr.Zero,
                0) > 0;
        }
        finally
        {
            NativeMethods.ImmReleaseContext(window, inputContext);
        }
    }
}
