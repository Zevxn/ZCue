using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TypeSense.Infrastructure;

namespace TypeSense.Views;

public enum AppDialogTone
{
    Information,
    Warning,
    Error,
    Question
}

public partial class AppDialogWindow : Window
{
    private readonly bool _isConfirmation;

    private AppDialogWindow(
        Window? owner,
        string title,
        string message,
        AppDialogTone tone,
        string primaryButtonText,
        bool isConfirmation,
        bool isDestructive)
    {
        InitializeComponent();

        _isConfirmation = isConfirmation;
        Title = title;
        TitleTextBlock.Text = string.IsNullOrWhiteSpace(title) ? "TypeSense" : title;
        MessageTextBlock.Text = message;
        PrimaryButton.Content = primaryButtonText;
        CancelButton.Visibility = isConfirmation ? Visibility.Visible : Visibility.Collapsed;
        PrimaryButton.Style = (Style)FindResource(
            isDestructive ? "DialogDangerButton" : "DialogPrimaryButton");
        PrimaryButton.IsDefault = !isDestructive;
        CancelButton.IsDefault = isDestructive;

        ApplyTone(tone);

        if (owner is { IsVisible: true })
        {
            Owner = owner;
            WindowStartupLocation = System.Windows.WindowStartupLocation.CenterOwner;
        }
        else
        {
            WindowStartupLocation = System.Windows.WindowStartupLocation.CenterScreen;
        }

        Loaded += (_, _) =>
        {
            if (isDestructive)
            {
                CancelButton.Focus();
            }
            else
            {
                PrimaryButton.Focus();
            }
        };
    }

    // SECTION 弹窗展示

    public static void ShowMessage(
        Window? owner,
        string title,
        string message,
        AppDialogTone tone = AppDialogTone.Information)
    {
        new AppDialogWindow(
            owner,
            title,
            message,
            tone,
            "确定",
            isConfirmation: false,
            isDestructive: false).ShowDialog();
    }

    public static bool Confirm(
        Window? owner,
        string title,
        string message,
        string confirmText = "确定",
        bool isDestructive = false)
    {
        var tone = isDestructive ? AppDialogTone.Warning : AppDialogTone.Question;
        var dialog = new AppDialogWindow(
            owner,
            title,
            message,
            tone,
            confirmText,
            isConfirmation: true,
            isDestructive);

        return dialog.ShowDialog() == true;
    }

    private void ApplyTone(AppDialogTone tone)
    {
        var (glyph, isDanger) = tone switch
        {
            AppDialogTone.Warning => ("!", true),
            AppDialogTone.Error => ("×", true),
            AppDialogTone.Question => ("?", false),
            _ => ("i", false)
        };

        IconGlyphText.Text = glyph;
        IconBadge.SetResourceReference(
            Border.BackgroundProperty,
            isDanger ? AppThemeManager.DangerHoverBrushKey : AppThemeManager.AccentSubtleBrushKey);
        IconGlyphText.SetResourceReference(
            TextBlock.ForegroundProperty,
            isDanger ? AppThemeManager.DangerTextBrushKey : AppThemeManager.AccentBrushKey);
    }

    // !SECTION 弹窗展示

    // SECTION 弹窗交互

    private void HandleCloseClick(object sender, RoutedEventArgs e)
    {
        CloseDialog(confirmed: false);
    }

    private void HandleCancelClick(object sender, RoutedEventArgs e)
    {
        CloseDialog(confirmed: false);
    }

    private void HandlePrimaryClick(object sender, RoutedEventArgs e)
    {
        if (_isConfirmation)
        {
            DialogResult = true;
            return;
        }

        Close();
    }

    private void HandleWindowKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key != Key.Escape)
        {
            return;
        }

        e.Handled = true;
        CloseDialog(confirmed: false);
    }

    private void CloseDialog(bool confirmed)
    {
        if (_isConfirmation)
        {
            DialogResult = confirmed;
            return;
        }

        Close();
    }

    // !SECTION 弹窗交互
}
