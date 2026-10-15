using System.Windows;
using ZCue.Infrastructure;
using ZCue.Models;
using ZCue.Services;

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
    private readonly string _editingId;
    private readonly int _usageCount;
    private readonly IReadOnlyList<PromptItem> _referenceItems;
    private readonly System.Windows.Threading.DispatcherTimer _referenceValidationTimer = new()
    {
        Interval = TimeSpan.FromMilliseconds(200)
    };

    // SECTION 编辑器初始化

    public PromptEditorWindow(
        PromptItem? item = null,
        IReadOnlyList<PromptCategory>? categories = null,
        string? defaultCategoryId = null,
        string? initialContent = null,
        IReadOnlyList<PromptItem>? prompts = null)
    {
        _editingId = item?.Id ?? Guid.NewGuid().ToString("N");
        _referenceItems = prompts ?? [];
        InitializeComponent();
        AppThemeManager.TrackWindow(this);
        _referenceValidationTimer.Tick += (_, _) => UpdateReferenceStatus();
        Closed += (_, _) => _referenceValidationTimer.Stop();

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
            ContentTextBox.Text = initialContent ?? string.Empty;
            UpdateReferenceStatus();
            return;
        }

        _usageCount = item.UsageCount;
        Title = "编辑指令";
        TitleTextBlock.Text = "编辑指令";
        NameTextBox.Text = item.Name;
        ContentTextBox.Text = item.Content;
        EnabledCheckBox.IsChecked = item.Enabled;
        UpdateReferenceStatus();
    }

    public PromptItem? ResultItem { get; private set; }

    // !SECTION 编辑器初始化

    // SECTION 正文编辑与引用

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

        comboBox.SelectedIndex = 0;
        InsertContentToken(variable);
    }

    private void InsertContentToken(string token)
    {
        var selectionStart = ContentTextBox.SelectionStart;
        var selectionLength = ContentTextBox.SelectionLength;
        var resultingLength = ContentTextBox.Text.Length - selectionLength + token.Length;
        if (resultingLength > ContentTextBox.MaxLength)
        {
            AppDialogWindow.ShowMessage(this, "无法插入", "插入后正文将超过 10,000 个字符。");
            ContentTextBox.Focus();
            return;
        }

        ContentTextBox.SelectedText = token;
        ContentTextBox.Focus();
        ContentTextBox.CaretIndex = selectionStart + token.Length;
    }

    private void HandleInsertReferenceClick(object sender, RoutedEventArgs e)
    {
        var selectionStart = ContentTextBox.SelectionStart;
        var selectionLength = ContentTextBox.SelectionLength;
        var picker = new PromptReferenceWindow(_referenceItems, _editingId) { Owner = this };
        if (picker.ShowDialog() == true && picker.SelectedPrompt is { } selected)
        {
            ContentTextBox.Select(selectionStart, selectionLength);
            InsertContentToken("{{prompt:" + selected.Id + "}}");
        }
    }

    private void HandleContentTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        if (ReferenceStatusTextBlock is not null)
        {
            _referenceValidationTimer.Stop();
            _referenceValidationTimer.Start();
        }
    }

    private PromptContentResult ValidateReferences(string name, string content)
    {
        var draft = new PromptItem { Id = _editingId, Name = name, Content = content };
        var items = _referenceItems.Where(item => item.Id != _editingId).Append(draft);
        return new PromptReferenceService(items).Expand(draft);
    }

    private void UpdateReferenceStatus()
    {
        _referenceValidationTimer.Stop();
        var ids = PromptReferenceService.GetReferenceIds(ContentTextBox.Text);
        var result = ValidateReferences(NameTextBox.Text.Trim(), ContentTextBox.Text);
        var names = ids.Select(id => _referenceItems.FirstOrDefault(item => item.Id == id)?.Name
            ?? $"缺失指令（{id}）");
        ReferenceStatusTextBlock.Text = result.Success
            ? (ids.Count == 0 ? string.Empty : $"引用了：{string.Join("、", names)}")
            : result.Error!;
        ReferenceStatusTextBlock.ToolTip = ReferenceStatusTextBlock.Text;
        ReferenceStatusTextBlock.SetResourceReference(System.Windows.Controls.TextBlock.ForegroundProperty,
            result.Success ? "ThemeTextSecondaryBrush" : "ThemeDangerTextBrush");
        ReferenceStatusTextBlock.Visibility = ReferenceStatusTextBlock.Text.Length == 0
            ? Visibility.Collapsed : Visibility.Visible;
    }

    // !SECTION 正文编辑与引用

    // SECTION 保存与校验

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

        var validation = ValidateReferences(name, content);
        if (!validation.Success)
        {
            ShowValidationMessage(validation.Error!, ContentTextBox);
            return;
        }

        ResultItem = new PromptItem
        {
            Id = _editingId,
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

    // !SECTION 保存与校验
}
