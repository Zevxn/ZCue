using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TypeSense.Models;
using TypeSense.Services;
using TypeSense.ViewModels;
using WpfButton = System.Windows.Controls.Button;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfListViewItem = System.Windows.Controls.ListViewItem;

namespace TypeSense.Views;

public partial class PromptManagerWindow : Window
{
    private readonly PromptManagerViewModel _promptViewModel;
    private readonly SettingsViewModel _settingsViewModel;
    private readonly Action? _refreshStartupState;
    private bool _allowClose;

    public PromptManagerWindow(
        PromptCatalogService catalog,
        AppSettingsService settings,
        StartupService startupService,
        Func<bool> isListeningEnabled,
        Action<bool> setListeningEnabled,
        Action? refreshStartupState = null)
    {
        InitializeComponent();

        _refreshStartupState = refreshStartupState;
        _promptViewModel = new PromptManagerViewModel(catalog);
        _settingsViewModel = new SettingsViewModel(
            settings,
            startupService,
            isListeningEnabled,
            setListeningEnabled);
        _settingsViewModel.ErrorOccurred += HandleSettingsError;
        _settingsViewModel.PropertyChanged += HandleSettingsPropertyChanged;

        DataContext = _promptViewModel;
        SettingsPage.DataContext = _settingsViewModel;
        Closing += HandleClosing;
        Loaded += (_, _) => UpdateEmptyState();
    }

    public void CloseWithoutHiding()
    {
        if (!IsVisible)
        {
            return;
        }

        _allowClose = true;
        Close();
    }

    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        _settingsViewModel.Refresh();
        _promptViewModel.Reload(_promptViewModel.SelectedPrompt?.Id);
        UpdateEmptyState();
    }

    // SECTION 页面导航与列表交互

    private void ShowCommandsPage(object sender, RoutedEventArgs e)
    {
        CommandsPage.Visibility = Visibility.Visible;
        SettingsPage.Visibility = Visibility.Collapsed;
        CommandsNavigationButton.Background = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(219, 234, 254));
        CommandsNavigationButton.Foreground = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(29, 78, 216));
        SettingsNavigationButton.ClearValue(BackgroundProperty);
        SettingsNavigationButton.ClearValue(ForegroundProperty);
        _promptViewModel.Reload(_promptViewModel.SelectedPrompt?.Id);
        UpdateEmptyState();
    }

    private void ShowSettingsPage(object sender, RoutedEventArgs e)
    {
        CommandsPage.Visibility = Visibility.Collapsed;
        SettingsPage.Visibility = Visibility.Visible;
        SettingsNavigationButton.Background = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(219, 234, 254));
        SettingsNavigationButton.Foreground = new System.Windows.Media.SolidColorBrush(
            System.Windows.Media.Color.FromRgb(29, 78, 216));
        CommandsNavigationButton.ClearValue(BackgroundProperty);
        CommandsNavigationButton.ClearValue(ForegroundProperty);
        _settingsViewModel.Refresh();
    }

    private void HandleSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        _promptViewModel.SearchText = SearchTextBox.Text;
        UpdateEmptyState();
    }

    private void HandleAddClick(object sender, RoutedEventArgs e)
    {
        var editor = new PromptEditorWindow
        {
            Owner = this
        };
        if (editor.ShowDialog() == true && editor.ResultItem is not null)
        {
            _promptViewModel.Add(editor.ResultItem);
            UpdateEmptyState();
        }
    }

    private void HandleEditClick(object sender, RoutedEventArgs e)
    {
        if ((sender as WpfButton)?.Tag is not PromptItem item)
        {
            return;
        }

        var editor = new PromptEditorWindow(item)
        {
            Owner = this
        };
        if (editor.ShowDialog() == true && editor.ResultItem is not null)
        {
            _promptViewModel.Update(editor.ResultItem);
            UpdateEmptyState();
        }
    }

    private void HandleDeleteClick(object sender, RoutedEventArgs e)
    {
        if ((sender as WpfButton)?.Tag is not PromptItem item)
        {
            return;
        }

        var result = System.Windows.MessageBox.Show(
            this,
            $"确定删除“{item.Name}”吗？\n删除后无法恢复。",
            "删除指令",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result == MessageBoxResult.Yes)
        {
            _promptViewModel.Delete(item.Id);
            UpdateEmptyState();
        }
    }

    private void HandleEnabledClick(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfCheckBox checkBox
            || checkBox.DataContext is not PromptItem item)
        {
            return;
        }

        _promptViewModel.SetEnabled(item.Id, checkBox.IsChecked == true);
        UpdateEmptyState();
    }

    private void HandlePromptDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is WpfListViewItem { DataContext: PromptItem item }
            && e.OriginalSource is not WpfButton)
        {
            OpenEditor(item);
        }
    }

    // !SECTION 页面导航与列表交互

    // SECTION 窗口生命周期与辅助方法

    private void OpenEditor(PromptItem item)
    {
        var editor = new PromptEditorWindow(item)
        {
            Owner = this
        };
        if (editor.ShowDialog() == true && editor.ResultItem is not null)
        {
            _promptViewModel.Update(editor.ResultItem);
            UpdateEmptyState();
        }
    }

    private void UpdateEmptyState()
    {
        if (!IsLoaded)
        {
            return;
        }

        EmptyState.Visibility = _promptViewModel.FilteredPrompts.IsEmpty
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    private void HandleSettingsError(string message)
    {
        System.Windows.MessageBox.Show(
            this,
            message,
            "TypeSense",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        _settingsViewModel.Refresh();
        _refreshStartupState?.Invoke();
    }

    private void HandleSettingsPropertyChanged(
        object? sender,
        PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SettingsViewModel.LaunchAtStartup))
        {
            _refreshStartupState?.Invoke();
        }
    }

    private void HandleClosing(object? sender, CancelEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
        Hide();
    }

    // !SECTION 窗口生命周期与辅助方法
}
