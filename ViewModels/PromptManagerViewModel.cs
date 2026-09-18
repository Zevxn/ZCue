using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Data;
using TypeSense.Models;
using TypeSense.Services;

namespace TypeSense.ViewModels;

public sealed class PromptManagerViewModel : INotifyPropertyChanged
{
    private readonly PromptCatalogService _catalog;
    private string _searchText = string.Empty;
    private PromptItem? _selectedPrompt;

    public PromptManagerViewModel(PromptCatalogService catalog)
    {
        _catalog = catalog;
        Prompts = new ObservableCollection<PromptItem>(_catalog.GetAllItems());
        FilteredPrompts = CollectionViewSource.GetDefaultView(Prompts);
        FilteredPrompts.Filter = FilterPrompt;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // SECTION 列表状态与筛选

    public ObservableCollection<PromptItem> Prompts { get; }

    public ICollectionView FilteredPrompts { get; }

    public PromptItem? SelectedPrompt
    {
        get => _selectedPrompt;
        set
        {
            if (ReferenceEquals(_selectedPrompt, value))
            {
                return;
            }

            _selectedPrompt = value;
            OnPropertyChanged();
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (string.Equals(_searchText, value, StringComparison.Ordinal))
            {
                return;
            }

            _searchText = value ?? string.Empty;
            FilteredPrompts.Refresh();
            OnPropertyChanged();
        }
    }

    public int TotalCount => Prompts.Count;

    public int EnabledCount => Prompts.Count(prompt => prompt.Enabled);

    // !SECTION 列表状态与筛选

    // SECTION 指令变更

    public void Add(PromptItem item)
    {
        var savedItem = _catalog.Add(item);
        Reload(savedItem.Id);
    }

    public bool Update(PromptItem item)
    {
        var updated = _catalog.Update(item);
        if (updated)
        {
            Reload(item.Id);
        }

        return updated;
    }

    public bool Delete(string id)
    {
        var deleted = _catalog.Delete(id);
        if (deleted)
        {
            Reload(null);
        }

        return deleted;
    }

    public bool SetEnabled(string id, bool enabled)
    {
        var updated = _catalog.SetEnabled(id, enabled);
        if (updated)
        {
            Reload(id);
        }

        return updated;
    }

    public void Reload(string? selectedId)
    {
        Prompts.Clear();
        foreach (var prompt in _catalog.GetAllItems())
        {
            Prompts.Add(prompt);
        }

        SelectedPrompt = selectedId is null
            ? null
            : Prompts.FirstOrDefault(prompt => prompt.Id == selectedId);
        FilteredPrompts.Refresh();
        OnPropertyChanged(nameof(TotalCount));
        OnPropertyChanged(nameof(EnabledCount));
    }

    // !SECTION 指令变更

    // SECTION 过滤逻辑

    private bool FilterPrompt(object value)
    {
        if (value is not PromptItem prompt)
        {
            return false;
        }

        var filter = SearchText.Trim();
        if (filter.Length == 0)
        {
            return true;
        }

        return prompt.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || prompt.Abbreviation.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || prompt.Content.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    // !SECTION 过滤逻辑

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
