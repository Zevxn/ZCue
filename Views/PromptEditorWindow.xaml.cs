using System.Windows;
using TypeSense.Models;

namespace TypeSense.Views;

public partial class PromptEditorWindow : Window
{
    private readonly string? _editingId;
    private readonly int _usageCount;

    public PromptEditorWindow(PromptItem? item = null)
    {
        InitializeComponent();

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
        System.Windows.MessageBox.Show(
            this,
            message,
            "无法保存指令",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        focusElement.Focus();
    }
}
