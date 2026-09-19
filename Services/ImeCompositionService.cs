using TypeSense.Infrastructure;

namespace TypeSense.Services;

public sealed class ImeCompositionService
{
    public bool IsComposing(IntPtr targetWindow)
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

            return IsComposingOn(focusedWindow)
                || focusedWindow != targetWindow && IsComposingOn(targetWindow);
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
