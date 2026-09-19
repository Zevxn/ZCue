using Microsoft.Win32;
using System.Windows.Media;
using TypeSense.Models;

namespace TypeSense.Infrastructure;

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
    public const string AccentTextBrushKey = "ThemeAccentTextBrush";
    public const string AccentSurfaceBrushKey = "ThemeAccentSurfaceBrush";
    public const string AccentSubtleBrushKey = "ThemeAccentSubtleBrush";
    public const string AccentBorderBrushKey = "ThemeAccentBorderBrush";
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
            [WindowBackgroundBrushKey] = ("#F8FAFC", "#171D2D"),
            [SidebarBrushKey] = ("#F1F5F9", "#020617"),
            [SurfaceBrushKey] = ("#FFFFFF", "#1E293B"),
            [BorderBrushKey] = ("#E2E8F0", "#334155"),
            [FieldBorderBrushKey] = ("#CBD5E1", "#475569"),
            [DividerBrushKey] = ("#F1F5F9", "#334155"),
            [BadgeBackgroundBrushKey] = ("#F1F5F9", "#334155"),
            [TextPrimaryBrushKey] = ("#0F172A", "#F1F5F9"),
            [TextStrongBrushKey] = ("#334155", "#E2E8F0"),
            [TextSecondaryBrushKey] = ("#64748B", "#94A3B8"),
            [TextMutedBrushKey] = ("#94A3B8", "#94A3B8"),
            [CategoryTextBrushKey] = ("#475569", "#CBD5E1"),
            [AccentBrushKey] = ("#2563EB", "#2563EB"),
            [AccentHoverBrushKey] = ("#1D4ED8", "#1D4ED8"),
            [AccentTextBrushKey] = ("#1D4ED8", "#93C5FD"),
            [AccentSurfaceBrushKey] = ("#DBEAFE", "#1E293B"),
            [AccentSubtleBrushKey] = ("#EFF6FF", "#334155"),
            [AccentBorderBrushKey] = ("#93C5FD", "#475569"),
            [HoverSurfaceBrushKey] = ("#E2E8F0", "#334155"),
            [ToggleTrackBrushKey] = ("#CBD5E1", "#475569"),
            [DangerTextBrushKey] = ("#DC2626", "#F87171"),
            [DangerHoverBrushKey] = ("#FEF2F2", "#450A0A"),
            [DangerBorderBrushKey] = ("#FCA5A5", "#7F1D1D"),
            [GhostTextBrushKey] = ("#9CA3AF", "#64748B"),
            [ScrollTrackBrushKey] = ("Transparent", "Transparent"),
            [ScrollThumbBrushKey] = ("#CBD5E1", "#475569"),
            [ScrollThumbHoverBrushKey] = ("#94A3B8", "#64748B")
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
