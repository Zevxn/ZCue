using System.Windows;
using ZCue.Infrastructure;
using ZCue.Models;

using WpfApplicationCommands = System.Windows.Input.ApplicationCommands;
using WpfExecutedRoutedEventArgs = System.Windows.Input.ExecutedRoutedEventArgs;
using WpfComboBox = System.Windows.Controls.ComboBox;
using WpfComboBoxItem = System.Windows.Controls.ComboBoxItem;
using WpfSelectionChangedEventArgs = System.Windows.Controls.SelectionChangedEventArgs;
using WpfTextBox = System.Windows.Controls.TextBox;
using WinFormsClipboard = System.Windows.Forms.Clipboard;
using WinFormsDataObject = System.Windows.Forms.DataObject;
using WinFormsTextDataFormat = System.Windows.Forms.TextDataFormat;

namespace ZCue.Views;

public partial class PromptEditorWindow : Wpf.Ui.Controls.FluentWindow
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

    private void HandleTextBoxPreviewExecuted(object sender, WpfExecutedRoutedEventArgs e)
    {
        // 菜单和快捷键共用剪切命令，保留写入成功后才删除选区的行为。
        if (e.Command != WpfApplicationCommands.Cut
            || sender is not WpfTextBox textBox
            || textBox.IsReadOnly
            || textBox.SelectionLength == 0)
        {
            return;
        }

        e.Handled = true;
        try
        {
            var clipboardData = new WinFormsDataObject();
            clipboardData.SetText(textBox.SelectedText, WinFormsTextDataFormat.UnicodeText);
            WinFormsClipboard.SetDataObject(clipboardData, true, 20, 25);
            textBox.SelectedText = string.Empty;
        }
        catch (Exception exception) when (exception is System.Runtime.InteropServices.ExternalException
            or InvalidOperationException)
        {
            AppDialogWindow.ShowMessage(this, "剪切失败", "无法写入系统剪贴板，所选文字未删除。");
        }
    }

    private void HandleVariableSelectionChanged(object sender, WpfSelectionChangedEventArgs e)
    {
        if (sender is not WpfComboBox comboBox
            || comboBox.SelectedItem is not WpfComboBoxItem { Tag: string variableName })
        {
            return;
        }

        var variable = variableName switch
        {
            "date" => "{{date}}",
            "time" => "{{time}}",
            "clipboard" => "{{clipboard}}",
            _ => string.Empty
        };
        if (variable.Length == 0)
        {
            return;
        }

        var selectionStart = ContentTextBox.SelectionStart;
        var selectionLength = ContentTextBox.SelectionLength;
        var resultingLength = ContentTextBox.Text.Length - selectionLength + variable.Length;
        if (resultingLength > ContentTextBox.MaxLength)
        {
            comboBox.SelectedIndex = 0;
            ContentTextBox.Focus();
            return;
        }

        comboBox.SelectedIndex = 0;
        ContentTextBox.SelectedText = variable;
        ContentTextBox.Focus();
        ContentTextBox.CaretIndex = selectionStart + variable.Length;
    }

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
