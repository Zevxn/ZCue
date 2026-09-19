using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TypeSense.Models;
using TypeSense.Services;
using TypeSense.ViewModels;
using WpfButton = System.Windows.Controls.Button;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfDataObject = System.Windows.DataObject;
using WpfDragEventArgs = System.Windows.DragEventArgs;
using WpfDragDropEffects = System.Windows.DragDropEffects;
using WpfListViewItem = System.Windows.Controls.ListViewItem;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfPoint = System.Windows.Point;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace TypeSense.Views;

public partial class PromptManagerWindow : Window
{
    private readonly PromptManagerViewModel _promptViewModel;
    private readonly SettingsViewModel _settingsViewModel;
    private readonly Action? _refreshStartupState;
    private bool _allowClose;
    private WpfPoint _promptDragStartPoint;
    private string? _pendingPromptDragId;
    private WpfPoint _categoryDragStartPoint;

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
        RenderCategoryButtons();
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
        var defaultCategoryId = _promptViewModel.SelectedCategoryId == "all"
            ? string.Empty
            : _promptViewModel.SelectedCategoryId;
        var editor = new PromptEditorWindow(
            categories: _promptViewModel.Categories.ToArray(),
            defaultCategoryId: defaultCategoryId)
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

        var editor = new PromptEditorWindow(item, _promptViewModel.Categories.ToArray())
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

    // SECTION 分类筛选与拖放

    private void HandleCategoryClick(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton { Tag: string categoryId })
        {
            return;
        }

