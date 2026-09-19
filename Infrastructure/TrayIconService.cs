using System.Drawing;
using System.Windows.Forms;
using TypeSense.Services;

namespace TypeSense.Infrastructure;

public sealed class TrayIconService : IDisposable
{
    private readonly StartupService _startupService;
    private readonly Icon _icon;
    private readonly NotifyIcon _notifyIcon;
    private readonly ToolStripMenuItem _pauseItem;
    private readonly ToolStripMenuItem _enableItem;
    private readonly ToolStripMenuItem _startupItem;
    private bool _disposed;

    public TrayIconService(StartupService startupService)
    {
        _startupService = startupService;

        _pauseItem = new ToolStripMenuItem("暂停全局监听");
        _pauseItem.Click += (_, _) => PauseRequested?.Invoke();

        _enableItem = new ToolStripMenuItem("启用全局监听");
        _enableItem.Click += (_, _) => EnableRequested?.Invoke();

        _startupItem = new ToolStripMenuItem("开机启动")
        {
            CheckOnClick = true,
            Checked = SafeIsStartupEnabled()
        };
        _startupItem.Click += HandleStartupClick;

        var menu = new ContextMenuStrip();
        menu.Items.Add("打开 Prompt 管理器", null, (_, _) => OpenManagerRequested?.Invoke());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_pauseItem);
        menu.Items.Add(_enableItem);
        menu.Items.Add(_startupItem);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => ExitRequested?.Invoke());

        _icon = new Icon(System.IO.Path.Combine(AppContext.BaseDirectory, "assets", "logo.ico"));
        _notifyIcon = new NotifyIcon
        {
            Icon = _icon,
            Text = "TypeSense Prompt 补全",
            ContextMenuStrip = menu,
            Visible = true
        };
        _notifyIcon.DoubleClick += (_, _) => OpenManagerRequested?.Invoke();
        UpdatePaused(false);
    }

    public event Action? OpenManagerRequested;

    public event Action? PauseRequested;

    public event Action? EnableRequested;

    public event Action? ExitRequested;

    public void UpdatePaused(bool paused)
    {
        _pauseItem.Enabled = !paused;
        _enableItem.Enabled = paused;
    }

    public void UpdateStartupState()
    {
        _startupItem.Checked = SafeIsStartupEnabled();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _icon.Dispose();
    }

    private void HandleStartupClick(object? sender, EventArgs e)
    {
        try
        {
            _startupService.SetEnabled(_startupItem.Checked);
        }
        catch (Exception exception)
        {
            _startupItem.Checked = SafeIsStartupEnabled();
            System.Windows.MessageBox.Show(
                $"设置开机启动失败：{exception.Message}",
                "TypeSense",
                System.Windows.MessageBoxButton.OK,
                System.Windows.MessageBoxImage.Warning);
        }
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
}
