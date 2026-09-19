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
    private string _selectedCategoryId = "all";
    private PromptItem? _selectedPrompt;

    public PromptManagerViewModel(PromptCatalogService catalog)
    {
        _catalog = catalog;
        Prompts = new ObservableCollection<PromptItem>(_catalog.GetAllItems());
        Categories = new ObservableCollection<PromptCategory>(_catalog.GetAllCategories());
        UpdateCategoryNames();
        FilteredPrompts = CollectionViewSource.GetDefaultView(Prompts);
        FilteredPrompts.Filter = FilterPrompt;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    // SECTION 列表状态与筛选

    public ObservableCollection<PromptItem> Prompts { get; }

    public ObservableCollection<PromptCategory> Categories { get; }

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

    public string SelectedCategoryId
    {
        get => _selectedCategoryId;
        set
        {
            if (string.Equals(_selectedCategoryId, value, StringComparison.Ordinal))
            {
                return;
            }

            _selectedCategoryId = value ?? "all";
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

    public int DeleteMany(IReadOnlyCollection<string> ids)
    {
        var deletedCount = _catalog.DeleteMany(ids);
        if (deletedCount > 0)
        {
            Reload(null);
        }

        return deletedCount;
    }

    public PromptCatalogService.ImportResult Import(
        IReadOnlyList<PromptItem> prompts,
        IReadOnlyList<PromptCategory> categories)
    {
        var result = _catalog.Import(prompts, categories);
        if (result.AddedPromptCount > 0 || result.AddedCategoryCount > 0)
        {
            Reload(null);
        }

        return result;
    }

    public bool MovePrompt(string draggedId, string targetId, bool insertAfter)
    {
        if (string.Equals(draggedId, targetId, StringComparison.Ordinal))
        {
            return false;
        }

        var visiblePrompts = FilteredPrompts.Cast<PromptItem>().ToList();
        var draggedVisibleIndex = visiblePrompts.FindIndex(prompt => prompt.Id == draggedId);
        var targetVisibleIndex = visiblePrompts.FindIndex(prompt => prompt.Id == targetId);
        if (draggedVisibleIndex < 0
            || targetVisibleIndex < 0
            || (!insertAfter && draggedVisibleIndex + 1 == targetVisibleIndex)
            || (insertAfter && draggedVisibleIndex == targetVisibleIndex + 1))
        {
            return false;
        }

        var draggedPrompt = Prompts.FirstOrDefault(prompt => prompt.Id == draggedId);
        var targetPrompt = Prompts.FirstOrDefault(prompt => prompt.Id == targetId);
        if (draggedPrompt is null || targetPrompt is null)
        {
            return false;
        }

        var draggedIndex = Prompts.IndexOf(draggedPrompt);
        Prompts.Remove(draggedPrompt);
        var targetIndex = Prompts.IndexOf(targetPrompt);
        if (targetIndex < 0)
        {
            Prompts.Insert(Math.Min(draggedIndex, Prompts.Count), draggedPrompt);
            return false;
        }

        Prompts.Insert(targetIndex + (insertAfter ? 1 : 0), draggedPrompt);
        return true;
    }

    public void RestorePromptOrder(IReadOnlyList<string> orderedIds)
    {
        if (orderedIds.Count != Prompts.Count
            || orderedIds.Distinct(StringComparer.Ordinal).Count() != Prompts.Count
            || orderedIds.Any(id => !Prompts.Any(prompt => prompt.Id == id)))
        {
            return;
        }

        for (var targetIndex = 0; targetIndex < orderedIds.Count; targetIndex++)
        {
            var prompt = Prompts.First(candidate => candidate.Id == orderedIds[targetIndex]);
            var currentIndex = Prompts.IndexOf(prompt);
            if (currentIndex != targetIndex)
            {
                Prompts.Move(currentIndex, targetIndex);
            }
        }
    }

    public bool ReorderPrompts(IReadOnlyList<string> orderedIds) =>
        _catalog.ReorderItems(orderedIds);

    public bool SetEnabled(string id, bool enabled)
    {
        var updated = _catalog.SetEnabled(id, enabled);
        if (updated)
        {
            Reload(id);
        }

        return updated;
    }

    // SECTION 分类变更

    public PromptCategory? AddCategory(string name)
    {
        var category = _catalog.AddCategory(name);
        if (category is not null)
        {
            Reload(SelectedPrompt?.Id);
        }

        return category;
    }

    public bool RenameCategory(string id, string name)
    {
        if (!_catalog.RenameCategory(id, name))
        {
            return false;
        }

        Reload(SelectedPrompt?.Id);
        return true;
    }

    public bool DeleteCategory(string id)
    {
        if (!_catalog.DeleteCategory(id))
        {
            return false;
        }

        if (SelectedCategoryId == id)
        {
            SelectedCategoryId = "all";
        }

        Reload(SelectedPrompt?.Id);
        return true;
    }

    public bool AssignCategory(string promptId, string categoryId)
    {
        if (!_catalog.AssignCategory(promptId, categoryId))
        {
            return false;
        }

        Reload(SelectedPrompt?.Id);
        return true;
    }

    public int AssignCategory(IReadOnlyCollection<string> promptIds, string categoryId)
    {
        var changedCount = _catalog.AssignCategory(promptIds, categoryId);
        if (changedCount > 0)
        {
            Reload(null);
        }

        return changedCount;
    }

    public bool ReorderCategories(IReadOnlyList<string> orderedIds)
    {
        if (!_catalog.ReorderCategories(orderedIds))
        {
            return false;
        }

        for (var targetIndex = 0; targetIndex < orderedIds.Count; targetIndex++)
        {
            var category = Categories.First(candidate => candidate.Id == orderedIds[targetIndex]);
            var currentIndex = Categories.IndexOf(category);
            if (currentIndex != targetIndex)
            {
                Categories.Move(currentIndex, targetIndex);
            }
        }

        return true;
    }

    // !SECTION 分类变更

    public void Reload(string? selectedId)
    {
        Categories.Clear();
        foreach (var category in _catalog.GetAllCategories())
        {
            Categories.Add(category);
        }

        if (SelectedCategoryId != "all"
            && SelectedCategoryId.Length > 0
            && !Categories.Any(category => category.Id == SelectedCategoryId))
        {
            SelectedCategoryId = "all";
        }

        Prompts.Clear();
        foreach (var prompt in _catalog.GetAllItems())
        {
            Prompts.Add(prompt);
        }

        UpdateCategoryNames();

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

        if (SelectedCategoryId != "all"
            && !string.Equals(prompt.CategoryId, SelectedCategoryId, StringComparison.Ordinal))
        {
            return false;
        }

        var filter = SearchText.Trim();
        if (filter.Length == 0)
        {
            return true;
        }

        return prompt.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || prompt.Content.Contains(filter, StringComparison.OrdinalIgnoreCase);
    }

    // !SECTION 过滤逻辑

    // SECTION 分类显示信息

    private void UpdateCategoryNames()
    {
        var categoryNames = Categories.ToDictionary(
            category => category.Id,
            category => category.Name,
            StringComparer.Ordinal);
        foreach (var prompt in Prompts)
        {
            prompt.CategoryName = categoryNames.GetValueOrDefault(prompt.CategoryId, string.Empty);
        }
    }

    // !SECTION 分类显示信息

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
