using System.Drawing;
using System.Windows.Forms;
using ZCue.Models;
using ZCue.Services;
using ZCue.Views;

namespace ZCue.Infrastructure;

public sealed class TrayIconService : IDisposable
{
    // SECTION 托盘菜单与状态

    private readonly StartupService _startupService;
    private readonly ApplicationFilterService _applicationFilter;
    private readonly Icon _icon;
    private readonly NotifyIcon _notifyIcon;
    private TrayMenuWindow? _menuWindow;
    private Uri? _pendingUpdateReleaseUri;
    private AppThemeMode _themeMode;
    private bool _startupEnabled;
    private bool _paused;
    private bool _disposed;

    public TrayIconService(
        StartupService startupService,
        ApplicationFilterService applicationFilter,
        AppThemeMode themeMode)
    {
        _startupService = startupService;
        _applicationFilter = applicationFilter;
        _themeMode = themeMode;
        _startupEnabled = SafeIsStartupEnabled();

        _icon = new Icon(System.IO.Path.Combine(AppContext.BaseDirectory, "assets", "logo.ico"));
        _notifyIcon = new NotifyIcon
        {
            Icon = _icon,
            Text = "ZCue Prompt 补全",
            Visible = true
        };
        _notifyIcon.MouseClick += HandleNotifyIconMouseClick;
        _notifyIcon.BalloonTipClicked += HandleBalloonTipClicked;
    }

    public event Action? OpenManagerRequested;

    public event Action? PauseRequested;

    public event Action? EnableRequested;

    public event Action? ExitRequested;

    public void ApplyTheme(AppThemeMode mode)
    {
        _themeMode = mode;
        _menuWindow?.ApplyTheme(mode);
    }

    public void UpdatePaused(bool paused)
    {
        _paused = paused;
        _menuWindow?.UpdatePaused(paused);
    }

    public void UpdateStartupState()
    {
        _startupEnabled = SafeIsStartupEnabled();
        _menuWindow?.UpdateStartupEnabled(_startupEnabled);
    }

    public void NotifyUpdateAvailable(string version, Uri releaseUri)
    {
        if (_disposed)
        {
            return;
        }

        _pendingUpdateReleaseUri = releaseUri;
        _notifyIcon.ShowBalloonTip(
            5000,
            "ZCue 有新版本",
            $"发现 {version}，点击通知查看更新说明和下载。",
            ToolTipIcon.Info);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        if (_menuWindow is { IsVisible: true } menuWindow)
        {
            menuWindow.Close();
        }

        _notifyIcon.MouseClick -= HandleNotifyIconMouseClick;
        _notifyIcon.BalloonTipClicked -= HandleBalloonTipClicked;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _icon.Dispose();
    }

    private void HandleNotifyIconMouseClick(object? sender, MouseEventArgs eventArgs)
    {
        if (eventArgs.Button == MouseButtons.Left)
        {
            OpenManagerRequested?.Invoke();
        }
        else if (eventArgs.Button == MouseButtons.Right)
        {
            ShowContextMenu();
        }
    }

    private void HandleBalloonTipClicked(object? sender, EventArgs eventArgs)
    {
        if (_pendingUpdateReleaseUri is not { } releaseUri
            || releaseUri.Scheme != Uri.UriSchemeHttps
            || !string.Equals(releaseUri.Host, "github.com", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(releaseUri.AbsoluteUri)
            {
                UseShellExecute = true
            });
        }
        catch
        {
            // 打开浏览器失败时不应影响托盘进程。
        }
    }

    private void ShowContextMenu()
    {
        if (_disposed)
        {
            return;
        }

        if (_menuWindow is { IsVisible: true } currentWindow)
        {
            currentWindow.Close();
        }

        _startupEnabled = SafeIsStartupEnabled();
        _ = _applicationFilter.GetForegroundProcessName();
        var currentApplication = _applicationFilter.GetLastExternalProcessName();
        var mode = _applicationFilter.FilterMode;
        var isListed = mode == ApplicationFilterMode.Blacklist
            ? _applicationFilter.IsBlacklisted(currentApplication)
            : _applicationFilter.IsWhitelisted(currentApplication);
        var filterActionText = currentApplication is null
            ? "无法识别当前应用"
            : mode == ApplicationFilterMode.Blacklist
                ? isListed ? "从黑名单移除当前应用" : "将当前应用加入黑名单"
                : isListed ? "从白名单移除当前应用" : "将当前应用加入白名单";

        var menuWindow = new TrayMenuWindow(
            _themeMode,
            _paused,
            _startupEnabled,
            filterActionText,
            currentApplication is not null);
        _menuWindow = menuWindow;
        menuWindow.ManagerRequested += HandleManagerRequested;
        menuWindow.ApplicationFilterActionRequested += HandleApplicationFilterActionRequested;
        menuWindow.ListeningToggleRequested += HandleListeningToggleRequested;
        menuWindow.StartupToggleRequested += HandleStartupToggleRequested;
        menuWindow.ExitRequested += HandleExitRequested;
        menuWindow.Closed += (_, _) =>
        {
            if (ReferenceEquals(_menuWindow, menuWindow))
            {
                _menuWindow = null;
            }
        };
        menuWindow.ShowAtScreenPoint(Cursor.Position);
    }

    private void HandleManagerRequested()
    {
        OpenManagerRequested?.Invoke();
    }

    private void HandleApplicationFilterActionRequested()
    {
        _applicationFilter.ToggleLastExternalApplication(out _);
    }

    private void HandleListeningToggleRequested()
    {
        if (_paused)
        {
            EnableRequested?.Invoke();
        }
        else
        {
            PauseRequested?.Invoke();
        }
    }

    private void HandleStartupToggleRequested()
    {
        var enabled = !_startupEnabled;
        try
        {
            _startupService.SetEnabled(enabled);
            _startupEnabled = enabled;
        }
        catch (Exception exception)
        {
            _startupEnabled = SafeIsStartupEnabled();
            AppDialogWindow.ShowMessage(
                null,
                "ZCue",
                $"设置开机启动失败：{exception.Message}",
                AppDialogTone.Warning);
        }
    }

    private void HandleExitRequested()
    {
        ExitRequested?.Invoke();
    }

    private bool SafeIsStartupEnabled()
    {
        try
        {
            return _startupService.IsEnabled();
        }
        catch
        {
            return false;
        }
    }

    // !SECTION 托盘菜单与状态
}
