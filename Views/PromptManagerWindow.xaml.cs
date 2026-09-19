using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using TypeSense.Infrastructure;
using TypeSense.Models;
using TypeSense.Services;
using TypeSense.ViewModels;
using WpfButton = System.Windows.Controls.Button;
using WpfCheckBox = System.Windows.Controls.CheckBox;
using WpfCursor = System.Windows.Input.Cursor;
using WpfCursors = System.Windows.Input.Cursors;
using WpfDataObject = System.Windows.DataObject;
using WpfDragEventArgs = System.Windows.DragEventArgs;
using WpfDragDropEffects = System.Windows.DragDropEffects;
using WpfGiveFeedbackEventArgs = System.Windows.GiveFeedbackEventArgs;
using WpfImage = System.Windows.Controls.Image;
using WpfListViewItem = System.Windows.Controls.ListViewItem;
using WpfMouseEventArgs = System.Windows.Input.MouseEventArgs;
using WpfOrientation = System.Windows.Controls.Orientation;
using WpfPoint = System.Windows.Point;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace TypeSense.Views;

public partial class PromptManagerWindow : Window
{
    private readonly PromptManagerViewModel _promptViewModel;
    private readonly PromptStorageService _transferStorage = new();
    private readonly SettingsViewModel _settingsViewModel;
    private readonly Action? _refreshStartupState;
    private readonly WpfCursor _grabCursor;
    private readonly WpfCursor _grabbingCursor;
    private bool _allowClose;
    private WpfPoint _promptDragStartPoint;
    private WpfPoint _promptDragGrabOffset;
    private string? _pendingPromptDragId;
    private string[]? _promptDragOriginalOrder;
    private bool _promptReorderCommitted;
    private WpfPoint _categoryDragStartPoint;
    private WpfPoint _categoryDragGrabOffset;
    private string[]? _categoryDragOriginalOrder;
    private bool _categoryReorderCommitted;
    private WpfPoint _dragPreviewPointerOffset;
    private UIElement? _dragPreviewSource;
    private double _dragPreviewSourceOpacity;
    private WpfImage? _dragPreviewImage;
    private System.Windows.Threading.DispatcherTimer? _dragPreviewTimer;
    private FrameworkElement? _pressedDragCursorElement;

    public PromptManagerWindow(
        PromptCatalogService catalog,
        AppSettingsService settings,
        ApplicationFilterService applicationFilter,
        StartupService startupService,
        Func<bool> isListeningEnabled,
        Action<bool> setListeningEnabled,
        Action? refreshStartupState = null)
    {
        InitializeComponent();
        _grabCursor = LoadCursor("grab.cur");
        _grabbingCursor = LoadCursor("grabbing.cur");
        PreviewMouseLeftButtonUp += (_, _) => ResetPressedDragCursor();
        Deactivated += (_, _) => ResetPressedDragCursor();
        AppThemeManager.TrackWindow(this);

        _refreshStartupState = refreshStartupState;
        _promptViewModel = new PromptManagerViewModel(catalog);
        _settingsViewModel = new SettingsViewModel(
            settings,
            applicationFilter,
            startupService,
            isListeningEnabled,
            setListeningEnabled);
        _settingsViewModel.ErrorOccurred += HandleSettingsError;
        _settingsViewModel.PropertyChanged += HandleSettingsPropertyChanged;

        DataContext = _promptViewModel;
        RenderCategoryButtons();
        SettingsPage.DataContext = _settingsViewModel;
        Closing += HandleClosing;
        Loaded += (_, _) =>
        {
            UpdateEmptyState();
            UpdateBatchSelectionState();
        };
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
        CommandsNavigationButton.SetResourceReference(
            System.Windows.Controls.Control.BackgroundProperty,
            AppThemeManager.AccentSurfaceBrushKey);
        CommandsNavigationButton.SetResourceReference(
            System.Windows.Controls.Control.ForegroundProperty,
            AppThemeManager.AccentTextBrushKey);
        SettingsNavigationButton.ClearValue(BackgroundProperty);
        SettingsNavigationButton.ClearValue(ForegroundProperty);
        _promptViewModel.Reload(_promptViewModel.SelectedPrompt?.Id);
        UpdateEmptyState();
    }

    private void ShowSettingsPage(object sender, RoutedEventArgs e)
    {
        CommandsPage.Visibility = Visibility.Collapsed;
        SettingsPage.Visibility = Visibility.Visible;
        SettingsNavigationButton.SetResourceReference(
            System.Windows.Controls.Control.BackgroundProperty,
            AppThemeManager.AccentSurfaceBrushKey);
        SettingsNavigationButton.SetResourceReference(
            System.Windows.Controls.Control.ForegroundProperty,
            AppThemeManager.AccentTextBrushKey);
        CommandsNavigationButton.ClearValue(BackgroundProperty);
        CommandsNavigationButton.ClearValue(ForegroundProperty);
        _settingsViewModel.Refresh();
    }

