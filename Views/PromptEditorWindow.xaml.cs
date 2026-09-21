using System.Windows;
using ZCue.Infrastructure;
using ZCue.Models;

namespace ZCue.Views;

public partial class PromptEditorWindow : Window
{
    private readonly string? _editingId;
    private readonly int _usageCount;

    public PromptEditorWindow(
        PromptItem? item = null,
        IReadOnlyList<PromptCategory>? categories = null,
        string? defaultCategoryId = null)
    {
        InitializeComponent();
        AppThemeManager.TrackWindow(this);

        var categoryOptions = new List<PromptCategory>
        {
            new() { Id = string.Empty, Name = "无分类" }
        };
        if (categories is not null)
        {
            categoryOptions.AddRange(categories.Select(category => category.Clone()));
        }

        CategoryComboBox.ItemsSource = categoryOptions;
        CategoryComboBox.SelectedValue = item?.CategoryId ?? defaultCategoryId ?? string.Empty;

        if (item is null)
        {
            return;
        }

        _editingId = item.Id;
        _usageCount = item.UsageCount;
        Title = "编辑指令";
        TitleTextBlock.Text = "编辑指令";
        NameTextBox.Text = item.Name;
        ContentTextBox.Text = item.Content;
        EnabledCheckBox.IsChecked = item.Enabled;
    }

    public PromptItem? ResultItem { get; private set; }

    private void HandleSaveClick(object sender, RoutedEventArgs e)
    {
        var name = NameTextBox.Text.Trim();
        var content = ContentTextBox.Text;

        if (name.Length == 0)
        {
            ShowValidationMessage("请填写指令名称。", NameTextBox);
            return;
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            ShowValidationMessage("请填写指令内容。", ContentTextBox);
            return;
        }

        ResultItem = new PromptItem
        {
            Id = _editingId ?? Guid.NewGuid().ToString("N"),
            Name = name,
            Content = content,
            CategoryId = CategoryComboBox.SelectedValue as string ?? string.Empty,
            UsageCount = _usageCount,
            Enabled = EnabledCheckBox.IsChecked == true
        };
        DialogResult = true;
    }

    private void HandleCancelClick(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void ShowValidationMessage(string message, UIElement focusElement)
    {
        AppDialogWindow.ShowMessage(
            this,
            "无法保存指令",
            message,
            AppDialogTone.Information);
        focusElement.Focus();
    }
}
