using System.Windows;
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
    private readonly PromptCatalogService _catalog = new();
    private readonly AppSettingsService _settings = new();
    private readonly UpdateService _updateService = new();
    private readonly System.Threading.CancellationTokenSource _lifetimeCancellation = new();
    private readonly ApplicationFilterService _applicationFilter;
    private readonly StartupService _startupService = new();
    private readonly PromptMatchService _matchService = new();
    private readonly CaretPositionService _caretPositionService = new();
    private readonly FocusedTextService _focusedTextService = new();
    private readonly ImeCompositionService _imeCompositionService = new();
    private readonly TextInsertionService _textInsertionService = new();
    private readonly KeyboardHookService _keyboardHook = new();
    private readonly TrayIconService _trayIcon;
    private readonly SuggestionWindow _suggestionWindow;
    private readonly GhostPreviewWindow _ghostPreviewWindow;
    private readonly PromptManagerWindow _promptManagerWindow;
    private readonly object _stateGate = new();
    private const int RightAltVirtualKeyCode = 0xA5;

    private IReadOnlyList<PromptMatch> _activeMatches = Array.Empty<PromptMatch>();
    private IntPtr _targetWindow;
    private IntPtr _imeCompositionTarget;
    private int _selectedIndex;
    private long _stateVersion;
    private long _textSyncRequest;
    private bool _suggestionsVisible;
    private bool _paused;
    private bool _disposed;

    public AppController(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        _applicationFilter = new ApplicationFilterService(_settings);
        AppThemeManager.Apply(_settings.Current.ThemeMode);
        _foregroundMonitor = new DispatcherTimer(DispatcherPriority.Background, dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(60)
        };
        _foregroundMonitor.Tick += HandleForegroundMonitorTick;
        _suggestionWindow = new SuggestionWindow();
        _suggestionWindow.SetThemeMode(_settings.Current.ThemeMode);
        _suggestionWindow.SuggestionBoxWidth = _settings.Current.SuggestionBoxWidth;
        _suggestionWindow.SelectionRequested += HandleMouseSelection;
        _ghostPreviewWindow = new GhostPreviewWindow();

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
        _suggestionWindow.HideSuggestions();
        _ghostPreviewWindow.HidePreview();
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

        var imeInputActive = HideSuggestionsIfImeComposing(foregroundWindow);
        if (IsShiftKey(input.VirtualKeyCode))
        {
            return KeyboardHookDecision.Pass;
        }

        if (imeInputActive)
        {
            if (input.VirtualKeyCode == NativeMethods.VK_ESCAPE)
            {
                ClearSuggestions(resetBuffer: true);
                CompleteImeComposition(foregroundWindow);
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

            ScheduleFocusedTextSync(foregroundWindow);
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

            if ((input.VirtualKeyCode == NativeMethods.VK_RETURN
                    && _settings.Current.EnableEnterConfirmation)
                || input.VirtualKeyCode == NativeMethods.VK_TAB && !input.IsShiftDown)
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
            RecomputeSuggestions(foregroundWindow);
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
                _inputBuffer.Append(inputText);
                // 物理缓冲此时已是最新 token，立即按其更新候选：窗口原地替换内容，不隐藏。
                RecomputeSuggestions(foregroundWindow);
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
        if ((!isShiftKey && !isRightAltKey) || IsPaused())
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

        ScheduleFocusedTextSync(
            foregroundWindow,
            preservePhysicalInputForShiftCommit: isShiftKey,
            waitForVoiceInputSettle: isRightAltKey);
    }

    private void SwitchTargetWindowIfNeeded(IntPtr foregroundWindow)
    {
        var changed = false;
        lock (_stateGate)
        {
            if (_targetWindow != IntPtr.Zero && _targetWindow != foregroundWindow)
            {
                changed = true;
                _imeCompositionTarget = IntPtr.Zero;
            }

            _targetWindow = foregroundWindow;
        }

        if (changed)
        {
            _inputBuffer.Reset();
            ClearSuggestions(resetBuffer: false);
        }
    }

    private bool IsImeCompositionObserved(IntPtr targetWindow)
    {
        lock (_stateGate)
        {
            return _imeCompositionTarget == targetWindow;
        }
    }

    private void CompleteImeComposition(IntPtr targetWindow)
    {
        lock (_stateGate)
        {
            if (_imeCompositionTarget == targetWindow)
            {
                _imeCompositionTarget = IntPtr.Zero;
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

    private void RecomputeSuggestions(
        IntPtr targetWindow,
        bool inferCommittedImeText = false)
    {
        if (NativeMethods.GetForegroundWindow() != targetWindow
            || !_applicationFilter.IsApplicationAllowed(targetWindow))
        {
            ClearSuggestions(resetBuffer: true);
            return;
        }

        var token = _inputBuffer.GetCurrentToken();
        var enabledItems = _catalog.GetEnabledItems();
        var matches = _matchService.Match(
            token,
            enabledItems,
            settings: _settings.Current);
        if (inferCommittedImeText)
        {
            // Chromium 类控件不暴露光标前文本时，物理缓冲里只有拼音字母，而输入框里
            // 实际已上屏中文。这里按拼音匹配结果反推出对应的中文名称片段，仅用于本次
            // 候选与确认时的删除长度计算；不回写 _inputBuffer，避免残留片段影响后续输入。
            var inferredMatch = matches.FirstOrDefault(match =>
                match.MatchKind is PromptMatchKind.PinyinFull or PromptMatchKind.PinyinInitial
                && match.HighlightLength > 0
                && match.HighlightStart >= 0
                && match.HighlightStart + match.HighlightLength <= match.Item.Name.Length);
            if (inferredMatch is not null)
            {
                token = inferredMatch.Item.Name.Substring(
                    inferredMatch.HighlightStart,
                    inferredMatch.HighlightLength);
                matches = _matchService.Match(
                    token,
                    enabledItems,
                    settings: _settings.Current);
            }
        }

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

            if (HideSuggestionsIfImeComposing(targetWindow))
            {
                return;
            }

            if (matches.Count == 0)
            {
                // 无匹配时关闭窗口，但必须先清空行内容再隐藏：
                // 否则分层窗口在隐藏瞬间会露出上一轮的候选行。
                _foregroundMonitor.Stop();
                _suggestionWindow.HideSuggestions();
                _ghostPreviewWindow.HidePreview();
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

                _suggestionWindow.ShowSuggestions(
                    matches,
                    currentIndex,
                    caretPosition,
                    _settings.Current.ShowContentPreview);
                ShowGhostPreview(matches[currentIndex].Item.Content, targetWindow, caretPosition);
                _foregroundMonitor.Start();
            }
            catch
            {
                _suggestionWindow.HideSuggestions();
                _ghostPreviewWindow.HidePreview();
            }
        });
    }

    // !SECTION 全局键盘事件与输入状态

    // SECTION 候选状态与确认

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

            if (HideSuggestionsIfImeComposing(targetWindow))
            {
                return;
            }

            if (HasSuggestions())
            {
                try
                {
                    var caretPosition = _caretPositionService.GetPosition(targetWindow);
                    _suggestionWindow.UpdateSelection(selectedIndex);
                    ShowGhostPreview(matches[selectedIndex].Item.Content, targetWindow, caretPosition);
                }
                catch
                {
                    _suggestionWindow.HideSuggestions();
                    _ghostPreviewWindow.HidePreview();
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
        _catalog.IncrementUsage(selectedMatch.Item.Id);

        PostAsyncToUi(async () =>
        {
            _suggestionWindow.HideSuggestions();
            _ghostPreviewWindow.HidePreview();
            if (NativeMethods.GetForegroundWindow() != targetWindow)
            {
                return;
            }

            await _textInsertionService.ReplaceAsync(
                targetWindow,
                selectedMatch.MatchLength,
                selectedMatch.Item.Content,
                _suggestionWindow.NativeHandle);
        });
    }

    private void HandleMouseSelection(int index)
    {
        ConfirmSelection(index);
    }

    private void HandleMouseButtonDown(int x, int y)
    {
        _ = _applicationFilter.GetForegroundProcessName();
        if (HasSuggestions() && !_suggestionWindow.ContainsScreenPoint(x, y))
        {
            ClearSuggestions(resetBuffer: true);
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
            _suggestionWindow.HideSuggestions();
            _ghostPreviewWindow.HidePreview();
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
    /// 输入法组合期间压住候选显示。这里只隐藏窗口、不结束候选状态：
    /// 组合结束后还要按拼音缓冲区重新匹配，若在此清空状态会丢掉触发串。
    /// 是否处于组合以 IMM32 的组合串为准；候选窗存在只作为补充信号，
    /// 因为部分输入法（如搜狗）的候选窗会常驻屏幕，单独依赖它会长期误判。
    /// </summary>
    private bool HideSuggestionsIfImeComposing(IntPtr targetWindow)
    {
        if (!_imeCompositionService.IsComposing(targetWindow))
        {
            return false;
        }

        lock (_stateGate)
        {
            _imeCompositionTarget = targetWindow;
        }

        PostToUi(() =>
        {
            _foregroundMonitor.Stop();
            _suggestionWindow.HideSuggestions();
            _ghostPreviewWindow.HidePreview();
        });
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
        }
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
        bool waitForVoiceInputSettle = false)
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
            var suggestionsSuppressed = false;
            var compositionDeadline = Environment.TickCount64 + 2000;
            while (true)
            {
                if (_imeCompositionService.IsComposing(targetWindow))
                {
                    observedComposition = true;
                    if (!suggestionsSuppressed || HasSuggestions())
                    {
                        HideSuggestionsIfImeComposing(targetWindow);
                        suggestionsSuppressed = true;
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

                if (observedComposition || IsImeCompositionObserved(targetWindow))
                {
                    await Task.Delay(20).ConfigureAwait(true);
                    if (request != Volatile.Read(ref _textSyncRequest)
                        || NativeMethods.GetForegroundWindow() != targetWindow)
                    {
                        return;
                    }

                    if (_imeCompositionService.IsComposing(targetWindow))
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

            var imeCompositionObserved = observedComposition
                || IsImeCompositionObserved(targetWindow);
            if (_imeCompositionService.IsComposing(targetWindow))
            {
                HideSuggestionsIfImeComposing(targetWindow);
                return;
            }

            var inferCommittedImeText = false;
            if (_focusedTextService.TryGetTextBeforeCaret(targetWindow, out var textBeforeCaret))
            {
                if (TextBeforeCaretMatchesCurrentToken(textBeforeCaret))
                {
                    // 光标前文本确实是当前 token 时，用它同步缓冲区（处理 Ctrl+V、撤销等）。
                    _inputBuffer.ReplaceFromTextBeforeCaret(textBeforeCaret);
                }
                else if (imeCompositionObserved && !preservePhysicalInputForShiftCommit)
                {
                    // 输入法已上屏中文：光标前的文本不再是拼音触发串，物理缓冲区里的字母
                    // 已经过期。此时必须清空，否则残留字母会与新按键拼成永不匹配的乱串。
                    _inputBuffer.Reset();
                }
            }
            else
            {
                if (imeCompositionObserved && !preservePhysicalInputForShiftCommit)
                {
                    // VS Code 的 Chromium 编辑器不暴露可靠的光标文本。保留组合期间收集的
                    // 拼音触发串，并按其对应的中文名称字符数删除已上屏文本。
                    inferCommittedImeText = true;
                }
            }

            RecomputeSuggestions(targetWindow, inferCommittedImeText);
            if (imeCompositionObserved)
            {
                CompleteImeComposition(targetWindow);
            }
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
            _suggestionWindow.SetThemeMode(settings.ThemeMode);
            _suggestionWindow.SuggestionBoxWidth = settings.SuggestionBoxWidth;
            if (!settings.ShowContentPreview)
            {
                _ghostPreviewWindow.HidePreview();
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
            _suggestionWindow.SetThemeMode(themeMode);
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
            _ghostPreviewWindow.HidePreview();
            return;
        }

        try
        {
            var hasControlBounds = _caretPositionService.TryGetTextControlBounds(
                targetWindow,
                out var controlBounds);
            _ghostPreviewWindow.ShowPreview(
                content,
                caretPosition,
                targetWindow,
                hasControlBounds ? controlBounds : null);
        }
        catch
        {
            _ghostPreviewWindow.HidePreview();
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
