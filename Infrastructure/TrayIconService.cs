using System.Drawing;
using System.Windows.Forms;
using TypeSense.Models;
using TypeSense.Services;
using TypeSense.Views;

namespace TypeSense.Infrastructure;

public sealed class TrayIconService : IDisposable
{
    // SECTION 托盘菜单与状态

    private readonly StartupService _startupService;
    private readonly Icon _icon;
    private readonly NotifyIcon _notifyIcon;
    private TrayMenuWindow? _menuWindow;
    private AppThemeMode _themeMode;
    private bool _startupEnabled;
    private bool _paused;
    private bool _disposed;

    public TrayIconService(StartupService startupService, AppThemeMode themeMode)
    {
        _startupService = startupService;
        _themeMode = themeMode;
        _startupEnabled = SafeIsStartupEnabled();

        _icon = new Icon(System.IO.Path.Combine(AppContext.BaseDirectory, "assets", "logo.ico"));
        _notifyIcon = new NotifyIcon
        {
            Icon = _icon,
            Text = "TypeSense Prompt 补全",
            Visible = true
        };
        _notifyIcon.MouseClick += HandleNotifyIconMouseClick;
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
        var menuWindow = new TrayMenuWindow(_themeMode, _paused, _startupEnabled);
        _menuWindow = menuWindow;
        menuWindow.ManagerRequested += HandleManagerRequested;
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
            System.Windows.MessageBox.Show(
                $"设置开机启动失败：{exception.Message}",
                "TypeSense",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
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
