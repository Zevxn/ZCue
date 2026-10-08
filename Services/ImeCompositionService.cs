using ZCue.Infrastructure;
using System.Text;

namespace ZCue.Services;

public sealed class ImeCompositionService
{
    private const double OimeCandidateMinimumWidthDips = 200;
    [ThreadStatic]
    private static TextEditInterop.IAutomation? _textEditAutomation;
    private IntPtr _textEditCompositionTarget;
    private IntPtr _completedTextEditTarget;
    private readonly object _textEditGate = new();
    private Task _textEditRefresh = Task.CompletedTask;
    private long _textEditRefreshStarted;
    private long _textEditVersion;

    // SECTION UIA 组合状态

    public void BeginTextInput(IntPtr targetWindow)
    {
        lock (_textEditGate)
        {
            if (_completedTextEditTarget == targetWindow)
            {
                _completedTextEditTarget = IntPtr.Zero;
                _textEditCompositionTarget = IntPtr.Zero;
                _textEditVersion++;
            }
        }
    }

    public void CompleteTextEditComposition(IntPtr targetWindow)
    {
        lock (_textEditGate)
        {
            // Chromium 可能在提交后继续返回最后一次组合范围，直到下一次文本编辑。
            _completedTextEditTarget = targetWindow;
            _textEditCompositionTarget = IntPtr.Zero;
            _textEditVersion++;
        }
    }

    public void ClearTextEditComposition(IntPtr targetWindow)
    {
        lock (_textEditGate)
        {
            _textEditVersion++;
            if (_textEditCompositionTarget == targetWindow)
            {
                _textEditCompositionTarget = IntPtr.Zero;
            }
        }
    }

    /// <summary>只排队一个 MTA 查询；UI 和 Hook 均不得同步等待跨进程 COM。</summary>
    public void RefreshTextEditComposition(IntPtr targetWindow)
    {
        _ = RequestTextEditRefresh(targetWindow);
    }

    public async Task RefreshTextEditCompositionAsync(IntPtr targetWindow)
    {
        try
        {
            await RequestTextEditRefresh(targetWindow)
                .WaitAsync(TimeSpan.FromMilliseconds(80)).ConfigureAwait(false);
        }
        catch (TimeoutException)
        {
            // 无响应的 UIA provider 只占用一个后台查询，不能排队积压。
        }
    }

    private Task RequestTextEditRefresh(IntPtr targetWindow)
    {
        lock (_textEditGate)
        {
            if (_completedTextEditTarget == targetWindow)
            {
                return Task.CompletedTask;
            }

            if (!_textEditRefresh.IsCompleted
                || Environment.TickCount64 - _textEditRefreshStarted < 40)
            {
                return _textEditRefresh;
            }

            _textEditRefreshStarted = Environment.TickCount64;
            var version = _textEditVersion;
            _textEditRefresh = Task.Run(() =>
            {
                var active = TryReadTextEditComposition(targetWindow)
                    && NativeMethods.GetForegroundWindow() == targetWindow;
                lock (_textEditGate)
                {
                    if (version != _textEditVersion)
                    {
                        return;
                    }

                    Interlocked.Exchange(ref _textEditCompositionTarget, active ? targetWindow : IntPtr.Zero);
                }
            });
            return _textEditRefresh;
        }
    }

    private static bool TryReadTextEditComposition(IntPtr targetWindow)
    {
        var text = string.Empty;
        try
        {
            if (targetWindow != IntPtr.Zero
                && NativeMethods.GetForegroundWindow() == targetWindow)
            {
                _textEditAutomation ??= TextEditInterop.CreateClient();
                var focusedElement = _textEditAutomation.GetFocusedElement();
                NativeMethods.GetWindowThreadProcessId(targetWindow, out var processId);
                if (focusedElement is not null
                    && focusedElement.GetCurrentPropertyValue(TextEditInterop.ProcessIdPropertyId) is int focusedProcessId
                    && focusedProcessId == processId
                    && focusedElement.GetCurrentPattern(TextEditInterop.TextEditPatternId)
                        is TextEditInterop.ITextEditPattern textEdit)
                {
                    var range = textEdit.GetActiveComposition() as TextEditInterop.ITextRange;
                    text = range?.GetText(256) ?? string.Empty;
                    if (text.Length == 0)
                    {
                        range = textEdit.GetConversionTarget() as TextEditInterop.ITextRange;
                        text = range?.GetText(256) ?? string.Empty;
                    }
                }
            }
        }
        catch
        {
            // 不支持 TextEditPattern 的控件继续使用 IMM32 和候选窗信号。
        }

        return text.Length > 0;
    }

    // !SECTION UIA 组合状态

    // SECTION 输入法候选与组合检测

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

        if (Interlocked.CompareExchange(ref _textEditCompositionTarget, IntPtr.Zero, IntPtr.Zero) == targetWindow)
        {
            return true;
        }

        return HasNativeComposition(targetWindow);
    }

    public bool HasNativeComposition(IntPtr targetWindow)
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

    // !SECTION 输入法候选与组合检测
}
