using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using TypeSense.Infrastructure;
using TypeSense.Models;
using TypeSense.Services;

namespace TypeSense.Views;

public partial class PromptExportWindow : Window
{
    private readonly List<ExportCategoryOption> _options = [];
    private bool _updatingSelection;

    // SECTION 分类选择与导出确认

    public PromptExportWindow(
        IReadOnlyList<PromptCategory> categories,
        IReadOnlyList<PromptItem> prompts)
    {
        InitializeComponent();
        AppThemeManager.TrackWindow(this);

        var promptCounts = prompts
            .GroupBy(prompt => prompt.CategoryId ?? string.Empty, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
        if (promptCounts.TryGetValue(string.Empty, out var uncategorizedCount))
        {
            AddOption(string.Empty, "无分类", uncategorizedCount);
        }

        foreach (var category in categories)
        {
            AddOption(
                category.Id,
                category.Name,
                promptCounts.GetValueOrDefault(category.Id));
        }

        CategoryOptionsList.ItemsSource = _options;
        UpdateSelectionState();
    }

    public IReadOnlyList<string> SelectedCategoryIds { get; private set; } = [];

    private void AddOption(string id, string name, int promptCount)
    {
        var option = new ExportCategoryOption(id, name, promptCount);
        option.PropertyChanged += HandleOptionPropertyChanged;
        _options.Add(option);
    }

    private void HandleSelectAllChanged(object sender, RoutedEventArgs e)
    {
        if (_updatingSelection || _options.Count == 0)
        {
            return;
        }

        var isSelected = _options.Any(option => !option.IsSelected);
        _updatingSelection = true;
        foreach (var option in _options)
        {
            option.IsSelected = isSelected;
        }

        _updatingSelection = false;
        UpdateSelectionState();
    }

    private void HandleOptionPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_updatingSelection)
        {
            UpdateSelectionState();
        }
    }

    private void UpdateSelectionState()
    {
        var selectedCount = _options.Count(option => option.IsSelected);
        _updatingSelection = true;
        SelectAllCheckBox.IsEnabled = _options.Count > 0;
        SelectAllCheckBox.IsChecked = selectedCount == 0
            ? false
            : selectedCount == _options.Count
                ? true
                : null;
        ConfirmExportButton.IsEnabled = selectedCount > 0;
        _updatingSelection = false;
    }

    private void HandleConfirmExportClick(object sender, RoutedEventArgs e)
    {
        SelectedCategoryIds = _options
            .Where(option => option.IsSelected)
            .Select(option => option.Id)
            .ToArray();
        DialogResult = SelectedCategoryIds.Count > 0;
    }

    private sealed class ExportCategoryOption(
        string id,
        string name,
        int promptCount) : INotifyPropertyChanged
    {
        private bool _isSelected = true;

        public event PropertyChangedEventHandler? PropertyChanged;

        public string Id { get; } = id;

        public string Name { get; } = name;

        public int PromptCount { get; } = promptCount;

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected == value)
                {
                    return;
                }

                _isSelected = value;
                OnPropertyChanged();
            }
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    // !SECTION 分类选择与导出确认
}
