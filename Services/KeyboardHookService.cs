using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using TypeSense.Infrastructure;

namespace TypeSense.Services;

public enum KeyboardHookDecision
{
    Pass,
    Block
}

public sealed class KeyboardInputEventArgs
{
    public required int VirtualKeyCode { get; init; }

    public required uint ScanCode { get; init; }

    public required uint Flags { get; init; }

    public required bool IsInjected { get; init; }

    public required string? Text { get; init; }

    public required bool IsCtrlDown { get; init; }

    public required bool IsAltDown { get; init; }

    public required bool IsShiftDown { get; init; }

    public required bool IsWindowsKeyDown { get; init; }

    public bool HasSystemModifier => IsCtrlDown || IsAltDown || IsWindowsKeyDown;
}

public sealed class KeyboardHookService : IDisposable
{
    private readonly object _gate = new();
    private readonly NativeMethods.HookProc _hookProc;
    private readonly NativeMethods.MouseHookProc _mouseHookProc;
    private readonly ManualResetEventSlim _ready = new(false);
    private Thread? _hookThread;
    private uint _hookThreadId;
    private IntPtr _hookHandle;
    private IntPtr _mouseHookHandle;
    private Exception? _startupException;
    private bool _disposed;

    public KeyboardHookService()
    {
        _hookProc = HookCallback;
        _mouseHookProc = MouseHookCallback;
    }

    public event Func<KeyboardInputEventArgs, KeyboardHookDecision>? KeyDown;

    public event Action<int, int>? MouseButtonDown;

    public bool IsRunning
    {
        get
        {
            lock (_gate)
            {
                return _hookHandle != IntPtr.Zero;
            }
        }
    }

