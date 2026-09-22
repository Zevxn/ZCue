using Microsoft.Win32;
using System.Windows.Media;
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
            [WindowBackgroundBrushKey] = ("#F8FAFC", "#202020"),
            [SidebarBrushKey] = ("#F1F5F9", "#252525"),
            [SurfaceBrushKey] = ("#FFFFFF", "#2B2B2B"),
            [BorderBrushKey] = ("#E2E8F0", "#454545"),
            [FieldBorderBrushKey] = ("#CBD5E1", "#5A5A5A"),
            [DividerBrushKey] = ("#F1F5F9", "#383838"),
            [BadgeBackgroundBrushKey] = ("#F1F5F9", "#3A3A3A"),
            [TextPrimaryBrushKey] = ("#0F172A", "#F1F5F9"),
            [TextStrongBrushKey] = ("#334155", "#E5E5E5"),
            [TextSecondaryBrushKey] = ("#64748B", "#B3B3B3"),
            [TextMutedBrushKey] = ("#94A3B8", "#8A8A8A"),
            [CategoryTextBrushKey] = ("#475569", "#CCCCCC"),
            [AccentBrushKey] = ("#2563EB", "#2563EB"),
            [AccentHoverBrushKey] = ("#1D4ED8", "#1D4ED8"),
            [AccentSoftBrushKey] = ("#60A5FA", "#6B9FEA"),
            [AccentTextBrushKey] = ("#1D4ED8", "#F3F3F3"),
            [AccentSurfaceBrushKey] = ("#DBEAFE", "#363636"),
            [SelectionSurfaceBrushKey] = ("#DBEAFE", "#3D3D3D"),
            [AccentSubtleBrushKey] = ("#EFF6FF", "#363636"),
            [AccentBorderBrushKey] = ("#93C5FD", "#5A5A5A"),
            [SuccessBrushKey] = ("#059669", "#34D399"),
            [HoverSurfaceBrushKey] = ("#E2E8F0", "#3A3A3A"),
            [ToggleTrackBrushKey] = ("#CBD5E1", "#505050"),
            [DangerTextBrushKey] = ("#DC2626", "#F87171"),
            [DangerHoverBrushKey] = ("#FEF2F2", "#450A0A"),
            [DangerBorderBrushKey] = ("#FCA5A5", "#7F1D1D"),
            [GhostTextBrushKey] = ("#9CA3AF", "#8A8A8A"),
            [ScrollTrackBrushKey] = ("Transparent", "Transparent"),
            [ScrollThumbBrushKey] = ("#CBD5E1", "#505050"),
            [ScrollThumbHoverBrushKey] = ("#94A3B8", "#686868")
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
        if (_lastAppliedDarkTheme != isDark)
        {
            foreach (var (key, colors) in Palette)
            {
                var brush = new SolidColorBrush(
                    (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(
                        isDark ? colors.Dark : colors.Light)!);
                brush.Freeze();
                application.Resources[key] = brush;
            }

            _lastAppliedDarkTheme = isDark;
        }

        foreach (System.Windows.Window window in application.Windows)
        {
            ApplyWindowTitleBarTheme(window, isDark);
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

    private static void ApplyWindowTitleBarTheme(System.Windows.Window window, bool isDark)
    {
        var handle = new System.Windows.Interop.WindowInteropHelper(window).Handle;
        if (handle == IntPtr.Zero)
        {
            return;
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