        _promptViewModel.SelectedCategoryId = categoryId;
        UpdateCategoryButtonStates();
        UpdateEmptyState();
    }

    private void HandlePromptListMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _promptDragStartPoint = e.GetPosition(PromptListView);
        _pendingPromptDragId = null;

        if (e.OriginalSource is not DependencyObject source
            || FindAncestor<WpfButton>(source) is not null
            || FindAncestor<WpfCheckBox>(source) is not null)
        {
            return;
        }

        var container = ItemsControl.ContainerFromElement(PromptListView, source) as WpfListViewItem;
        if (container?.DataContext is PromptItem item)
        {
            _pendingPromptDragId = item.Id;
        }
    }

    private void HandlePromptListMouseMove(object sender, WpfMouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed
            || _pendingPromptDragId is null
            || e.OriginalSource is not DependencyObject source
            || FindAncestor<WpfButton>(source) is not null
            || FindAncestor<WpfCheckBox>(source) is not null)
        {
            return;
        }

        var currentPosition = e.GetPosition(PromptListView);
        if (Math.Abs(currentPosition.X - _promptDragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(currentPosition.Y - _promptDragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var promptId = _pendingPromptDragId;
        _pendingPromptDragId = null;
        var data = new WpfDataObject("TypeSense.PromptId", promptId);
        DragDrop.DoDragDrop(PromptListView, data, WpfDragDropEffects.Move);
    }

    private void HandleAddCategoryClick(object sender, RoutedEventArgs e)
    {
        var name = ShowCategoryNameDialog("新建分类", string.Empty);
        if (name is null)
        {
            return;
        }

        if (_promptViewModel.AddCategory(name) is null)
        {
            System.Windows.MessageBox.Show(
                this,
                "分类名称不能为空。",
                "无法创建分类",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        RenderCategoryButtons();
    }

    private void HandleRenameCategoryClick(object sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.Tag is not PromptCategory category)
        {
            return;
        }

        var name = ShowCategoryNameDialog("重命名分类", category.Name);
        if (name is null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            System.Windows.MessageBox.Show(
                this,
                "分类名称不能为空。",
                "无法重命名分类",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            return;
        }

        _promptViewModel.RenameCategory(category.Id, name);
        RenderCategoryButtons();
    }

    private void HandleDeleteCategoryClick(object sender, RoutedEventArgs e)
    {
        if ((sender as MenuItem)?.Tag is not PromptCategory category)
        {
            return;
        }

        var result = System.Windows.MessageBox.Show(
            this,
            $"删除分类“{category.Name}”后，其中的提示词会保留并变为无分类。确定删除吗？",
            "删除分类",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning);
        if (result == MessageBoxResult.Yes)
        {
            _promptViewModel.DeleteCategory(category.Id);
            RenderCategoryButtons();
            UpdateEmptyState();
        }
    }

    private void HandlePromptDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is WpfListViewItem { DataContext: PromptItem item }
            && e.OriginalSource is not WpfButton)
        {
            OpenEditor(item);
        }
    }

    // !SECTION 分类筛选与拖放

    // !SECTION 页面导航与列表交互

    // SECTION 窗口生命周期与辅助方法

    private void OpenEditor(PromptItem item)
    {
        var editor = new PromptEditorWindow(item, _promptViewModel.Categories.ToArray())
        {
            Owner = this
        };
        if (editor.ShowDialog() == true && editor.ResultItem is not null)
        {
            _promptViewModel.Update(editor.ResultItem);
            UpdateEmptyState();
        }
    }

    // SECTION 分类栏渲染与对话框

    private void RenderCategoryButtons()
    {
        CategoryPanel.Children.Clear();
        CategoryPanel.Children.Add(CreateCategoryButton("all", "全部"));
        CategoryPanel.Children.Add(CreateCategoryButton(string.Empty, "无分类"));

        foreach (var category in _promptViewModel.Categories)
        {
            var button = CreateCategoryButton(category.Id, category.Name);
            var contextMenu = new ContextMenu();
            var renameItem = new MenuItem
            {
                Header = "重命名",
                Tag = category
            };
            renameItem.Click += HandleRenameCategoryClick;
            var deleteItem = new MenuItem
            {
                Header = "删除分类",
                Tag = category
            };
            deleteItem.Click += HandleDeleteCategoryClick;
            contextMenu.Items.Add(renameItem);
            contextMenu.Items.Add(deleteItem);
            button.ContextMenu = contextMenu;
            CategoryPanel.Children.Add(button);
        }

        UpdateCategoryButtonStates();
    }

    private WpfButton CreateCategoryButton(string categoryId, string name)
    {
        var button = new WpfButton
        {
            Content = name,
            Tag = categoryId,
            Style = (Style)FindResource("CategoryPill"),
            ToolTip = name,
            ClickMode = ClickMode.Release,
            IsTabStop = true,
            Focusable = true,
            AllowDrop = categoryId != "all"
        };
        button.DragOver += HandleCategoryDragOver;
        button.Drop += HandleCategoryDrop;
        if (categoryId.Length > 0 && categoryId != "all")
        {
            button.PreviewMouseLeftButtonDown += HandleCategoryMouseLeftButtonDown;
            button.PreviewMouseMove += HandleCategoryMouseMove;
        }

        button.Click += HandleCategoryClick;
        return button;
    }

    private void HandleCategoryMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _categoryDragStartPoint = e.GetPosition(CategoryPanel);
    }

    private void HandleCategoryMouseMove(object sender, WpfMouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed
            || sender is not WpfButton { Tag: string categoryId }
            || categoryId.Length == 0
            || categoryId == "all")
        {
            return;
        }

        var currentPosition = e.GetPosition(CategoryPanel);
        if (Math.Abs(currentPosition.X - _categoryDragStartPoint.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(currentPosition.Y - _categoryDragStartPoint.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var data = new WpfDataObject("TypeSense.CategoryId", categoryId);
        DragDrop.DoDragDrop((WpfButton)sender, data, WpfDragDropEffects.Move);
    }

    private void HandleCategoryDragOver(object sender, WpfDragEventArgs e)
    {
        if (sender is not WpfButton { Tag: string targetCategoryId })
        {
            e.Effects = WpfDragDropEffects.None;
            e.Handled = true;
            return;
        }

        var isPrompt = e.Data.GetDataPresent("TypeSense.PromptId");
        var isCategory = targetCategoryId.Length > 0
            && targetCategoryId != "all"
            && e.Data.GetDataPresent("TypeSense.CategoryId");
        e.Effects = isPrompt || isCategory ? WpfDragDropEffects.Move : WpfDragDropEffects.None;
        e.Handled = true;
    }

    private void HandleCategoryDrop(object sender, WpfDragEventArgs e)
    {
        if (sender is not WpfButton { Tag: string targetCategoryId })
        {
            return;
        }

        if (e.Data.GetData("TypeSense.PromptId") is string promptId)
        {
            _promptViewModel.AssignCategory(promptId, targetCategoryId);
            UpdateEmptyState();
            return;
        }

        if (targetCategoryId.Length == 0
            || targetCategoryId == "all"
            || e.Data.GetData("TypeSense.CategoryId") is not string draggedCategoryId)
        {
            return;
        }

        var categories = _promptViewModel.Categories.ToList();
        var sourceIndex = categories.FindIndex(category => category.Id == draggedCategoryId);
        var targetIndex = categories.FindIndex(category => category.Id == targetCategoryId);
        if (sourceIndex < 0 || targetIndex < 0 || sourceIndex == targetIndex)
        {
            return;
        }

        var dropPosition = e.GetPosition((WpfButton)sender);
        var insertAfter = dropPosition.X >= ((WpfButton)sender).ActualWidth / 2;
        var movedCategory = categories[sourceIndex];
        categories.RemoveAt(sourceIndex);
        targetIndex = categories.FindIndex(category => category.Id == targetCategoryId);
        categories.Insert(targetIndex + (insertAfter ? 1 : 0), movedCategory);
        if (_promptViewModel.ReorderCategories(categories.Select(category => category.Id).ToArray()))
        {
            RenderCategoryButtons();
        }
    }

    private static T? FindAncestor<T>(DependencyObject source)
        where T : DependencyObject
    {
        var current = source;
        while (current is not null)
        {
            if (current is T match)
            {
                return match;
            }

            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }

        return null;
    }

    private void UpdateCategoryButtonStates()
    {
        foreach (var button in CategoryPanel.Children.OfType<WpfButton>())
        {
            var selected = button.Tag is string id
                && string.Equals(id, _promptViewModel.SelectedCategoryId, StringComparison.Ordinal);
            button.Background = selected
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(219, 234, 254))
                : System.Windows.Media.Brushes.White;
            button.Foreground = selected
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(29, 78, 216))
                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(71, 85, 105));
            button.BorderBrush = selected
                ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(37, 99, 235))
                : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(203, 213, 225));
        }
    }

    private string? ShowCategoryNameDialog(string title, string currentName)
    {
        var nameTextBox = new WpfTextBox
        {
            Text = currentName,
            FontSize = 14,
            Padding = new Thickness(10, 8, 10, 8),
            BorderBrush = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(203, 213, 225)),
            BorderThickness = new Thickness(1)
        };
        var saveButton = new WpfButton
        {
            Content = "保存",
            Style = (Style)FindResource("PrimaryButton"),
            IsDefault = true,
            MinWidth = 76
        };
        var cancelButton = new WpfButton
        {
            Content = "取消",
            Style = (Style)FindResource("SecondaryButton"),
            IsCancel = true,
            MinWidth = 76,
            Margin = new Thickness(0, 0, 9, 0)
        };
        var buttons = new StackPanel
        {
            Orientation = WpfOrientation.Horizontal,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Right,
            Margin = new Thickness(0, 18, 0, 0)
        };
        buttons.Children.Add(cancelButton);
        buttons.Children.Add(saveButton);

        var content = new StackPanel { Margin = new Thickness(24) };
        content.Children.Add(new TextBlock
        {
            Text = "分类名称",
            Foreground = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(51, 65, 85)),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });
        content.Children.Add(nameTextBox);
        content.Children.Add(buttons);

        var dialog = new Window
        {
            Title = title,
            Width = 360,
            Height = 190,
            MinWidth = 320,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(248, 250, 252)),
            Content = content
        };
        string? result = null;
        saveButton.Click += (_, _) =>
        {
            result = nameTextBox.Text.Trim();
            dialog.DialogResult = true;
        };
        dialog.Loaded += (_, _) =>
        {
            nameTextBox.Focus();
            nameTextBox.SelectAll();
        };

        return dialog.ShowDialog() == true ? result : null;
    }

    // !SECTION 分类栏渲染与对话框

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