    public void Start()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_hookThread is not null)
            {
                return;
            }

            _ready.Reset();
            _startupException = null;
            _hookThread = new Thread(HookThreadMain)
            {
                IsBackground = true,
                Name = "TypeSense.KeyboardHook"
            };
            _hookThread.Start();
        }

        if (!_ready.Wait(TimeSpan.FromSeconds(5)))
        {
            throw new TimeoutException("等待全局键盘 Hook 启动超时。");
        }

        if (_startupException is not null)
        {
            throw new InvalidOperationException("全局键盘 Hook 启动失败。", _startupException);
        }
    }

    public void Stop()
    {
        Thread? thread;
        uint threadId;

        lock (_gate)
        {
            thread = _hookThread;
            threadId = _hookThreadId;
        }

        if (thread is null)
        {
            return;
        }

        if (threadId != 0)
        {
            NativeMethods.PostThreadMessage(threadId, NativeMethods.WM_QUIT, UIntPtr.Zero, IntPtr.Zero);
        }

        if (Thread.CurrentThread != thread)
        {
            thread.Join(TimeSpan.FromSeconds(2));
        }

        lock (_gate)
        {
            _hookThread = null;
            _hookThreadId = 0;
            _hookHandle = IntPtr.Zero;
            _mouseHookHandle = IntPtr.Zero;
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Stop();
        _ready.Dispose();
    }

    // SECTION Hook 线程与回调

    private void HookThreadMain()
    {
        try
        {
            _hookThreadId = NativeMethods.GetCurrentThreadId();
            var moduleHandle = NativeMethods.GetModuleHandle(null);
            var hookHandle = NativeMethods.SetWindowsHookEx(
                NativeMethods.WH_KEYBOARD_LL,
                _hookProc,
                moduleHandle,
                0);

            if (hookHandle == IntPtr.Zero)
            {
                _startupException = new Win32Exception(Marshal.GetLastWin32Error());
                _ready.Set();
                return;
            }

            _hookHandle = hookHandle;
            var mouseHookHandle = NativeMethods.SetWindowsHookEx(
                NativeMethods.WH_MOUSE_LL,
                _mouseHookProc,
                moduleHandle,
                0);
            if (mouseHookHandle == IntPtr.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            _mouseHookHandle = mouseHookHandle;
            // 确保线程消息队列已创建，Stop() 才能可靠投递 WM_QUIT。
            NativeMethods.PeekMessage(out _, IntPtr.Zero, 0, 0, 0);
            _ready.Set();

            while (true)
            {
                var result = NativeMethods.GetMessage(out var message, IntPtr.Zero, 0, 0);
                if (result <= 0)
                {
                    break;
                }

                NativeMethods.TranslateMessage(ref message);
                NativeMethods.DispatchMessage(ref message);
            }
        }
        catch (Exception exception)
        {
            _startupException = exception;
            _ready.Set();
        }
        finally
        {
            if (_mouseHookHandle != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_mouseHookHandle);
                _mouseHookHandle = IntPtr.Zero;
            }

            if (_hookHandle != IntPtr.Zero)
            {
                NativeMethods.UnhookWindowsHookEx(_hookHandle);
                _hookHandle = IntPtr.Zero;
            }
        }
    }

    private IntPtr HookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && (wParam.ToInt32() == NativeMethods.WM_KEYDOWN || wParam.ToInt32() == NativeMethods.WM_SYSKEYDOWN))
        {
            try
            {
                var nativeData = Marshal.PtrToStructure<NativeMethods.KbdLlHookStruct>(lParam);
                var isInjected = (nativeData.Flags & (NativeMethods.LLKHF_INJECTED | NativeMethods.LLKHF_LOWER_IL_INJECTED)) != 0;
                var keyEvent = new KeyboardInputEventArgs
                {
                    VirtualKeyCode = unchecked((int)nativeData.VkCode),
                    ScanCode = nativeData.ScanCode,
                    Flags = nativeData.Flags,
                    IsInjected = isInjected,
                    Text = isInjected ? null : TryGetText(nativeData.VkCode, nativeData.ScanCode),
                    IsCtrlDown = IsKeyDown(NativeMethods.VK_CONTROL) || IsKeyDown(0xA2) || IsKeyDown(0xA3),
                    IsAltDown = IsKeyDown(NativeMethods.VK_MENU) || IsKeyDown(0xA4) || IsKeyDown(0xA5),
                    IsShiftDown = IsKeyDown(NativeMethods.VK_SHIFT) || IsKeyDown(0xA0) || IsKeyDown(0xA1),
                    IsWindowsKeyDown = IsKeyDown(NativeMethods.VK_LWIN) || IsKeyDown(NativeMethods.VK_RWIN)
                };

                var decision = KeyDown?.Invoke(keyEvent) ?? KeyboardHookDecision.Pass;
                if (decision == KeyboardHookDecision.Block)
                {
                    return (IntPtr)1;
                }
            }
            catch
            {
                // Hook 线程不能因单个事件异常而中断。当前按键放行给目标应用。
            }
        }

        return NativeMethods.CallNextHookEx(_hookHandle, code, wParam, lParam);
    }

    private IntPtr MouseHookCallback(int code, IntPtr wParam, IntPtr lParam)
    {
        if (code >= 0 && IsMouseButtonDown(wParam.ToInt32()))
        {
            try
            {
                var mouseData = Marshal.PtrToStructure<NativeMethods.MsllHookStruct>(lParam);
                MouseButtonDown?.Invoke(mouseData.Point.X, mouseData.Point.Y);
            }
            catch
            {
                // 全局鼠标 Hook 只观察点击；异常不能阻断目标应用的鼠标输入。
            }
        }

        return NativeMethods.CallNextHookEx(_mouseHookHandle, code, wParam, lParam);
    }

    private static bool IsMouseButtonDown(int message)
    {
        return message is NativeMethods.WM_LBUTTONDOWN
            or NativeMethods.WM_RBUTTONDOWN
            or NativeMethods.WM_MBUTTONDOWN
            or NativeMethods.WM_XBUTTONDOWN;
    }

    // !SECTION Hook 线程与回调

    // SECTION 键盘字符转换

    private static bool IsKeyDown(int virtualKeyCode)
    {
        return (NativeMethods.GetAsyncKeyState(virtualKeyCode) & 0x8000) != 0;
    }

    private static string? TryGetText(uint virtualKeyCode, uint scanCode)
    {
        if (virtualKeyCode is NativeMethods.VK_BACK or NativeMethods.VK_TAB or NativeMethods.VK_RETURN
            or NativeMethods.VK_ESCAPE or NativeMethods.VK_DELETE)
        {
            return null;
        }

        if (IsKeyDown(NativeMethods.VK_CONTROL) || IsKeyDown(NativeMethods.VK_MENU)
            || IsKeyDown(NativeMethods.VK_LWIN) || IsKeyDown(NativeMethods.VK_RWIN))
        {
            return null;
        }

        var keyboardState = new byte[256];
        if (!NativeMethods.GetKeyboardState(keyboardState))
        {
            return null;
        }

        keyboardState[virtualKeyCode] |= 0x80;
        var foregroundWindow = NativeMethods.GetForegroundWindow();
        var threadId = foregroundWindow == IntPtr.Zero
            ? 0u
            : NativeMethods.GetWindowThreadProcessId(foregroundWindow, out _);
        var keyboardLayout = NativeMethods.GetKeyboardLayout(threadId);
        var characters = new StringBuilder(8);
        var result = NativeMethods.ToUnicodeEx(
            virtualKeyCode,
            scanCode,
            keyboardState,
            characters,
            characters.Capacity,
            0,
            keyboardLayout);

        if (result < 0)
        {
            // 清理死键状态，避免下一次普通字符被错误拼接。
            NativeMethods.ToUnicodeEx(
                virtualKeyCode,
                scanCode,
                keyboardState,
                new StringBuilder(8),
                8,
                0,
                keyboardLayout);
            return null;
        }

        if (result <= 0)
        {
            return null;
        }

        var text = characters.ToString(0, result);
        return text.Any(character => !char.IsControl(character)) ? text : null;
    }

    // !SECTION 键盘字符转换
}
