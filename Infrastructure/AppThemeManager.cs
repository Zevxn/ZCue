using Microsoft.Win32;
using System.Windows.Media;
using WpfColor = System.Windows.Media.Color;
using ZCue.Models;

namespace ZCue.Infrastructure;

public static class AppThemeManager
{
    // SECTION 主题资源与应用

    public const string WindowBackgroundBrushKey = "ThemeWindowBackgroundBrush";
    public const string SidebarBrushKey = "ThemeSidebarBrush";
    public const string SurfaceBrushKey = "ThemeSurfaceBrush";
    public const string BorderBrushKey = "ThemeBorderBrush";
    public const string FieldBorderBrushKey = "ThemeFieldBorderBrush";
    public const string DividerBrushKey = "ThemeDividerBrush";
    public const string BadgeBackgroundBrushKey = "ThemeBadgeBackgroundBrush";
    public const string TextPrimaryBrushKey = "ThemeTextPrimaryBrush";
    public const string TextStrongBrushKey = "ThemeTextStrongBrush";
    public const string TextSecondaryBrushKey = "ThemeTextSecondaryBrush";
    public const string TextMutedBrushKey = "ThemeTextMutedBrush";
    public const string CategoryTextBrushKey = "ThemeCategoryTextBrush";
    public const string AccentBrushKey = "ThemeAccentBrush";
    public const string AccentHoverBrushKey = "ThemeAccentHoverBrush";
    public const string AccentForegroundBrushKey = "ThemeAccentForegroundBrush";
    public const string AccentSoftBrushKey = "ThemeAccentSoftBrush";
    public const string AccentTextBrushKey = "ThemeAccentTextBrush";
    public const string AccentSurfaceBrushKey = "ThemeAccentSurfaceBrush";
    public const string SelectionSurfaceBrushKey = "ThemeSelectionSurfaceBrush";
    public const string AccentSubtleBrushKey = "ThemeAccentSubtleBrush";
    public const string AccentBorderBrushKey = "ThemeAccentBorderBrush";
    public const string SuccessBrushKey = "ThemeSuccessBrush";
    public const string HoverSurfaceBrushKey = "ThemeHoverSurfaceBrush";
    public const string ToggleTrackBrushKey = "ThemeToggleTrackBrush";
    public const string DangerTextBrushKey = "ThemeDangerTextBrush";
    public const string DangerHoverBrushKey = "ThemeDangerHoverBrush";
    public const string DangerBorderBrushKey = "ThemeDangerBorderBrush";
    public const string GhostTextBrushKey = "ThemeGhostTextBrush";
    public const string ScrollTrackBrushKey = "ThemeScrollTrackBrush";
    public const string ScrollThumbBrushKey = "ThemeScrollThumbBrush";
    public const string ScrollThumbHoverBrushKey = "ThemeScrollThumbHoverBrush";

    private static readonly IReadOnlyDictionary<string, (string Light, string Dark)> Palette =
        new Dictionary<string, (string Light, string Dark)>
        {
            [WindowBackgroundBrushKey] = ("#F3F3F3", "#202020"),
            [SidebarBrushKey] = ("#00F3F3F3", "#00202020"),
            [SurfaceBrushKey] = ("#E6FFFFFF", "#E62B2B2B"),
            [BorderBrushKey] = ("#14000000", "#20FFFFFF"),
            [FieldBorderBrushKey] = ("#D1D1D1", "#5A5A5A"),
            [DividerBrushKey] = ("#F3F3F3", "#383838"),
            [BadgeBackgroundBrushKey] = ("#F3F3F3", "#3A3A3A"),
            [TextPrimaryBrushKey] = ("#1A1A1A", "#F3F3F3"),
            [TextStrongBrushKey] = ("#323232", "#E5E5E5"),
            [TextSecondaryBrushKey] = ("#616161", "#B3B3B3"),
            [TextMutedBrushKey] = ("#757575", "#8A8A8A"),
            [CategoryTextBrushKey] = ("#484848", "#CCCCCC"),
            [AccentBrushKey] = ("#0067C0", "#4CC2FF"),
            [AccentHoverBrushKey] = ("#E60067C0", "#E64CC2FF"),
            [AccentForegroundBrushKey] = ("#FFFFFF", "#000000"),
            [AccentSoftBrushKey] = ("#60A5FA", "#4CC2FF"),
            [AccentTextBrushKey] = ("#1A1A1A", "#FFFFFF"),
            [AccentSurfaceBrushKey] = ("#E9E9E9", "#363636"),
            [SelectionSurfaceBrushKey] = ("#E9E9E9", "#3D3D3D"),
            [AccentSubtleBrushKey] = ("#F0F0F0", "#363636"),
            [AccentBorderBrushKey] = ("#B3B3B3", "#5A5A5A"),
            [SuccessBrushKey] = ("#059669", "#34D399"),
            [HoverSurfaceBrushKey] = ("#E5E5E5", "#3A3A3A"),
            [ToggleTrackBrushKey] = ("#D1D1D1", "#505050"),
            [DangerTextBrushKey] = ("#DC2626", "#F87171"),
            [DangerHoverBrushKey] = ("#FEF2F2", "#450A0A"),
            [DangerBorderBrushKey] = ("#FCA5A5", "#7F1D1D"),
            [GhostTextBrushKey] = ("#9CA3AF", "#8A8A8A"),
            [ScrollTrackBrushKey] = ("Transparent", "Transparent"),
            [ScrollThumbBrushKey] = ("#D1D1D1", "#505050"),
            [ScrollThumbHoverBrushKey] = ("#757575", "#686868")
        };
    private static bool? _lastAppliedDarkTheme;