    private void HandleSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        _promptViewModel.SearchText = SearchTextBox.Text;
        UpdateEmptyState();
    }

    private void HandlePromptSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        UpdateBatchSelectionState();
    }

    private void UpdateBatchSelectionState()
    {
        if (!IsInitialized)
        {
            return;
        }

        var selectedCount = PromptListView.SelectedItems.Count;
        var hasBatchSelection = selectedCount > 1;
        BatchSelectionSummary.Text = $"已选 {selectedCount} 项";
        BatchActionsBar.Visibility = hasBatchSelection
            ? Visibility.Visible
            : Visibility.Collapsed;
        HeaderActionsPanel.Visibility = hasBatchSelection
            ? Visibility.Collapsed
            : Visibility.Visible;
    }

    private string[] GetSelectedPromptIds() => PromptListView.SelectedItems
        .OfType<PromptItem>()
        .Select(prompt => prompt.Id)
        .Distinct(StringComparer.Ordinal)
        .ToArray();

    private void HandleBatchAssignCategoryClick(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton button || GetSelectedPromptIds().Length < 2)
        {
            return;
        }

        var menu = new ContextMenu
        {
            Style = (Style)FindResource("CategoryContextMenu"),
            PlacementTarget = button,
            Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom
        };
        AddBatchCategoryMenuItem(menu, "无分类", string.Empty);
        foreach (var category in _promptViewModel.Categories)
        {
            AddBatchCategoryMenuItem(menu, category.Name, category.Id);
        }

        menu.IsOpen = true;
    }

    private void AddBatchCategoryMenuItem(ContextMenu menu, string name, string categoryId)
    {
        var item = new MenuItem
        {
            Header = name,
            Tag = categoryId,
            Style = (Style)FindResource("CategoryContextMenuItem")
        };
        item.Click += HandleBatchAssignCategoryMenuClick;
        menu.Items.Add(item);
    }

    private void HandleBatchAssignCategoryMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string categoryId })
        {
            return;
        }

        _promptViewModel.AssignCategory(GetSelectedPromptIds(), categoryId);
        UpdateEmptyState();
    }

    private void HandleBatchDeleteClick(object sender, RoutedEventArgs e)
    {
        var selectedIds = GetSelectedPromptIds();
        if (selectedIds.Length < 2)
        {
            return;
        }

        var confirmation = AppDialogWindow.Confirm(
            this,
            "批量删除提示词",
            $"确定删除选中的 {selectedIds.Length} 条提示词吗？\n删除后无法恢复。",
            "删除",
            isDestructive: true);
        if (confirmation)
        {
            _promptViewModel.DeleteMany(selectedIds);
            UpdateEmptyState();
        }
    }

    private void HandleClearSelectionClick(object sender, RoutedEventArgs e)
    {
        PromptListView.SelectedItems.Clear();
        PromptListView.Focus();
    }

    private void HandleImportClick(object sender, RoutedEventArgs e)
    {
        var fileDialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "导入提示词",
            Filter = "JSON 文件 (*.json)|*.json|所有文件 (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };
        if (fileDialog.ShowDialog(this) != true)
        {
            return;
        }

        PromptStorageService.StorageSnapshot importData;
        try
        {
            importData = _transferStorage.LoadImportFile(fileDialog.FileName);
        }
        catch (Exception error)
        {
            AppDialogWindow.ShowMessage(
                this,
                "导入失败",
                $"无法导入该文件。\n{error.Message}",
                AppDialogTone.Warning);
            return;
        }

        var confirmation = AppDialogWindow.Confirm(
            this,
            "确认导入",
            $"文件中包含 {importData.Prompts.Count} 条提示词和 {importData.Categories.Count} 个分类。\n"
            + "导入会保留现有提示词；相同 ID 的提示词将跳过，同名分类会合并，找不到对应分类的提示词会归入“无分类”。\n\n"
            + "是否继续？",
            "导入");
        if (!confirmation)
        {
            return;
        }

        var result = _promptViewModel.Import(importData.Prompts, importData.Categories);
        if (result.AddedPromptCount > 0 || result.AddedCategoryCount > 0)
        {
            _promptViewModel.SelectedCategoryId = "all";
            SearchTextBox.Clear();
        }

        RenderCategoryButtons();
        UpdateEmptyState();

        var summary = $"新增提示词：{result.AddedPromptCount} 条\n"
            + $"新增分类：{result.AddedCategoryCount} 个\n"
            + $"合并到已有分类：{result.MergedCategoryCount} 个\n"
            + $"跳过提示词：{result.SkippedPromptCount} 条\n"
            + $"忽略无效分类：{result.SkippedCategoryCount} 个";
        AppDialogWindow.ShowMessage(
            this,
            "导入完成",
            summary);
    }

    private void HandleExportClick(object sender, RoutedEventArgs e)
    {
        var prompts = _promptViewModel.Prompts.ToArray();
        var categories = _promptViewModel.Categories.ToArray();
        if (prompts.Length == 0 && categories.Length == 0)
        {
            AppDialogWindow.ShowMessage(
                this,
                "无法导出",
                "当前没有可导出的提示词或分类。",
                AppDialogTone.Information);
            return;
        }

        var exportDialog = new PromptExportWindow(categories, prompts)
        {
            Owner = this
        };
        if (exportDialog.ShowDialog() != true)
        {
            return;
        }

        var selectedCategoryIds = exportDialog.SelectedCategoryIds
            .ToHashSet(StringComparer.Ordinal);
        var promptsToExport = prompts
            .Where(prompt => selectedCategoryIds.Contains(prompt.CategoryId ?? string.Empty))
            .Select(prompt => prompt.Clone())
            .ToArray();
        var categoriesToExport = categories
            .Where(category => selectedCategoryIds.Contains(category.Id))
            .Select(category => category.Clone())
            .ToArray();

        var fileDialog = new Microsoft.Win32.SaveFileDialog
        {
            Title = "导出提示词",
            Filter = "JSON 文件 (*.json)|*.json",
            DefaultExt = ".json",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = $"提示词备份_{DateTime.Now:yyyy-MM-dd}.json"
        };
        if (fileDialog.ShowDialog(this) != true)
        {
            return;
        }

        try
        {
            _transferStorage.ExportToFile(
                fileDialog.FileName,
                promptsToExport,
                categoriesToExport);
        }
        catch (Exception error)
        {
            AppDialogWindow.ShowMessage(
                this,
                "导出失败",
                $"导出失败。\n{error.Message}",
                AppDialogTone.Warning);
            return;
        }

        AppDialogWindow.ShowMessage(
            this,
            "导出完成",
            $"已导出 {promptsToExport.Length} 条提示词和 {categoriesToExport.Length} 个分类。");
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

        var result = AppDialogWindow.Confirm(
            this,
            "删除指令",
            $"确定删除“{item.Name}”吗？\n删除后无法恢复。",
            "删除",
            isDestructive: true);
        if (result)
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

    // SECTION 拖拽排序与分类筛选

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

        if (e.OriginalSource is not DependencyObject source)
        {
            return;
        }

        if (FindAncestor<WpfButton>(source) is not null
            || FindAncestor<WpfCheckBox>(source) is not null)
        {
            return;
        }

        var container = ItemsControl.ContainerFromElement(PromptListView, source) as WpfListViewItem;
        if (container?.DataContext is PromptItem item)
        {
            SetPressedDragCursor(container);
            _pendingPromptDragId = item.Id;
            _promptDragGrabOffset = e.GetPosition(container);
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
        _promptDragOriginalOrder = _promptViewModel.Prompts
            .Select(prompt => prompt.Id)
            .ToArray();
        _promptReorderCommitted = false;
        var selectedPromptIds = GetSelectedPromptIds();
        if (!selectedPromptIds.Contains(promptId, StringComparer.Ordinal))
        {
            PromptListView.SelectedItems.Clear();
            if (FindPromptContainer(promptId) is { } draggedContainer)
            {
                draggedContainer.IsSelected = true;
            }

            selectedPromptIds = [promptId];
        }

        var data = new WpfDataObject();
        if (selectedPromptIds.Length > 1)
        {
            data.SetData("TypeSense.PromptIds", selectedPromptIds);
        }
        else
        {
            data.SetData("TypeSense.PromptId", promptId);
        }

        if (FindPromptContainer(promptId) is { } sourceContainer)
        {
            BeginDragPreview(sourceContainer, _promptDragGrabOffset);
        }

        try
        {
            DragDrop.DoDragDrop(PromptListView, data, WpfDragDropEffects.Move);
        }
        finally
        {
            ResetPressedDragCursor();
            EndDragPreview();
            if (!_promptReorderCommitted && _promptDragOriginalOrder is not null)
            {
                var oldPositions = CapturePromptPositions();
                _promptViewModel.RestorePromptOrder(_promptDragOriginalOrder);
                AnimatePromptFlow(oldPositions);
            }

            _promptDragOriginalOrder = null;
            _promptReorderCommitted = false;
        }
    }

    private WpfListViewItem? FindPromptContainer(string promptId)
    {
        var prompt = _promptViewModel.Prompts.FirstOrDefault(item => item.Id == promptId);
        return prompt is null
            ? null
            : PromptListView.ItemContainerGenerator.ContainerFromItem(prompt) as WpfListViewItem;
    }

    private void HandlePromptListDragOver(object sender, WpfDragEventArgs e)
    {
        UpdateDragPreviewPosition();
        if (e.Data.GetData("TypeSense.PromptId") is not string draggedId)
        {
            e.Effects = WpfDragDropEffects.None;
            e.Handled = true;
            return;
        }

        e.Effects = WpfDragDropEffects.Move;
        if (TryGetPromptDropTarget(e, out var targetId, out var insertAfter))
        {
            var oldPositions = CapturePromptPositions();
            if (_promptViewModel.MovePrompt(draggedId, targetId, insertAfter))
            {
                AnimatePromptFlow(oldPositions);
            }
        }

        e.Handled = true;
    }

    private void HandlePromptListDrop(object sender, WpfDragEventArgs e)
    {
        if (e.Data.GetData("TypeSense.PromptId") is not string)
        {
            e.Effects = WpfDragDropEffects.None;
            e.Handled = true;
            return;
        }

        var orderedIds = _promptViewModel.Prompts
            .Select(prompt => prompt.Id)
            .ToArray();
        var saved = _promptViewModel.ReorderPrompts(orderedIds);
        _promptReorderCommitted = saved
            || (_promptDragOriginalOrder is not null
                && _promptDragOriginalOrder.SequenceEqual(orderedIds, StringComparer.Ordinal));
        e.Effects = WpfDragDropEffects.Move;
        e.Handled = true;
    }

    private bool TryGetPromptDropTarget(
        WpfDragEventArgs e,
        out string targetId,
        out bool insertAfter)
    {
        targetId = string.Empty;
        insertAfter = false;
        var pointerY = e.GetPosition(PromptListView).Y;
        PromptItem? lastVisiblePrompt = null;

        foreach (var prompt in _promptViewModel.FilteredPrompts.Cast<PromptItem>())
        {
            if (PromptListView.ItemContainerGenerator.ContainerFromItem(prompt)
                is not WpfListViewItem container)
            {
                continue;
            }

            lastVisiblePrompt = prompt;
            var top = container.TransformToAncestor(PromptListView)
                .Transform(new WpfPoint(0, 0)).Y;
            if (pointerY < top + container.ActualHeight / 2)
            {
                targetId = prompt.Id;
                return true;
            }
        }

        if (lastVisiblePrompt is null)
        {
            return false;
        }

        targetId = lastVisiblePrompt.Id;
        insertAfter = true;
        return true;
    }

    private void HandleDragGiveFeedback(object sender, WpfGiveFeedbackEventArgs e)
    {
        UpdateDragPreviewPosition();
        e.UseDefaultCursors = false;
        Mouse.SetCursor(_grabbingCursor);
    }

    private void HandlePromptRowCardLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement promptRow)
        {
            promptRow.Cursor = _grabCursor;
        }
    }

    private void SetPressedDragCursor(FrameworkElement element)
    {
        ResetPressedDragCursor();
        _pressedDragCursorElement = element;
        element.Cursor = _grabbingCursor;
        Mouse.OverrideCursor = _grabbingCursor;
    }

    private void ResetPressedDragCursor()
    {
        if (_pressedDragCursorElement is not null)
        {
            _pressedDragCursorElement.Cursor = _grabCursor;
            _pressedDragCursorElement = null;
        }

        if (ReferenceEquals(Mouse.OverrideCursor, _grabbingCursor))
        {
            Mouse.OverrideCursor = null;
        }
    }

    private void BeginDragPreview(UIElement source, WpfPoint pointerOffset)
    {
        EndDragPreview();
        if (source.RenderSize.Width <= 0
            || source.RenderSize.Height <= 0
            || CaptureVisual(source) is not { } snapshot)
        {
            return;
        }

        _dragPreviewSource = source;
        _dragPreviewSourceOpacity = source.Opacity;
        _dragPreviewPointerOffset = pointerOffset;
        _dragPreviewImage = new WpfImage
        {
            Source = snapshot,
            Width = source.RenderSize.Width,
            Height = source.RenderSize.Height,
            Opacity = 0.96,
            IsHitTestVisible = false,
            Effect = new DropShadowEffect
            {
                Color = Colors.Black,
                BlurRadius = 12,
                ShadowDepth = 2,
                Opacity = 0.18,
                RenderingBias = RenderingBias.Performance
            }
        };

        DragPreviewCanvas.Children.Add(_dragPreviewImage);
        source.Opacity = 0.32;
        UpdateDragPreviewPosition();

        _dragPreviewTimer = new System.Windows.Threading.DispatcherTimer(
            System.Windows.Threading.DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(16)
        };
        _dragPreviewTimer.Tick += HandleDragPreviewTimerTick;
        _dragPreviewTimer.Start();
    }

    private static BitmapSource? CaptureVisual(UIElement source)
    {
        var size = source.RenderSize;
        if (size.Width <= 0 || size.Height <= 0)
        {
            return null;
        }

        var dpi = VisualTreeHelper.GetDpi(source);
        var bitmap = new RenderTargetBitmap(
            Math.Max(1, (int)Math.Ceiling(size.Width * dpi.DpiScaleX)),
            Math.Max(1, (int)Math.Ceiling(size.Height * dpi.DpiScaleY)),
            96 * dpi.DpiScaleX,
            96 * dpi.DpiScaleY,
            PixelFormats.Pbgra32);
        bitmap.Render(source);
        bitmap.Freeze();
        return bitmap;
    }

    private void HandleDragPreviewTimerTick(object? sender, EventArgs e)
    {
        UpdateDragPreviewPosition();
    }

    private void UpdateDragPreviewPosition()
    {
        if (_dragPreviewImage is null)
        {
            return;
        }

        var pointer = Mouse.GetPosition(DragPreviewCanvas);
        Canvas.SetLeft(_dragPreviewImage, pointer.X - _dragPreviewPointerOffset.X);
        Canvas.SetTop(_dragPreviewImage, pointer.Y - _dragPreviewPointerOffset.Y);
    }

    private void EndDragPreview()
    {
        if (_dragPreviewTimer is not null)
        {
            _dragPreviewTimer.Stop();
            _dragPreviewTimer.Tick -= HandleDragPreviewTimerTick;
            _dragPreviewTimer = null;
        }

        if (_dragPreviewImage is not null)
        {
            DragPreviewCanvas.Children.Remove(_dragPreviewImage);
            _dragPreviewImage = null;
        }

        if (_dragPreviewSource is not null)
        {
            _dragPreviewSource.Opacity = _dragPreviewSourceOpacity;
            _dragPreviewSource = null;
        }
    }

    private Dictionary<string, WpfPoint> CapturePromptPositions()
    {
        PromptListView.UpdateLayout();
        var positions = new Dictionary<string, WpfPoint>(StringComparer.Ordinal);
        foreach (var prompt in _promptViewModel.FilteredPrompts.Cast<PromptItem>())
        {
            if (PromptListView.ItemContainerGenerator.ContainerFromItem(prompt)
                is WpfListViewItem container)
            {
                positions[prompt.Id] = container.TransformToAncestor(PromptListView)
                    .Transform(new WpfPoint(0, 0));
            }
        }

        return positions;
    }

    private void AnimatePromptFlow(IReadOnlyDictionary<string, WpfPoint> oldPositions)
    {
        PromptListView.UpdateLayout();
        foreach (var prompt in _promptViewModel.FilteredPrompts.Cast<PromptItem>())
        {
            if (PromptListView.ItemContainerGenerator.ContainerFromItem(prompt)
                is WpfListViewItem container)
            {
                ResetFlowTransform(container);
            }
        }

        PromptListView.UpdateLayout();
        foreach (var prompt in _promptViewModel.FilteredPrompts.Cast<PromptItem>())
        {
            if (!oldPositions.TryGetValue(prompt.Id, out var oldPosition)
                || PromptListView.ItemContainerGenerator.ContainerFromItem(prompt)
                    is not WpfListViewItem container)
            {
                continue;
            }

            var newPosition = container.TransformToAncestor(PromptListView)
                .Transform(new WpfPoint(0, 0));
            AnimateFlow(container, oldPosition, newPosition);
        }
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
            AppDialogWindow.ShowMessage(
                this,
                "无法创建分类",
                "分类名称不能为空。",
                AppDialogTone.Information);
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
            AppDialogWindow.ShowMessage(
                this,
                "无法重命名分类",
                "分类名称不能为空。",
                AppDialogTone.Information);
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

        var result = AppDialogWindow.Confirm(
            this,
            "删除分类",
            $"删除分类“{category.Name}”后，其中的提示词会保留并变为无分类。确定删除吗？",
            "删除",
            isDestructive: true);
        if (result)
        {
            _promptViewModel.DeleteCategory(category.Id);
            RenderCategoryButtons();
            UpdateEmptyState();
        }
    }

    private void HandlePromptDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is WpfListViewItem { DataContext: PromptItem item }
            && e.OriginalSource is DependencyObject source
            && FindAncestor<WpfButton>(source) is null
            && FindAncestor<WpfCheckBox>(source) is null)
        {
            OpenEditor(item);
        }
    }

    // !SECTION 拖拽排序与分类筛选

    // SECTION 应用范围设置

    private void AddCurrentApplicationClick(object sender, RoutedEventArgs e)
    {
        _settingsViewModel.AddLastExternalApplication();
    }

    private void SelectApplicationClick(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "选择应用程序",
            Filter = "应用程序 (*.exe)|*.exe",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog(this) == true)
        {
            _settingsViewModel.AddApplication(Path.GetFileName(dialog.FileName));
        }
    }

    private void RemoveApplicationClick(object sender, RoutedEventArgs e)
    {
        if ((sender as WpfButton)?.Tag is string processName)
        {
            _settingsViewModel.RemoveApplication(processName);
        }
    }

    // !SECTION 应用范围设置

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

    private static WpfCursor LoadCursor(string fileName)
    {
        try
        {
            var cursorUri = new Uri($"pack://application:,,,/assets/cursors/{fileName}", UriKind.Absolute);
            var cursorResource = System.Windows.Application.GetResourceStream(cursorUri);
            if (cursorResource is null)
            {
                return WpfCursors.Hand;
            }

            using var stream = cursorResource.Stream;
        return new WpfCursor(stream);
        }
        catch (Exception error) when (error is ArgumentException or IOException or InvalidOperationException or Win32Exception)
        {
            return WpfCursors.Hand;
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
            var contextMenu = new ContextMenu
            {
                Style = (Style)FindResource("CategoryContextMenu")
            };
            var renameItem = new MenuItem
            {
                Header = "重命名",
                Tag = category,
                Style = (Style)FindResource("CategoryContextMenuItem")
            };
            renameItem.Click += HandleRenameCategoryClick;
            var deleteItem = new MenuItem
            {
                Header = "删除分类",
                Tag = category,
                Style = (Style)FindResource("CategoryContextMenuDangerItem")
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
            Cursor = categoryId.Length > 0 && categoryId != "all"
                ? _grabCursor
                : WpfCursors.Hand,
            ToolTip = categoryId.Length > 0 && categoryId != "all"
                ? $"{name}（拖动可排序）"
                : name,
            ClickMode = ClickMode.Release,
            IsTabStop = true,
            Focusable = true,
            AllowDrop = categoryId != "all"
        };
        button.GiveFeedback += HandleDragGiveFeedback;
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
        if (sender is WpfButton button)
        {
            SetPressedDragCursor(button);
            _categoryDragGrabOffset = e.GetPosition(button);
        }
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
        _categoryDragOriginalOrder = GetCategoryButtonOrder();
        _categoryReorderCommitted = false;
        var sourceButton = (WpfButton)sender;
        BeginDragPreview(sourceButton, _categoryDragGrabOffset);

        try
        {
            DragDrop.DoDragDrop(sourceButton, data, WpfDragDropEffects.Move);
        }
        finally
        {
            ResetPressedDragCursor();
            EndDragPreview();
            if (!_categoryReorderCommitted && _categoryDragOriginalOrder is not null)
            {
                ApplyCategoryButtonOrder(_categoryDragOriginalOrder);
            }

            _categoryDragOriginalOrder = null;
            _categoryReorderCommitted = false;
        }
    }

    private void HandleCategoryDragOver(object sender, WpfDragEventArgs e)
    {
        UpdateDragPreviewPosition();
        if (sender is not WpfButton { Tag: string targetCategoryId })
        {
            e.Effects = WpfDragDropEffects.None;
            e.Handled = true;
            return;
        }

        if (e.Data.GetData("TypeSense.PromptIds") is string[] promptIds
            && promptIds.Length > 0)
        {
            e.Effects = WpfDragDropEffects.Move;
        }
        else if (e.Data.GetData("TypeSense.PromptId") is string)
        {
            e.Effects = WpfDragDropEffects.Move;
        }
        else if (targetCategoryId.Length > 0
            && targetCategoryId != "all"
            && e.Data.GetData("TypeSense.CategoryId") is string draggedCategoryId)
        {
            e.Effects = WpfDragDropEffects.Move;
            var targetButton = (WpfButton)sender;
            var insertAfter = e.GetPosition(targetButton).X >= targetButton.ActualWidth / 2;
            PreviewCategoryMove(draggedCategoryId, targetCategoryId, insertAfter);
        }
        else
        {
            e.Effects = WpfDragDropEffects.None;
        }

        e.Handled = true;
    }

    private void HandleCategoryDrop(object sender, WpfDragEventArgs e)
    {
        if (sender is not WpfButton { Tag: string targetCategoryId })
        {
            return;
        }

        if (e.Data.GetData("TypeSense.PromptIds") is string[] promptIds
            && promptIds.Length > 0)
        {
            _promptViewModel.AssignCategory(promptIds, targetCategoryId);
            UpdateEmptyState();
            e.Effects = WpfDragDropEffects.Move;
            e.Handled = true;
            return;
        }

        if (e.Data.GetData("TypeSense.PromptId") is string promptId)
        {
            _promptViewModel.AssignCategory(promptId, targetCategoryId);
            UpdateEmptyState();
            e.Effects = WpfDragDropEffects.Move;
            e.Handled = true;
            return;
        }

        if (targetCategoryId.Length == 0
            || targetCategoryId == "all"
            || e.Data.GetData("TypeSense.CategoryId") is not string)
        {
            e.Effects = WpfDragDropEffects.None;
            e.Handled = true;
            return;
        }

        var orderedIds = GetCategoryButtonOrder();
        var saved = _promptViewModel.ReorderCategories(orderedIds);
        _categoryReorderCommitted = saved
            || (_categoryDragOriginalOrder is not null
                && _categoryDragOriginalOrder.SequenceEqual(orderedIds, StringComparer.Ordinal));
        UpdateCategoryButtonStates();
        e.Effects = WpfDragDropEffects.Move;
        e.Handled = true;
    }

    private void PreviewCategoryMove(string draggedId, string targetId, bool insertAfter)
    {
        var orderedIds = GetCategoryButtonOrder().ToList();
        var sourceIndex = orderedIds.IndexOf(draggedId);
        var targetIndex = orderedIds.IndexOf(targetId);
        if (sourceIndex < 0 || targetIndex < 0 || sourceIndex == targetIndex)
        {
            return;
        }

        orderedIds.RemoveAt(sourceIndex);
        targetIndex = orderedIds.IndexOf(targetId);
        var insertIndex = targetIndex + (insertAfter ? 1 : 0);
        if (insertIndex == sourceIndex)
        {
            return;
        }

        orderedIds.Insert(insertIndex, draggedId);
        ApplyCategoryButtonOrder(orderedIds);
    }

    private string[] GetCategoryButtonOrder() => CategoryPanel.Children
        .OfType<WpfButton>()
        .Select(button => button.Tag as string ?? string.Empty)
        .Where(id => id.Length > 0 && id != "all")
        .ToArray();

    private void ApplyCategoryButtonOrder(IReadOnlyList<string> orderedIds)
    {
        var oldPositions = CaptureCategoryPositions();
        var buttons = CategoryPanel.Children.OfType<WpfButton>().ToArray();
        var allButton = buttons.FirstOrDefault(button => button.Tag as string == "all");
        var uncategorizedButton = buttons.FirstOrDefault(button => button.Tag as string == string.Empty);
        if (allButton is null
            || uncategorizedButton is null
            || orderedIds.Count != buttons.Length - 2)
        {
            return;
        }

        var buttonsById = buttons
            .Where(button => button.Tag is string id && id.Length > 0 && id != "all")
            .ToDictionary(button => (string)button.Tag, StringComparer.Ordinal);
        if (orderedIds.Any(id => !buttonsById.ContainsKey(id)))
        {
            return;
        }

        var desiredButtons = new List<WpfButton>(buttons.Length)
        {
            allButton,
            uncategorizedButton
        };
        desiredButtons.AddRange(orderedIds.Select(id => buttonsById[id]));

        for (var targetIndex = 0; targetIndex < desiredButtons.Count; targetIndex++)
        {
            var button = desiredButtons[targetIndex];
            var currentIndex = CategoryPanel.Children.IndexOf(button);
            if (currentIndex != targetIndex)
            {
                CategoryPanel.Children.RemoveAt(currentIndex);
                CategoryPanel.Children.Insert(targetIndex, button);
            }
        }

        AnimateCategoryFlow(oldPositions);
    }

    private Dictionary<string, WpfPoint> CaptureCategoryPositions()
    {
        CategoryPanel.UpdateLayout();
        var positions = new Dictionary<string, WpfPoint>(StringComparer.Ordinal);
        foreach (var button in CategoryPanel.Children.OfType<WpfButton>())
        {
            if (button.Tag is string id)
            {
                positions[id] = button.TransformToAncestor(CategoryPanel)
                    .Transform(new WpfPoint(0, 0));
            }
        }

        return positions;
    }

    private void AnimateCategoryFlow(IReadOnlyDictionary<string, WpfPoint> oldPositions)
    {
        CategoryPanel.UpdateLayout();
        foreach (var button in CategoryPanel.Children.OfType<WpfButton>())
        {
            ResetFlowTransform(button);
        }

        CategoryPanel.UpdateLayout();
        foreach (var button in CategoryPanel.Children.OfType<WpfButton>())
        {
            if (button.Tag is not string id
                || !oldPositions.TryGetValue(id, out var oldPosition))
            {
                continue;
            }

            var newPosition = button.TransformToAncestor(CategoryPanel)
                .Transform(new WpfPoint(0, 0));
            AnimateFlow(button, oldPosition, newPosition);
        }
    }

    private static void AnimateFlow(UIElement element, WpfPoint oldPosition, WpfPoint newPosition)
    {
        ResetFlowTransform(element);
        var translate = GetFlowTransform(element);

        var offsetX = oldPosition.X - newPosition.X;
        var offsetY = oldPosition.Y - newPosition.Y;
        if (Math.Abs(offsetX) < 0.5 && Math.Abs(offsetY) < 0.5)
        {
            return;
        }

        var duration = new Duration(TimeSpan.FromMilliseconds(170));
        if (Math.Abs(offsetX) >= 0.5)
        {
            translate.BeginAnimation(
                TranslateTransform.XProperty,
                new DoubleAnimation(offsetX, 0, duration)
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                    FillBehavior = FillBehavior.Stop
                });
        }

        if (Math.Abs(offsetY) >= 0.5)
        {
            translate.BeginAnimation(
                TranslateTransform.YProperty,
                new DoubleAnimation(offsetY, 0, duration)
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                    FillBehavior = FillBehavior.Stop
                });
        }
    }

    private static void ResetFlowTransform(UIElement element)
    {
        var translate = element.RenderTransform switch
        {
            TranslateTransform direct => direct,
            TransformGroup group => group.Children.OfType<TranslateTransform>().FirstOrDefault(),
            _ => null
        };
        if (translate is null)
        {
            return;
        }

        translate.BeginAnimation(TranslateTransform.XProperty, null);
        translate.BeginAnimation(TranslateTransform.YProperty, null);
        translate.X = 0;
        translate.Y = 0;
    }

    private static TranslateTransform GetFlowTransform(UIElement element)
    {
        if (element.RenderTransform is TransformGroup existingGroup)
        {
            var existingTransform = existingGroup.Children.OfType<TranslateTransform>().FirstOrDefault();
            if (existingTransform is not null)
            {
                return existingTransform;
            }

            var addedTransform = new TranslateTransform();
            existingGroup.Children.Add(addedTransform);
            return addedTransform;
        }

        var transformGroup = new TransformGroup();
        if (element.RenderTransform is not null)
        {
            transformGroup.Children.Add(element.RenderTransform);
        }

        var translateTransform = new TranslateTransform();
        transformGroup.Children.Add(translateTransform);
        element.RenderTransform = transformGroup;
        return translateTransform;
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
            button.SetResourceReference(
                System.Windows.Controls.Control.BackgroundProperty,
                selected ? AppThemeManager.AccentSurfaceBrushKey : AppThemeManager.SurfaceBrushKey);
            button.SetResourceReference(
                System.Windows.Controls.Control.ForegroundProperty,
                selected ? AppThemeManager.AccentTextBrushKey : AppThemeManager.CategoryTextBrushKey);
            button.SetResourceReference(
                System.Windows.Controls.Control.BorderBrushProperty,
                selected ? AppThemeManager.AccentBrushKey : AppThemeManager.FieldBorderBrushKey);
        }
    }

    private string? ShowCategoryNameDialog(string title, string currentName)
    {
        var nameTextBox = new WpfTextBox
        {
            Text = currentName,
            FontSize = 14,
            Padding = new Thickness(10, 8, 10, 8),
            BorderThickness = new Thickness(1),
            Style = (Style)FindResource("RoundedTextBoxBase")
        };
        nameTextBox.SetResourceReference(
            System.Windows.Controls.Control.BackgroundProperty,
            AppThemeManager.SurfaceBrushKey);
        nameTextBox.SetResourceReference(
            System.Windows.Controls.Control.ForegroundProperty,
            AppThemeManager.TextPrimaryBrushKey);
        nameTextBox.SetResourceReference(
            System.Windows.Controls.Control.BorderBrushProperty,
            AppThemeManager.FieldBorderBrushKey);
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
        var titleText = new TextBlock
        {
            Text = "分类名称",
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        };
        titleText.SetResourceReference(
            System.Windows.Controls.TextBlock.ForegroundProperty,
            AppThemeManager.TextStrongBrushKey);
        content.Children.Add(titleText);
        content.Children.Add(nameTextBox);
        content.Children.Add(buttons);

        var dialog = new Window
        {
            Title = title,
            Width = 360,
            MinWidth = 320,
            MinHeight = 220,
            SizeToContent = SizeToContent.Height,
            ResizeMode = ResizeMode.NoResize,
            WindowStartupLocation = WindowStartupLocation.CenterOwner,
            Owner = this,
            Content = content
        };
        AppThemeManager.TrackWindow(dialog);
        dialog.SetResourceReference(
            Window.BackgroundProperty,
            AppThemeManager.WindowBackgroundBrushKey);
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
        AppDialogWindow.ShowMessage(
            this,
            "TypeSense",
            message,
            AppDialogTone.Warning);
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
