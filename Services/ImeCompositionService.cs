using ZCue.Infrastructure;
using System.Text;

namespace ZCue.Services;

public sealed class ImeCompositionService
{
    private const double OimeCandidateMinimumWidthDips = 200;

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
            var isCandidateWindow = name.Contains("CandidateUI", StringComparison.OrdinalIgnoreCase);
            if (name.Equals("OimeDirectUIWindow", StringComparison.Ordinal))
            {
                // 豆包的常驻工具栏与候选面板同类；候选面板宽度按窗口 DPI 换算后判定。
                var dpi = NativeMethods.GetDpiForWindow(window);
                var widthDips = (rectangle.Right - rectangle.Left) * 96d / (dpi == 0 ? 96u : dpi);
                isCandidateWindow = widthDips >= OimeCandidateMinimumWidthDips;
            }

            if (isCandidateWindow)
            {
                found = true;
                return false;
            }

            return true;
        }, IntPtr.Zero);
        return found;
    }

    public bool HasCandidateList(IntPtr targetWindow)
    {
        if (targetWindow == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            var focusedWindow = GetFocusedWindow(targetWindow);
            return HasCandidateListOn(focusedWindow)
                || focusedWindow != targetWindow && HasCandidateListOn(targetWindow);
        }
        catch
        {
            return false;
        }
    }

    public bool IsComposing(IntPtr targetWindow)
    {
        if (targetWindow == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            var focusedWindow = GetFocusedWindow(targetWindow);
            return IsComposingOn(focusedWindow)
                || focusedWindow != targetWindow && IsComposingOn(targetWindow);
        }
        catch
        {
            // 不支持 IMM32 的控件继续使用键盘缓冲区和 UI Automation 同步。
            return false;
        }
    }

    private static IntPtr GetFocusedWindow(IntPtr targetWindow)
    {
        var threadId = NativeMethods.GetWindowThreadProcessId(targetWindow, out _);
        if (threadId == 0)
        {
            return targetWindow;
        }

        var threadInfo = new NativeMethods.GuiThreadInfo
        {
            CbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<NativeMethods.GuiThreadInfo>()
        };
        return NativeMethods.GetGUIThreadInfo(threadId, ref threadInfo)
            && threadInfo.HWndFocus != IntPtr.Zero
            ? threadInfo.HWndFocus
            : targetWindow;
    }

    private static bool HasCandidateListOn(IntPtr window)
    {
        var inputContext = NativeMethods.ImmGetContext(window);
        if (inputContext == IntPtr.Zero)
        {
            return false;
        }

        try
        {
            var requiredSize = NativeMethods.ImmGetCandidateListCount(
                inputContext,
                out var candidateListCount);
            return requiredSize > 0 && candidateListCount > 0;
        }
        finally
        {
            NativeMethods.ImmReleaseContext(window, inputContext);
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
