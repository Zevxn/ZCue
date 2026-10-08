using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using ZCue.Infrastructure;
using ZCue.Models;
using ZCue.Views;

namespace ZCue.Services;

public sealed class AppController : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private readonly DispatcherTimer _foregroundMonitor;
    private readonly InputBufferService _inputBuffer = new();
    private readonly PromptCatalogService _catalog;
    private readonly AppSettingsService _settings = new();
    private readonly UpdateService _updateService = new();
    private readonly System.Threading.CancellationTokenSource _lifetimeCancellation = new();
    private readonly ApplicationFilterService _applicationFilter;
    private readonly StartupService _startupService = new();
    private readonly PromptMatchService _matchService = new();
    private readonly PromptVariableService _promptVariableService = new();
    private readonly CaretPositionService _caretPositionService = new();
    private readonly FocusedTextService _focusedTextService = new();
    private readonly ImeCompositionService _imeCompositionService = new();
    private readonly TextInsertionService _textInsertionService = new();
    private readonly KeyboardHookService _keyboardHook = new();
    private readonly TrayIconService _trayIcon;
    private SuggestionWindow? _suggestionWindow;
    private readonly GhostPreview _ghostPreview;
    private readonly PromptManagerWindow _promptManagerWindow;
    private readonly IntPtr _clipboardOwnerWindow;
    private readonly object _stateGate = new();
    private const int RightAltVirtualKeyCode = 0xA5;

    private IReadOnlyList<PromptMatch> _activeMatches = Array.Empty<PromptMatch>();
    private IntPtr _targetWindow;
    private IntPtr _imeInputStateTarget;
    private int _selectedIndex;
    private long _stateVersion;
    private long _textSyncRequest;
    private bool _suggestionsVisible;
    private bool _paused;
    private bool _disposed;

    public AppController(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _catalog = new PromptCatalogService(new PromptStorageService(_settings));
        _applicationFilter = new ApplicationFilterService(_settings);
        AppThemeManager.Apply(_settings.Current.ThemeMode);
        _foregroundMonitor = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(60)
        };
        _foregroundMonitor.Tick += HandleForegroundMonitorTick;
        _ghostPreview = new GhostPreview();

        _trayIcon = new TrayIconService(
            _startupService,
            _applicationFilter,
            _settings.Current.ThemeMode);
        _promptManagerWindow = new PromptManagerWindow(
            _catalog,
            _settings,
            _applicationFilter,
            _startupService,
            () => !IsPaused(),
            enabled => SetPaused(!enabled),
            _trayIcon.UpdateStartupState,
            _updateService);
        // 候选窗会在隐藏时销毁；粘贴操作使用生命周期稳定的管理器句柄。
        _clipboardOwnerWindow = new WindowInteropHelper(_promptManagerWindow).EnsureHandle();
        _trayIcon.OpenManagerRequested += OpenPromptManager;
        _trayIcon.PauseRequested += () => SetPaused(true);
        _trayIcon.EnableRequested += () => SetPaused(false);
        _trayIcon.ExitRequested += ExitApplication;

        _keyboardHook.KeyDown += HandleKeyDown;
        _keyboardHook.KeyUp += HandleKeyUp;
        _keyboardHook.MouseButtonDown += HandleMouseButtonDown;
        _settings.Changed += HandleSettingsChanged;
        Microsoft.Win32.SystemEvents.UserPreferenceChanged += HandleSystemPreferenceChanged;
    }

    public void Start()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _keyboardHook.Start();

        if (_settings.Current.EnableAutomaticUpdateNotifications)
        {
            _ = CheckForUpdatesOnStartupAsync(_lifetimeCancellation.Token);
        }
    }

    public void SetPaused(bool paused)
    {
        lock (_stateGate)
        {
            _paused = paused;
        }

        _trayIcon.UpdatePaused(paused);
        ClearSuggestions(resetBuffer: true);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _lifetimeCancellation.Cancel();
        _settings.Changed -= HandleSettingsChanged;
        _applicationFilter.Dispose();
        _keyboardHook.KeyDown -= HandleKeyDown;
        _keyboardHook.KeyUp -= HandleKeyUp;
        _keyboardHook.MouseButtonDown -= HandleMouseButtonDown;
        Microsoft.Win32.SystemEvents.UserPreferenceChanged -= HandleSystemPreferenceChanged;
        _keyboardHook.Dispose();
        _foregroundMonitor.Stop();
        _foregroundMonitor.Tick -= HandleForegroundMonitorTick;
        CloseSuggestionWindow();
        _ghostPreview.Dispose();
        _promptManagerWindow.CloseWithoutHiding();
        _trayIcon.Dispose();
        _lifetimeCancellation.Dispose();
    }

    // SECTION 启动更新检查

    private async Task CheckForUpdatesOnStartupAsync(System.Threading.CancellationToken cancellationToken)
    {
        try
        {
            var update = await _updateService.CheckForUpdateAsync(cancellationToken);
            if (update is null
                || _disposed
                || cancellationToken.IsCancellationRequested
                || !_settings.Current.EnableAutomaticUpdateNotifications)
            {
                return;
            }

            _trayIcon.NotifyUpdateAvailable(update.TagName, update.ReleaseUri);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch
        {
            // 自动检查失败不影响启动；用户仍可在设置页手动检查。
        }
    }

    // !SECTION 启动更新检查

    // SECTION 全局键盘事件与输入状态

    private KeyboardHookDecision HandleKeyDown(KeyboardInputEventArgs input)
    {
        if (input.IsInjected || IsPaused())
        {
            return KeyboardHookDecision.Pass;
        }

        var foregroundWindow = NativeMethods.GetForegroundWindow();
        if (foregroundWindow == IntPtr.Zero)
        {
            return KeyboardHookDecision.Pass;
        }

        SwitchTargetWindowIfNeeded(foregroundWindow);
        if (!_applicationFilter.IsApplicationAllowed(foregroundWindow))
        {
            ClearSuggestions(resetBuffer: true);
            return KeyboardHookDecision.Pass;
        }

        if (input.VirtualKeyCode == RightAltVirtualKeyCode)
        {
            ClearSuggestions(resetBuffer: false);
            return KeyboardHookDecision.Pass;
        }

        if (input.HasSystemModifier)
        {
            ClearSuggestions(resetBuffer: true);
            if (IsTextChangingSystemShortcut(input))
            {
                ScheduleFocusedTextSync(foregroundWindow);
            }

            return KeyboardHookDecision.Pass;
        }

        if (TryGetPinyinInput(input.VirtualKeyCode, out _)
            || input.VirtualKeyCode is NativeMethods.VK_BACK or NativeMethods.VK_DELETE)
        {
            _imeCompositionService.BeginTextInput(foregroundWindow);
        }

        var imeInputActive = ObserveImeInputState(foregroundWindow);
        if (imeInputActive && !_settings.Current.ShowSuggestionsDuringImeComposition)
        {
            ClearSuggestions(resetBuffer: false, cancelTextSync: false);
        }
        if (IsShiftKey(input.VirtualKeyCode))
        {
            return KeyboardHookDecision.Pass;
        }

        if (HasSuggestions())
        {
            // 候选显示时先处理导航和确认，避免被输入法分支提前放行。
            if (input.VirtualKeyCode == NativeMethods.VK_UP)
            {
                MoveSelection(-1);
                return KeyboardHookDecision.Block;
            }

            if (input.VirtualKeyCode == NativeMethods.VK_DOWN)
            {
                MoveSelection(1);
                return KeyboardHookDecision.Block;
            }

            if (input.VirtualKeyCode == NativeMethods.VK_TAB && !input.IsShiftDown)
            {
                ConfirmSelection(GetSelectedIndex());
                return KeyboardHookDecision.Block;
            }
        }

        if (imeInputActive)
        {
            if (input.VirtualKeyCode == NativeMethods.VK_ESCAPE)
            {
                ClearSuggestions(resetBuffer: true);
                CompleteImeInputState(foregroundWindow);
                return KeyboardHookDecision.Pass;
            }

            if (input.VirtualKeyCode == NativeMethods.VK_BACK)
            {
                _inputBuffer.Backspace();
            }
            else if (TryGetPinyinInput(input.VirtualKeyCode, out var pinyinInput))
            {
                _inputBuffer.Append(pinyinInput);
            }

            var allowImeCommitSync = input.VirtualKeyCode is NativeMethods.VK_RETURN or NativeMethods.VK_SPACE
                || !input.IsShiftDown && input.VirtualKeyCode is >= 0x31 and <= 0x39
                || input.VirtualKeyCode is (>= 0xBA and <= 0xC0) or (>= 0xDB and <= 0xDE);
            ScheduleFocusedTextSync(foregroundWindow, allowImeCommitSync: allowImeCommitSync);
            return KeyboardHookDecision.Pass;
        }

        if (input.VirtualKeyCode == NativeMethods.VK_ESCAPE)
        {
            if (HasSuggestions())
            {
                ClearSuggestions(resetBuffer: true);
                return KeyboardHookDecision.Block;
            }

            ClearSuggestions(resetBuffer: true);
            ScheduleFocusedTextSync(foregroundWindow);
            return KeyboardHookDecision.Pass;
        }

        if (HasSuggestions())
        {
            if (input.VirtualKeyCode == NativeMethods.VK_RETURN
                && _settings.Current.EnableEnterConfirmation)
            {
                ConfirmSelection(GetSelectedIndex());
                return KeyboardHookDecision.Block;
            }

            if (_settings.Current.EnableNumberSelection
                && !input.IsShiftDown
                && input.VirtualKeyCode is >= 0x31 and <= 0x39)
            {
                var numberIndex = input.VirtualKeyCode - 0x31;
                if (IsValidMatchIndex(numberIndex))
                {
                    ConfirmSelection(numberIndex);
                    return KeyboardHookDecision.Block;
                }
            }
        }

        if (input.VirtualKeyCode == NativeMethods.VK_BACK)
        {
            _inputBuffer.Backspace();
            ClearSuggestions(resetBuffer: false, cancelTextSync: false);
            ScheduleFocusedTextSync(foregroundWindow);
            return KeyboardHookDecision.Pass;
        }

        if (input.VirtualKeyCode == NativeMethods.VK_DELETE
            || input.VirtualKeyCode is NativeMethods.VK_HOME or NativeMethods.VK_END
            || input.VirtualKeyCode is NativeMethods.VK_LEFT or NativeMethods.VK_RIGHT
            || input.VirtualKeyCode is NativeMethods.VK_PRIOR or NativeMethods.VK_NEXT)
        {
            ClearSuggestions(resetBuffer: true);
            ScheduleFocusedTextSync(foregroundWindow);
            return KeyboardHookDecision.Pass;
        }

        if (input.VirtualKeyCode is NativeMethods.VK_RETURN or NativeMethods.VK_TAB or NativeMethods.VK_SPACE)
        {
            ClearSuggestions(resetBuffer: true);
            ScheduleFocusedTextSync(foregroundWindow);
            return KeyboardHookDecision.Pass;
        }

        var inputText = input.Text;
        if (string.IsNullOrEmpty(inputText)
            && TryGetPinyinInput(input.VirtualKeyCode, out var fallbackInput))
        {
            inputText = fallbackInput;
        }

        if (!string.IsNullOrEmpty(inputText))
        {
            if (inputText.Any(char.IsWhiteSpace))
            {
                ClearSuggestions(resetBuffer: true);
            }
            else
            {
                if (HasSuggestions())
                {
                    ClearSuggestions(resetBuffer: false, cancelTextSync: false);
                }

                _inputBuffer.Append(inputText);
            }

            ScheduleFocusedTextSync(foregroundWindow);
        }
        else
        {
            // IME 提交、组合键和第三方控件可能没有可由 ToUnicodeEx 返回的字符。
            ScheduleFocusedTextSync(foregroundWindow);
        }

        return KeyboardHookDecision.Pass;
    }

    private void HandleKeyUp(int virtualKeyCode)
    {
        var isRightAltKey = virtualKeyCode == RightAltVirtualKeyCode;
        var isShiftKey = IsShiftKey(virtualKeyCode);
        var isImeCommitKey = virtualKeyCode is NativeMethods.VK_SPACE or NativeMethods.VK_RETURN
            or (>= 0x31 and <= 0x39);
        if ((!isShiftKey && !isRightAltKey && !isImeCommitKey) || IsPaused())
        {
            return;
        }

        var foregroundWindow = NativeMethods.GetForegroundWindow();
        if (foregroundWindow == IntPtr.Zero)
        {
            return;
        }

        lock (_stateGate)
        {
            if (_targetWindow != foregroundWindow)
            {
                return;
            }
        }

        if (!_applicationFilter.IsApplicationAllowed(foregroundWindow))
        {
            ClearSuggestions(resetBuffer: true);
            return;
        }

        if (isImeCommitKey && !IsImeInputStateObserved(foregroundWindow))
        {
            return;
        }
        // 选词键按下时目标控件尚未处理提交，松开后再同步一次真实文本。
        ScheduleFocusedTextSync(
            foregroundWindow,
            preservePhysicalInputForShiftCommit: isShiftKey,
            waitForVoiceInputSettle: isRightAltKey,
            allowImeCommitSync: isShiftKey || isImeCommitKey);
    }

    private void SwitchTargetWindowIfNeeded(IntPtr foregroundWindow)
    {
        var changed = false;
        lock (_stateGate)
        {
            if (_targetWindow != IntPtr.Zero && _targetWindow != foregroundWindow)
            {
                changed = true;
                _imeInputStateTarget = IntPtr.Zero;
            }

            _targetWindow = foregroundWindow;
        }

        if (changed)
        {
            _inputBuffer.Reset();
            ClearSuggestions(resetBuffer: false);
        }
    }

    private bool IsImeInputStateObserved(IntPtr targetWindow)
    {
        lock (_stateGate)
        {
            return targetWindow != IntPtr.Zero && _imeInputStateTarget == targetWindow;
        }
    }

    private void CompleteImeInputState(IntPtr targetWindow)
    {
        _imeCompositionService.ClearTextEditComposition(targetWindow);
        lock (_stateGate)
        {
            if (_imeInputStateTarget == targetWindow)
            {
                _imeInputStateTarget = IntPtr.Zero;
            }
        }
    }

    private static bool IsShiftKey(int virtualKeyCode)
    {
        return virtualKeyCode is NativeMethods.VK_SHIFT
            or NativeMethods.VK_LSHIFT
            or NativeMethods.VK_RSHIFT;
    }

    private static bool TryGetPinyinInput(int virtualKeyCode, out string input)
    {
        if (virtualKeyCode is >= NativeMethods.VK_A and <= NativeMethods.VK_Z)
        {
            input = char.ToLowerInvariant((char)virtualKeyCode).ToString();
            return true;
        }

        input = string.Empty;
        return false;
    }

    private void RecomputeSuggestions(IntPtr targetWindow)
    {
        if (NativeMethods.GetForegroundWindow() != targetWindow
            || !_applicationFilter.IsApplicationAllowed(targetWindow))
        {
            ClearSuggestions(resetBuffer: true);
            return;
        }

        var token = _inputBuffer.GetCurrentToken();
        var enabledItems = _catalog.GetEnabledMatchSnapshot();
        var matches = _matchService.Match(
            token,
            enabledItems,
            settings: _settings.Current);
        long version;

        lock (_stateGate)
        {
            _activeMatches = matches;
            _selectedIndex = 0;
            _suggestionsVisible = matches.Count > 0;
            version = ++_stateVersion;
        }

        PostToUi(() =>
        {
            if (!IsCurrentVersion(version))
            {
                return;
            }

            if (NativeMethods.GetForegroundWindow() != targetWindow
                || !_applicationFilter.IsApplicationAllowed(targetWindow))
            {
                ClearSuggestions(resetBuffer: true);
                return;
            }

            if (HideSuggestionsIfImeInputActive(targetWindow))
            {
                return;
            }

            if (matches.Count == 0)
            {
                // 无匹配时关闭窗口，但必须先清空行内容再隐藏：
                // 否则分层窗口在隐藏瞬间会露出上一轮的候选行。
                _foregroundMonitor.Stop();
                CloseSuggestionWindow();
                _ghostPreview.HidePreview();
                return;
            }

            try
            {
                var currentIndex = GetSelectedIndex();
                var caretPosition = _caretPositionService.GetPosition(targetWindow);
                // 光标定位是跨进程 UI Automation，耗时可能超过一次按键间隔。
                // 期间新按键会更新 _stateVersion，此处必须重新校验，
                // 否则会用上一轮的 matches 覆盖刚更新的候选。
                if (!IsCurrentVersion(version))
                {
                    return;
                }

                var previewContents = _promptVariableService.ResolveAll(
                    matches.Select(match => match.Item.Content).ToArray());
                // 定位和变量解析期间输入法仍可开始预编辑，显示前必须再次检查。
                if (!IsCurrentVersion(version)
                    || HideSuggestionsIfImeInputActive(targetWindow))
                {
                    return;
                }

                GetOrCreateSuggestionWindow().ShowSuggestions(
                    matches,
                    currentIndex,
                    caretPosition,
                    _settings.Current.ShowContentPreview,
                    previewContents);
                ShowGhostPreview(previewContents[currentIndex], targetWindow, caretPosition);
                _foregroundMonitor.Start();
            }
            catch
            {
                CloseSuggestionWindow();
                _ghostPreview.HidePreview();
            }
        });
    }

    // !SECTION 全局键盘事件与输入状态

    // SECTION 候选状态与确认

    private SuggestionWindow GetOrCreateSuggestionWindow()
    {
        if (_suggestionWindow is not null)
        {
            return _suggestionWindow;
        }

        var window = new SuggestionWindow();
        window.SetThemeMode(_settings.Current.ThemeMode);
        window.SuggestionBoxWidth = _settings.Current.SuggestionBoxWidth;
        window.SelectionRequested += HandleMouseSelection;
        _suggestionWindow = window;
        return window;
    }

    private void CloseSuggestionWindow()
    {
        var window = _suggestionWindow;
        _suggestionWindow = null;
        if (window is null)
        {
            return;
        }

        window.SelectionRequested -= HandleMouseSelection;
        window.Close();
    }

    private void MoveSelection(int direction)
    {
        int selectedIndex;
        IReadOnlyList<PromptMatch> matches;
        IntPtr targetWindow;
        long version;
        lock (_stateGate)
        {
            if (!_suggestionsVisible || _activeMatches.Count == 0)
            {
                return;
            }

            _selectedIndex = (_selectedIndex + direction + _activeMatches.Count) % _activeMatches.Count;
            selectedIndex = _selectedIndex;
            matches = _activeMatches;
            targetWindow = _targetWindow;
            version = ++_stateVersion;
        }

        PostToUi(() =>
        {
            if (!IsCurrentVersion(version))
            {
                return;
            }

            if (NativeMethods.GetForegroundWindow() != targetWindow
                || !_applicationFilter.IsApplicationAllowed(targetWindow))
            {
                ClearSuggestions(resetBuffer: true);
                return;
            }

            if (HideSuggestionsIfImeInputActive(targetWindow))
            {
                return;
            }

            if (HasSuggestions())
            {
                try
                {
                    var caretPosition = _caretPositionService.GetPosition(targetWindow);
                    string previewContent;
                    if (_suggestionWindow is { } window)
                    {
                        window.UpdateSelection(selectedIndex);
                        previewContent = _promptVariableService.Resolve(matches[selectedIndex].Item.Content);
                    }
                    else
                    {
                        // 首次显示前已按下方向键时，也要用当前选择创建完整的新候选窗。
                        var previewContents = _promptVariableService.ResolveAll(
                            matches.Select(match => match.Item.Content).ToArray());
                        GetOrCreateSuggestionWindow().ShowSuggestions(matches, selectedIndex,
                            caretPosition, _settings.Current.ShowContentPreview, previewContents);
                        _foregroundMonitor.Start();
                        previewContent = previewContents[selectedIndex];
                    }
                    ShowGhostPreview(previewContent, targetWindow, caretPosition);
                }
                catch
                {
                    CloseSuggestionWindow();
                    _ghostPreview.HidePreview();
                }
            }
        });
    }

    private void ConfirmSelection(int index)
    {
        PromptMatch? match;
        IntPtr targetWindow;

        lock (_stateGate)
        {
            if (!_suggestionsVisible || index < 0 || index >= _activeMatches.Count)
            {
                return;
            }

            match = _activeMatches[index];
            targetWindow = _targetWindow;
            _suggestionsVisible = false;
            _activeMatches = Array.Empty<PromptMatch>();
            _selectedIndex = 0;
            _stateVersion++;
        }

        CancelFocusedTextSync();
        _inputBuffer.Reset();
        var selectedMatch = match!;

        PostAsyncToUi(async () =>
        {
            CloseSuggestionWindow();
            _ghostPreview.HidePreview();
            if (NativeMethods.GetForegroundWindow() != targetWindow)
            {
                return;
            }

            var content = _promptVariableService.Resolve(selectedMatch.Item.Content);
            CompleteImeInputState(targetWindow);
            var inserted = await _textInsertionService.ReplaceAsync(
                targetWindow,
                selectedMatch.MatchLength,
                content,
                _clipboardOwnerWindow);
            if (inserted)
            {
                try
                {
                    // 使用计数的磁盘保存不应阻塞正文插入和候选键响应。
                    await Task.Run(() => _catalog.IncrementUsage(selectedMatch.Item.Id)).ConfigureAwait(true);
                }
                catch (Exception exception)
                {
                    System.Diagnostics.Trace.TraceWarning("ZCue 使用计数保存失败：{0}", exception.GetType().Name);
                }
            }
        });
    }

    private void HandleMouseSelection(int index)
    {
        ConfirmSelection(index);
    }

    private void HandleMouseButtonDown(int x, int y)
    {
        _ = _applicationFilter.GetForegroundProcessName();
        if (HasSuggestions() && _suggestionWindow?.ContainsScreenPoint(x, y) != true)
        {
            ClearSuggestions(resetBuffer: true);
        }

        IntPtr targetWindow;
        lock (_stateGate)
        {
            targetWindow = _targetWindow;
        }

        if (IsImeInputStateObserved(targetWindow))
        {
            ScheduleFocusedTextSync(targetWindow, allowImeCommitSync: true);
        }
    }

    private void ClearSuggestions(bool resetBuffer, bool cancelTextSync = true)
    {
        if (cancelTextSync)
        {
            CancelFocusedTextSync();
        }

        if (resetBuffer)
        {
            _inputBuffer.Reset();
        }

        lock (_stateGate)
        {
            _activeMatches = Array.Empty<PromptMatch>();
            _selectedIndex = 0;
            _suggestionsVisible = false;
            _stateVersion++;
        }

        PostToUi(() =>
        {
            _foregroundMonitor.Stop();
            CloseSuggestionWindow();
            _ghostPreview.HidePreview();
        });
    }

    private bool HasSuggestions()
    {
        lock (_stateGate)
        {
            return _suggestionsVisible && _activeMatches.Count > 0;
        }
    }

    /// <summary>
    /// 输入法组合或选词期间压住候选显示。这里只隐藏窗口、不结束输入法状态：
    /// 组合结束后还要用光标前的真实文本同步缓冲，不能在组合期间丢掉输入状态。
    /// UIA TextEditPattern、IMM32 和候选窗共同检测状态；设置允许时仍可显示候选。
    /// </summary>
    private bool HideSuggestionsIfImeInputActive(IntPtr targetWindow)
    {
        if (!ObserveImeInputState(targetWindow)
            || _settings.Current.ShowSuggestionsDuringImeComposition)
        {
            return false;
        }

        ClearSuggestions(resetBuffer: false, cancelTextSync: false);
        return true;
    }

    private bool ObserveImeInputState(IntPtr targetWindow)
    {
        if (targetWindow == IntPtr.Zero)
        {
            return false;
        }

        _imeCompositionService.RefreshTextEditComposition(targetWindow);

        var isImeInputActive = _imeCompositionService.IsComposing(targetWindow)
            || _imeCompositionService.HasCandidateList(targetWindow)
            || _imeCompositionService.HasVisibleCandidateWindow();
        if (!isImeInputActive)
        {
            return false;
        }

        lock (_stateGate)
        {
            _imeInputStateTarget = targetWindow;
        }

        return true;
    }

    private bool IsValidMatchIndex(int index)
    {
        lock (_stateGate)
        {
            return _suggestionsVisible && index >= 0 && index < _activeMatches.Count;
        }
    }

    private int GetSelectedIndex()
    {
        lock (_stateGate)
        {
            return _selectedIndex;
        }
    }

    private bool IsCurrentVersion(long version)
    {
        lock (_stateGate)
        {
            return _stateVersion == version;
        }
    }

    private void HandleForegroundMonitorTick(object? sender, EventArgs e)
    {
        IntPtr targetWindow;
        lock (_stateGate)
        {
            if (!_suggestionsVisible)
            {
                _foregroundMonitor.Stop();
                return;
            }

            targetWindow = _targetWindow;
        }

        var foregroundWindow = NativeMethods.GetForegroundWindow();
        if (targetWindow == IntPtr.Zero
            || foregroundWindow != targetWindow
            || !_applicationFilter.IsApplicationAllowed(foregroundWindow))
        {
            ClearSuggestions(resetBuffer: true);
            return;
        }

        HideSuggestionsIfImeInputActive(targetWindow);
    }

    // !SECTION 候选状态与确认

    // SECTION 托盘、UI 调度与生命周期

    private bool IsTextChangingSystemShortcut(KeyboardInputEventArgs input)
    {
        return input.IsCtrlDown
            && (input.VirtualKeyCode is NativeMethods.VK_V or NativeMethods.VK_X or NativeMethods.VK_Z);
    }

    private void ScheduleFocusedTextSync(
        IntPtr targetWindow,
        bool preservePhysicalInputForShiftCommit = false,
        bool waitForVoiceInputSettle = false,
        bool allowImeCommitSync = false)
    {
        var request = Interlocked.Increment(ref _textSyncRequest);
        PostAsyncToUi(async () =>
        {
            await Task.Delay(35).ConfigureAwait(true);
            if (request != Volatile.Read(ref _textSyncRequest)
                || NativeMethods.GetForegroundWindow() != targetWindow)
            {
                return;
            }

            var observedComposition = false;
            var compositionSuggestionsUpdated = false;
            var compositionDeadline = Environment.TickCount64 + 2000;
            while (true)
            {
                await _imeCompositionService.RefreshTextEditCompositionAsync(targetWindow).ConfigureAwait(true);
                if (request != Volatile.Read(ref _textSyncRequest)
                    || NativeMethods.GetForegroundWindow() != targetWindow)
                {
                    return;
                }

                if (allowImeCommitSync
                    && !_imeCompositionService.HasNativeComposition(targetWindow)
                    && !_imeCompositionService.HasCandidateList(targetWindow)
                    && !_imeCompositionService.HasVisibleCandidateWindow())
                {
                    // 已收到选词提交键且原生选词信号结束，忽略 Chromium 残留的 UIA 范围。
                    _imeCompositionService.CompleteTextEditComposition(targetWindow);
                }

                // 提交键之后候选列表可能滞留；等待活动组合结束，再读取真实上屏文本。
                // 非提交按键仍保留候选窗信号，覆盖只有 TSF 选词窗的输入法。
                if (_imeCompositionService.IsComposing(targetWindow)
                    || !allowImeCommitSync && ObserveImeInputState(targetWindow))
                {
                    observedComposition = true;
                    if (!compositionSuggestionsUpdated)
                    {
                        if (_settings.Current.ShowSuggestionsDuringImeComposition)
                        {
                            RecomputeSuggestions(targetWindow);
                        }
                        else
                        {
                            ClearSuggestions(resetBuffer: false, cancelTextSync: false);
                        }

                        compositionSuggestionsUpdated = true;
                    }

                    if (Environment.TickCount64 >= compositionDeadline)
                    {
                        return;
                    }

                    await Task.Delay(25).ConfigureAwait(true);
                    if (request != Volatile.Read(ref _textSyncRequest)
                        || NativeMethods.GetForegroundWindow() != targetWindow)
                    {
                        return;
                    }

                    continue;
                }

                if (observedComposition || IsImeInputStateObserved(targetWindow))
                {
                    await Task.Delay(20).ConfigureAwait(true);
                    if (request != Volatile.Read(ref _textSyncRequest)
                        || NativeMethods.GetForegroundWindow() != targetWindow)
                    {
                        return;
                    }

                    if (_imeCompositionService.IsComposing(targetWindow)
                        || !allowImeCommitSync && ObserveImeInputState(targetWindow))
                    {
                        observedComposition = true;
                        continue;
                    }
                }

                break;
            }

            if (request != Volatile.Read(ref _textSyncRequest)
                || NativeMethods.GetForegroundWindow() != targetWindow)
            {
                return;
            }

            if (waitForVoiceInputSettle)
            {
                await WaitForVoiceInputSettleAsync(request, targetWindow).ConfigureAwait(true);
            }

            var imeInputObserved = observedComposition
                || IsImeInputStateObserved(targetWindow);
            if (!allowImeCommitSync && ObserveImeInputState(targetWindow))
            {
                HideSuggestionsIfImeInputActive(targetWindow);
                return;
            }

            var canReadText = _focusedTextService.TryGetTextBeforeCaret(targetWindow, out var textBeforeCaret);
            if (imeInputObserved && !preservePhysicalInputForShiftCommit)
            {
                // 组合结束和 UIA 光标/文本更新不同步；不能因第一次仍为空或旧拼音就结束同步。
                var textDeadline = Environment.TickCount64 + 250;
                while ((!canReadText || string.IsNullOrEmpty(textBeforeCaret)
                        || TextBeforeCaretMatchesCurrentToken(textBeforeCaret))
                    && Environment.TickCount64 < textDeadline)
                {
                    await Task.Delay(35).ConfigureAwait(true);
                    if (request != Volatile.Read(ref _textSyncRequest)
                        || NativeMethods.GetForegroundWindow() != targetWindow)
                    {
                        return;
                    }

                    canReadText = _focusedTextService.TryGetTextBeforeCaret(targetWindow, out textBeforeCaret);
                }
            }

            if (request != Volatile.Read(ref _textSyncRequest)
                || NativeMethods.GetForegroundWindow() != targetWindow)
            {
                return;
            }
            if (canReadText)
            {
                var matchesPhysicalInput = TextBeforeCaretMatchesCurrentToken(textBeforeCaret);
                var shouldReplaceBuffer = preservePhysicalInputForShiftCommit
                    ? matchesPhysicalInput
                    : matchesPhysicalInput || imeInputObserved || waitForVoiceInputSettle;
                if (shouldReplaceBuffer)
                {
                    // 输入法已提交时，物理缓冲是拼音，光标前真实文本才是中文触发串。
                    _inputBuffer.ReplaceFromTextBeforeCaret(textBeforeCaret);
                }
            }
            else if (imeInputObserved && !preservePhysicalInputForShiftCommit)
            {
                // 选词结果不能由拼音唯一确定；读不到已提交文本时，不再用旧拼音匹配或
                // 从提示词反推中文，避免 bb 上屏为“宝宝”后误触发“发布备注”。
                ClearSuggestions(resetBuffer: true);
                CompleteImeInputState(targetWindow);
                return;
            }

            if (imeInputObserved)
            {
                CompleteImeInputState(targetWindow);
            }

            RecomputeSuggestions(targetWindow);
        });
    }

    private async Task WaitForVoiceInputSettleAsync(long request, IntPtr targetWindow)
    {
        // 豆包松开右 Alt 后仍可能继续修正识别结果，先给语音服务留出处理时间。
        await Task.Delay(200).ConfigureAwait(true);

        var deadline = Environment.TickCount64 + 3000;
        var stableSince = Environment.TickCount64;
        string? previousText = null;
        while (Environment.TickCount64 < deadline)
        {
            if (request != Volatile.Read(ref _textSyncRequest)
                || NativeMethods.GetForegroundWindow() != targetWindow)
            {
                return;
            }

            if (_focusedTextService.TryGetTextBeforeCaret(targetWindow, out var textBeforeCaret))
            {
                if (previousText is not null
                    && !string.Equals(previousText, textBeforeCaret, StringComparison.Ordinal))
                {
                    stableSince = Environment.TickCount64;
                }

                previousText = textBeforeCaret;
                if (Environment.TickCount64 - stableSince >= 300)
                {
                    return;
                }
            }

            await Task.Delay(30).ConfigureAwait(true);
        }
    }

    /// <summary>
    /// 判断 UI Automation 读到的光标前文本是否值得用来替换物理按键缓冲。
    /// 只有文本尾部确实以当前 token 结尾时才允许替换：UIA 读取可能滞后于物理按键，
    /// 若放任它覆盖，会把上一次输入遗留的字符带进缓冲区，重算出上一轮的候选。
    /// token 为空时没有可校验的依据，保持物理缓冲。
    /// </summary>
    private bool TextBeforeCaretMatchesCurrentToken(string textBeforeCaret)
    {
        var token = _inputBuffer.GetCurrentToken();
        return token.Length > 0
            && textBeforeCaret.EndsWith(token, StringComparison.OrdinalIgnoreCase);
    }

    private void CancelFocusedTextSync()
    {
        Interlocked.Increment(ref _textSyncRequest);
    }

    private bool IsPaused()
    {
        lock (_stateGate)
        {
            return _paused;
        }
    }

    public void ActivateManager()
    {
        if (_disposed)
        {
            return;
        }

        OpenPromptManager();
    }

    private void OpenPromptManager()
    {
        _ = _applicationFilter.GetForegroundProcessName();
        PostToUi(() =>
        {
            if (_promptManagerWindow.WindowState == WindowState.Minimized)
            {
                _promptManagerWindow.WindowState = WindowState.Normal;
            }

            _promptManagerWindow.Show();
            _promptManagerWindow.Activate();
        });
    }

    private void HandleSettingsChanged(AppSettings settings)
    {
        if (!_applicationFilter.IsApplicationAllowed(NativeMethods.GetForegroundWindow()))
        {
            ClearSuggestions(resetBuffer: true);
        }

        PostToUi(() =>
        {
            AppThemeManager.Apply(settings.ThemeMode);
            _trayIcon.ApplyTheme(settings.ThemeMode);
            if (_suggestionWindow is { } window)
            {
                window.SetThemeMode(settings.ThemeMode);
                window.SuggestionBoxWidth = settings.SuggestionBoxWidth;
            }
            if (!settings.ShowContentPreview)
            {
                _ghostPreview.HidePreview();
            }
            else
            {
                RefreshGhostPreview();
            }
        });
    }

    private void HandleSystemPreferenceChanged(
        object? sender,
        Microsoft.Win32.UserPreferenceChangedEventArgs e)
    {
        if (e.Category != Microsoft.Win32.UserPreferenceCategory.Color
            && e.Category != Microsoft.Win32.UserPreferenceCategory.General)
        {
            return;
        }

        var themeMode = _settings.Current.ThemeMode;
        if (themeMode != AppThemeMode.System)
        {
            return;
        }

        PostToUi(() =>
        {
            AppThemeManager.Apply(themeMode);
            _trayIcon.ApplyTheme(themeMode);
            _suggestionWindow?.SetThemeMode(themeMode);
        });
    }

    private void RefreshGhostPreview()
    {
        IntPtr targetWindow;
        lock (_stateGate)
        {
            targetWindow = _targetWindow;
        }

        // 预览内容只能由 RecomputeSuggestions 写。这里直接调用 ShowGhostPreview 会绕过
        // 版本号校验：同一时刻尚未执行的旧重算任务恢复后仍会用上一轮内容覆盖窗口，
        // 表现为旧预览闪回。改为重新排队一次重算，旧任务随即被版本号丢弃。
        RecomputeSuggestions(targetWindow);
    }

    private void ShowGhostPreview(
        string content,
        IntPtr targetWindow,
        CaretPosition caretPosition)
    {
        if (!_settings.Current.ShowContentPreview
            || targetWindow == IntPtr.Zero
            || NativeMethods.GetForegroundWindow() != targetWindow)
        {
            _ghostPreview.HidePreview();
            return;
        }

        try
        {
            var hasControlBounds = _caretPositionService.TryGetTextControlBounds(
                targetWindow,
                out var controlBounds);
            _ghostPreview.ShowPreview(
                content,
                caretPosition,
                targetWindow,
                hasControlBounds ? controlBounds : null);
        }
        catch
        {
            _ghostPreview.HidePreview();
        }
    }

    private void ExitApplication()
    {
        PostToUi(() => System.Windows.Application.Current.Shutdown());
    }

    private void PostToUi(Action action)
    {
        if (_dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished)
        {
            return;
        }

        _dispatcher.BeginInvoke(DispatcherPriority.Input, action);
    }

    private void PostAsyncToUi(Func<Task> action)
    {
        if (_dispatcher.HasShutdownStarted || _dispatcher.HasShutdownFinished)
        {
            return;
        }

        _dispatcher.BeginInvoke(DispatcherPriority.Input, async () =>
        {
            try
            {
                await action();
            }
            catch
            {
                // 目标应用拒绝注入时，保持常驻程序和 Hook 继续工作。
            }
        });
    }

    // !SECTION 托盘、UI 调度与生命周期
}
