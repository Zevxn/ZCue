using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using ZCue.Infrastructure;

namespace ZCue.Services;

internal sealed class WindowSelectionService
{
    private WindowSelectionTarget? _cachedTarget;

    public WindowSelectionTarget? GetTargetAtCursor()
    {
        return NativeMethods.GetCursorPos(out var point) ? GetTargetAtPoint(point) : null;
    }

    internal WindowSelectionTarget? GetTargetAtPoint(NativeMethods.Point point)
    {
        var window = NativeMethods.GetAncestor(NativeMethods.WindowFromPoint(point), NativeMethods.GA_ROOT);
        if (window == IntPtr.Zero || !NativeMethods.IsWindowVisible(window)
            || !NativeMethods.GetWindowRect(window, out var bounds)
            || bounds.Right <= bounds.Left || bounds.Bottom <= bounds.Top) return null;

        // DWM 可见边界不包含窗口外的透明缩放边距。
        if (NativeMethods.DwmGetWindowAttribute(window, 9, out var frameBounds,
                (uint)Marshal.SizeOf<NativeMethods.Rect>()) == 0
            && frameBounds.Right > frameBounds.Left && frameBounds.Bottom > frameBounds.Top)
            bounds = frameBounds;

        NativeMethods.GetWindowThreadProcessId(window, out var processId);
        if (processId == 0 || processId == Environment.ProcessId) return null;

        var className = new StringBuilder(256);
        NativeMethods.GetClassName(window, className, className.Capacity);
        if (className.ToString() is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
            return null;

        if (_cachedTarget is { } cached && cached.Window == window && cached.ProcessId == processId)
            return _cachedTarget = cached with { Bounds = bounds };

        try
        {
            using var process = Process.GetProcessById(unchecked((int)processId));
            var name = process.ProcessName;
            if (!name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name += ".exe";
            string? path = null;
            try
            {
                path = process.MainModule?.FileName;
            }
            catch
            {
                // 受限进程仍可按名称添加，图标使用默认值。
            }

            return _cachedTarget = new(window, processId, name, path, bounds);
        }
        catch
        {
            return null;
        }
    }
}

internal sealed record WindowSelectionTarget(
    IntPtr Window, uint ProcessId, string ProcessName, string? ExecutablePath, NativeMethods.Rect Bounds);
