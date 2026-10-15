using System.Windows;
using ZCue.Infrastructure;
using ZCue.Models;
using ZCue.Services;

namespace ZCue.Views;

public partial class PromptReferenceWindow : Wpf.Ui.Controls.FluentWindow
{
    private readonly IReadOnlyList<PromptItem> _items;

    public PromptReferenceWindow(IReadOnlyList<PromptItem> items, string editingId)
    {
        _items = items.Where(item => item.Id != editingId).ToArray();
        InitializeComponent();
        AppThemeManager.TrackWindow(this);
        RefreshItems();
        Loaded += (_, _) => SearchTextBox.Focus();
    }

    public PromptItem? SelectedPrompt { get; private set; }

    private void HandleSearchTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (PromptListBox is not null)
        {
            RefreshItems();
        }
    }

    private void RefreshItems()
    {
        var query = SearchTextBox.Text.Trim();
        var pinyin = PinyinAliasService.NormalizeInput(query);
        var filtered = _items.Where(item => query.Length == 0
                || item.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || item.CategoryName.Contains(query, StringComparison.OrdinalIgnoreCase)
                || item.Content.Contains(query, StringComparison.OrdinalIgnoreCase)
                || (pinyin.Length > 0 && item.PinyinAliases.Any(alias =>
                    alias.SearchText.Contains(pinyin, StringComparison.Ordinal))))
            .ToArray();
        PromptListBox.ItemsSource = filtered;
        EmptyTextBlock.Visibility = filtered.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void HandleSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        InsertButton.IsEnabled = PromptListBox.SelectedItem is PromptItem;
    }

    private void HandleDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        // 空白区域双击不使用上次选择。
        if (System.Windows.Controls.ItemsControl.ContainerFromElement(
                PromptListBox, e.OriginalSource as DependencyObject) is System.Windows.Controls.ListBoxItem)
        {
            HandleInsertClick(sender, e);
        }
    }

    private void HandleInsertClick(object sender, RoutedEventArgs e)
    {
        if (PromptListBox.SelectedItem is PromptItem item)
        {
            SelectedPrompt = item;
            DialogResult = true;
        }
    }

    private void HandleCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