    public static bool IsDarkTheme(AppThemeMode mode)
    {
        return mode switch
        {
            AppThemeMode.Dark => true,
            AppThemeMode.Light => false,
            _ => IsSystemDarkTheme()
        };
    }

    public static void Apply(AppThemeMode mode)
    {
        var application = System.Windows.Application.Current;
        if (application is null)
        {
            return;
        }

        var isDark = IsDarkTheme(mode);
        var themeChanged = _lastAppliedDarkTheme != isDark;
        if (themeChanged)
        {
            // 与现有设置共用主题入口，避免 Fluent 控件和自绘浮窗使用不同主题。
            var fluentTheme = isDark
                ? Wpf.Ui.Appearance.ApplicationTheme.Dark
                : Wpf.Ui.Appearance.ApplicationTheme.Light;
            // 控件字典会读取强调色资源，先设置颜色，再切换主题字典。
            Wpf.Ui.Appearance.ApplicationAccentColorManager.Apply(
                isDark
                    ? System.Windows.Media.Color.FromRgb(76, 194, 255)
                    : System.Windows.Media.Color.FromRgb(0, 103, 192),
                fluentTheme);
            Wpf.Ui.Appearance.ApplicationThemeManager.Apply(
                fluentTheme,
                Wpf.Ui.Controls.WindowBackdropType.None,
                updateAccent: false);
            foreach (var (key, colors) in Palette)
            {
                var brush = new SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
                        isDark ? colors.Dark : colors.Light)!);
                brush.Freeze();
                application.Resources[key] = brush;
            }

            ApplyFluentAccentResources(application.Resources);
            _lastAppliedDarkTheme = isDark;
        }

        foreach (System.Windows.Window window in application.Windows)
        {
            ApplyWindowTitleBarTheme(window, isDark, themeChanged);
        }
    }

    private static void ApplyFluentAccentResources(System.Windows.ResourceDictionary resources)
    {
        var accent = ((SolidColorBrush)resources[AccentBrushKey]).Color;
        var foreground = ((SolidColorBrush)resources[AccentForegroundBrushKey]).Color;

        // 覆盖控件库生成的浅色阶，使按钮和开关使用一致的 Windows 强调色。
        resources["SystemAccentColorPrimary"] = accent;
        SetBrush(accent,
            "AccentButtonBackground", "ToggleSwitchFillOn", "AccentFillColorDefaultBrush",
            "SliderThumbBackground", "SliderTrackFillPointerOver", "SliderThumbBackgroundPointerOver");
        SetBrush(WpfColor.FromArgb(0xE6, accent.R, accent.G, accent.B),
            "AccentButtonBackgroundPointerOver", "ToggleSwitchFillOnPointerOver",
            "ToggleSwitchStrokeOnPointerOver", "AccentFillColorSecondaryBrush");
        SetBrush(WpfColor.FromArgb(0xCC, accent.R, accent.G, accent.B),
            "AccentButtonBackgroundPressed", "ToggleSwitchFillOnPressed",
            "ToggleSwitchStrokeOnPressed", "AccentFillColorTertiaryBrush");
        SetBrush(foreground,
            "AccentButtonForeground", "AccentButtonForegroundPointerOver",
            "ToggleSwitchKnobFillOn", "ToggleSwitchKnobFillOnPointerOver", "ToggleSwitchKnobFillOnPressed",
            "TextOnAccentFillColorPrimaryBrush", "TextOnAccentFillColorSecondaryBrush");

        void SetBrush(WpfColor color, params string[] keys)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            foreach (var key in keys)
            {
                resources[key] = brush;
            }
        }
    }

    public static void TrackWindow(System.Windows.Window window)
    {
        if (new System.Windows.Interop.WindowInteropHelper(window).Handle != IntPtr.Zero)
        {
            ApplyWindowTitleBarTheme(window, _lastAppliedDarkTheme ?? IsSystemDarkTheme());
            return;
        }

        window.SourceInitialized += (_, _) =>
            ApplyWindowTitleBarTheme(window, _lastAppliedDarkTheme ?? IsSystemDarkTheme());
    }

    private static void ApplyWindowTitleBarTheme(System.Windows.Window window, bool isDark, bool refreshBackdrop = false)
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
        }

        // 只为普通 Fluent 窗口刷新材质；透明候选窗继续使用原来的窗口样式。
        if (refreshBackdrop && window is Wpf.Ui.Controls.FluentWindow fluentWindow)
        {
            fluentWindow.WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.None;
            fluentWindow.WindowBackdropType = Wpf.Ui.Controls.WindowBackdropType.Mica;
        }

        var useDarkMode = isDark ? 1 : 0;
        try
        {
            if (NativeMethods.DwmSetWindowAttribute(
                    handle,
                    NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE,
                    ref useDarkMode,
                    sizeof(int)) < 0)
            {
                NativeMethods.DwmSetWindowAttribute(
                    handle,
                    NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE_LEGACY,
                    ref useDarkMode,
                    sizeof(int));
            }
        }
        catch (DllNotFoundException)
        {
        }
        catch (EntryPointNotFoundException)
        {
        }
    }

    private static bool IsSystemDarkTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(
                "Software\\Microsoft\\Windows\\CurrentVersion\\Themes\\Personalize");
            return Convert.ToInt32(key?.GetValue("AppsUseLightTheme") ?? 1) == 0;
        }
        catch
        {
            return false;
        }
    }

    // !SECTION 主题资源与应用
}
