using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using ZCue.Infrastructure;
using ZCue.Models;
using FormsScreen = System.Windows.Forms.Screen;
using WpfColor = System.Windows.Media.Color;
using WpfKeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace ZCue.Views;

public partial class TrayMenuWindow : Window
{
    // SECTION 托盘菜单状态与主题

    private System.Drawing.Point _screenPoint;

    public TrayMenuWindow(
        AppThemeMode themeMode,
        bool paused,
        bool startupEnabled,
        string applicationFilterActionText,
        bool applicationFilterActionEnabled)
    {
        InitializeComponent();
        AppThemeManager.TrackWindow(this);
        ApplyTheme(themeMode);
        UpdatePaused(paused);
        UpdateStartupEnabled(startupEnabled);
        ApplicationFilterText.Text = applicationFilterActionText;
        ApplicationFilterButton.IsEnabled = applicationFilterActionEnabled;
        Loaded += HandleLoaded;
    }

    public event Action? ManagerRequested;

    public event Action? ApplicationFilterActionRequested;

    public event Action? ListeningToggleRequested;

    public event Action? StartupToggleRequested;

    public event Action? ExitRequested;

    public void ApplyTheme(AppThemeMode mode)
    {
        var palette = TrayMenuPalette.Create(AppThemeManager.IsDarkTheme(mode));
        Resources["TrayMenuSurfaceBrush"] = CreateBrush(palette.Surface);
        Resources["TrayMenuBorderBrush"] = CreateBrush(palette.Border);
        Resources["TrayMenuTextBrush"] = CreateBrush(palette.Text);
        Resources["TrayMenuHoverBrush"] = CreateBrush(palette.Hover);
        Resources["TrayMenuPressedBrush"] = CreateBrush(palette.Pressed);
        Resources["TrayMenuCheckmarkBrush"] = CreateBrush(palette.Checkmark);
        Foreground = CreateBrush(palette.Text);
    }

    public void UpdatePaused(bool paused)
    {
        ListeningCheckBox.IsChecked = !paused;
    }

    public void UpdateStartupEnabled(bool enabled)
    {
        StartupCheckmark.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
    }

    public void ShowAtScreenPoint(System.Drawing.Point screenPoint)
    {
        _screenPoint = screenPoint;
        Left = screenPoint.X - Width / 2;
        Top = screenPoint.Y - Height;
        Opacity = 0;
        Show();
    }

    // !SECTION 托盘菜单状态与主题

    // SECTION 菜单定位与交互

    private void HandleLoaded(object sender, RoutedEventArgs e)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        var screen = FormsScreen.FromPoint(_screenPoint);
        var workArea = screen.WorkingArea;
        var width = ActualWidth * dpi.DpiScaleX;
        var height = ActualHeight * dpi.DpiScaleY;
        var shadowMarginX = 8 * dpi.DpiScaleX;
        var shadowMarginY = 8 * dpi.DpiScaleY;
        var margin = 4 * Math.Max(dpi.DpiScaleX, dpi.DpiScaleY);

        var left = _screenPoint.X - width / 2;
        var top = _screenPoint.Y - height - shadowMarginY;
        if (top < workArea.Top + margin)
        {
            top = _screenPoint.Y + shadowMarginY;
        }

        left = Clamp(left, workArea.Left + margin, workArea.Right - width - margin);
        top = Clamp(top, workArea.Top + margin, workArea.Bottom - height - margin);
        Left = left / dpi.DpiScaleX;
        Top = top / dpi.DpiScaleY;
        Opacity = 1;
        Activate();
    }

    private void HandleWindowDeactivated(object? sender, EventArgs e)
    {
        Dispatcher.BeginInvoke(
            DispatcherPriority.Background,
            new Action(() =>
            {
                if (IsVisible && !IsActive)
                {
                    Close();
                }
            }));
    }

    private void HandlePreviewKeyDown(object sender, WpfKeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }

    private void HandleManagerClick(object sender, RoutedEventArgs e)
    {
        InvokeAndClose(ManagerRequested);
    }

    private void HandleListeningClick(object sender, RoutedEventArgs e)
    {
        InvokeAndClose(ListeningToggleRequested);
    }

    private void HandleApplicationFilterClick(object sender, RoutedEventArgs e)
    {
        InvokeAndClose(ApplicationFilterActionRequested);
    }

    private void HandleStartupClick(object sender, RoutedEventArgs e)
    {
        InvokeAndClose(StartupToggleRequested);
    }

    private void HandleExitClick(object sender, RoutedEventArgs e)
    {
        InvokeAndClose(ExitRequested);
    }

    private void InvokeAndClose(Action? action)
    {
        Close();
        action?.Invoke();
    }

    private static double Clamp(double value, double minimum, double maximum)
    {
        return Math.Min(Math.Max(value, minimum), Math.Max(minimum, maximum));
    }

    private static SolidColorBrush CreateBrush(WpfColor color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    // !SECTION 菜单定位与交互

    // SECTION 托盘菜单配色

    private readonly record struct TrayMenuPalette(
        WpfColor Surface,
        WpfColor Border,
        WpfColor Text,
        WpfColor Hover,
        WpfColor Pressed,
        WpfColor Checkmark)
    {
        public static TrayMenuPalette Create(bool isDark)
        {
            return isDark
                ? new TrayMenuPalette(
                    WpfColor.FromRgb(32, 32, 32),
                    WpfColor.FromRgb(69, 69, 69),
                    WpfColor.FromRgb(243, 243, 243),
                    WpfColor.FromRgb(56, 56, 56),
                    WpfColor.FromRgb(68, 68, 68),
                    WpfColor.FromRgb(243, 243, 243))
                : new TrayMenuPalette(
                    WpfColor.FromRgb(250, 250, 250),
                    WpfColor.FromRgb(214, 214, 214),
                    WpfColor.FromRgb(31, 31, 31),
                    WpfColor.FromRgb(239, 239, 239),
                    WpfColor.FromRgb(228, 228, 228),
                    WpfColor.FromRgb(31, 31, 31));
        }
    }

    // !SECTION 托盘菜单配色
}
