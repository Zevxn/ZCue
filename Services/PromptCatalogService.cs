using TypeSense.Models;

namespace TypeSense.Services;

public sealed class PromptCatalogService
{
    private readonly object _gate = new();
    private readonly PromptStorageService _storage;
    private readonly PinyinAliasService _pinyinAliasService = new();
    private readonly List<PromptItem> _items;
    private readonly List<PromptCategory> _categories;

    // SECTION 初始化与查询

    public PromptCatalogService(PromptStorageService? storage = null)
    {
        _storage = storage ?? new PromptStorageService();
        var loadedData = _storage.Load();
        var storedCategories = loadedData?.Categories ?? [];
        _categories = storedCategories
            .Where(category => category is not null
                && !string.IsNullOrWhiteSpace(category.Id)
                && !string.IsNullOrWhiteSpace(category.Name))
            .Select(NormalizeCategory)
            .DistinctBy(category => category.Id, StringComparer.Ordinal)
            .ToList() ?? [];
        _items = loadedData is null
            ? CreateDefaultItems()
            : loadedData.Prompts
                .Where(item => item is not null
                    && !string.IsNullOrWhiteSpace(item.Name)
                    && !string.IsNullOrWhiteSpace(item.Content))
                .Select(Normalize)
                .ToList();

        var aliasesChanged = false;
        var promptCategoriesChanged = false;
        var categoriesChanged = loadedData is not null
            && (storedCategories.Count != _categories.Count
                || storedCategories.Where((category, index) => category is null
                    || category.Id != _categories[index].Id
                    || category.Name != _categories[index].Name).Any());
        var validCategoryIds = _categories.Select(category => category.Id)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var item in _items)
        {
            if (!string.IsNullOrEmpty(item.CategoryId)
                && !validCategoryIds.Contains(item.CategoryId))
            {
                item.CategoryId = string.Empty;
                promptCategoriesChanged = true;
            }

            if (item.PinyinAliases.Count == 0)
            {
                aliasesChanged |= _pinyinAliasService.RefreshAliases(item);
            }
        }

        if (loadedData is null
            || categoriesChanged
            || aliasesChanged
            || promptCategoriesChanged)
        {
            PersistLocked();
        }
    }

    public IReadOnlyList<PromptItem> GetEnabledItems()
    {
        lock (_gate)
        {
            return _items.Where(item => item.Enabled).Select(item => item.Clone()).ToArray();
        }
    }

    public IReadOnlyList<PromptItem> GetAllItems()
    {
        lock (_gate)
        {
            return _items.Select(item => item.Clone()).ToArray();
        }
    }

    public IReadOnlyList<PromptCategory> GetAllCategories()
    {
        lock (_gate)
        {
            return _categories.Select(category => category.Clone()).ToArray();
        }
    }

    // !SECTION 初始化与查询

    // SECTION 修改与持久化

    public PromptItem Add(PromptItem item)
    {
        lock (_gate)
        {
            var copy = Normalize(item);
            if (_items.Any(existing => existing.Id == copy.Id))
            {
                copy.Id = Guid.NewGuid().ToString("N");
            }

            _pinyinAliasService.RefreshAliases(copy);
            _items.Add(copy);
            PersistLocked();
            return copy.Clone();
        }
    }

    public bool Update(PromptItem item)
    {
        lock (_gate)
        {
            var index = _items.FindIndex(existing => existing.Id == item.Id);
            if (index < 0)
            {
                return false;
            }

            var copy = Normalize(item);
            copy.Id = _items[index].Id;
            _pinyinAliasService.RefreshAliases(copy);
            _items[index] = copy;
            PersistLocked();
            return true;
        }
    }

    public bool Delete(string id)
    {
        lock (_gate)
        {
            var removed = _items.RemoveAll(item => item.Id == id) > 0;
            if (removed)
            {
                PersistLocked();
            }

            return removed;
        }
    }

    public ImportResult Import(
        IReadOnlyList<PromptItem> prompts,
        IReadOnlyList<PromptCategory> categories)
    {
        ArgumentNullException.ThrowIfNull(prompts);
        ArgumentNullException.ThrowIfNull(categories);

        lock (_gate)
        {
            var categoryIdMap = new Dictionary<string, string>(StringComparer.Ordinal);
            var addedCategoryCount = 0;
            var mergedCategoryCount = 0;
            var skippedCategoryCount = 0;

            foreach (var category in categories)
            {
                if (category is null)
                {
                    skippedCategoryCount++;
                    continue;
                }

                var sourceId = category.Id?.Trim() ?? string.Empty;
                var name = category.Name?.Trim() ?? string.Empty;
                if (sourceId.Length > 0 && categoryIdMap.ContainsKey(sourceId))
                {
                    skippedCategoryCount++;
                    continue;
                }

                if (name.Length == 0)
                {
                    if (sourceId.Length > 0)
                    {
                        categoryIdMap[sourceId] = string.Empty;
                    }

                    skippedCategoryCount++;
                    continue;
                }

                var targetCategory = _categories.FirstOrDefault(existing =>
                    string.Equals(existing.Name, name, StringComparison.OrdinalIgnoreCase));
                if (targetCategory is null && sourceId.Length > 0)
                {
                    targetCategory = _categories.FirstOrDefault(existing =>
                        string.Equals(existing.Id, sourceId, StringComparison.Ordinal)
                        && string.Equals(existing.Name, name, StringComparison.OrdinalIgnoreCase));
                }

                if (targetCategory is not null)
                {
                    mergedCategoryCount++;
                }
                else
                {
                    var targetId = sourceId;
                    if (targetId.Length == 0
                        || _categories.Any(existing => string.Equals(
                            existing.Id,
                            targetId,
                            StringComparison.Ordinal)))
                    {
                        targetId = $"cat_{Guid.NewGuid():N}";
                    }

                    targetCategory = new PromptCategory
                    {
                        Id = targetId,
                        Name = name
                    };
                    _categories.Add(targetCategory);
                    addedCategoryCount++;
                }

                if (sourceId.Length > 0)
                {
                    categoryIdMap[sourceId] = targetCategory.Id;
                }
            }

            var promptIds = _items.Select(item => item.Id)
                .ToHashSet(StringComparer.Ordinal);
            var addedPromptCount = 0;
            var skippedPromptCount = 0;
            foreach (var prompt in prompts)
            {
                if (prompt is null
                    || string.IsNullOrWhiteSpace(prompt.Name)
                    || string.IsNullOrWhiteSpace(prompt.Content))
                {
                    skippedPromptCount++;
                    continue;
                }

                var copy = Normalize(prompt);
                if (!promptIds.Add(copy.Id))
                {
                    skippedPromptCount++;
                    continue;
                }

                var sourceCategoryId = prompt.CategoryId?.Trim() ?? string.Empty;
                if (sourceCategoryId.Length == 0)
                {
                    copy.CategoryId = string.Empty;
                }
                else if (categoryIdMap.TryGetValue(sourceCategoryId, out var mappedCategoryId))
                {
                    copy.CategoryId = mappedCategoryId;
                }
                else if (_categories.Any(category => string.Equals(
                    category.Id,
                    sourceCategoryId,
                    StringComparison.Ordinal)))
                {
                    copy.CategoryId = sourceCategoryId;
                }
                else
                {
                    copy.CategoryId = string.Empty;
                }

                if (copy.PinyinAliases.Count == 0)
                {
                    _pinyinAliasService.RefreshAliases(copy);
                }

                _items.Add(copy);
                addedPromptCount++;
            }

            if (addedPromptCount > 0 || addedCategoryCount > 0)
            {
                PersistLocked();
            }

            return new ImportResult(
                addedPromptCount,
                addedCategoryCount,
                mergedCategoryCount,
                skippedPromptCount,
                skippedCategoryCount);
        }
    }

    public sealed record ImportResult(
        int AddedPromptCount,
        int AddedCategoryCount,
        int MergedCategoryCount,
        int SkippedPromptCount,
        int SkippedCategoryCount);

    public bool ReorderItems(IReadOnlyList<string> orderedIds)
    {
        lock (_gate)
        {
            if (orderedIds.Count != _items.Count
                || orderedIds.Distinct(StringComparer.Ordinal).Count() != _items.Count
                || orderedIds.Any(id => !_items.Any(item => item.Id == id)))
            {
                return false;
            }

            var orderedItems = orderedIds
                .Select(id => _items.First(item => item.Id == id))
                .ToArray();
            if (_items.SequenceEqual(orderedItems))
            {
                return false;
            }

            _items.Clear();
            _items.AddRange(orderedItems);
            PersistLocked();
            return true;
        }
    }

    // SECTION 分类管理

    public PromptCategory? AddCategory(string name)
    {
        var trimmedName = name?.Trim() ?? string.Empty;
        if (trimmedName.Length == 0)
        {
            return null;
        }

        lock (_gate)
        {
            var category = new PromptCategory
            {
                Id = $"cat_{Guid.NewGuid():N}",
                Name = trimmedName
            };
            _categories.Add(category);
            PersistLocked();
            return category.Clone();
        }
    }

    public bool RenameCategory(string id, string name)
    {
        var trimmedName = name?.Trim() ?? string.Empty;
        if (trimmedName.Length == 0)
        {
            return false;
        }

        lock (_gate)
        {
            var category = _categories.FirstOrDefault(candidate => candidate.Id == id);
            if (category is null || category.Name == trimmedName)
            {
                return false;
            }

            category.Name = trimmedName;
            PersistLocked();
            return true;
        }
    }

    public bool DeleteCategory(string id)
    {
        lock (_gate)
        {
            var removed = _categories.RemoveAll(category => category.Id == id) > 0;
            if (!removed)
            {
                return false;
            }

            foreach (var item in _items.Where(item => item.CategoryId == id))
            {
                item.CategoryId = string.Empty;
            }

            PersistLocked();
            return true;
        }
    }

    public bool AssignCategory(string promptId, string? categoryId)
    {
        var normalizedCategoryId = categoryId ?? string.Empty;
        lock (_gate)
        {
            if (normalizedCategoryId.Length > 0
                && !_categories.Any(category => category.Id == normalizedCategoryId))
            {
                return false;
            }

            var item = _items.FirstOrDefault(candidate => candidate.Id == promptId);
            if (item is null || item.CategoryId == normalizedCategoryId)
            {
                return false;
            }

            item.CategoryId = normalizedCategoryId;
            PersistLocked();
            return true;
        }
    }

    public bool ReorderCategories(IReadOnlyList<string> orderedIds)
    {
        lock (_gate)
        {
            if (orderedIds.Count != _categories.Count
                || orderedIds.Distinct(StringComparer.Ordinal).Count() != _categories.Count
                || orderedIds.Any(id => !_categories.Any(category => category.Id == id)))
            {
                return false;
            }

            var orderedCategories = orderedIds
                .Select(id => _categories.First(category => category.Id == id))
                .ToArray();
            if (_categories.SequenceEqual(orderedCategories))
            {
                return false;
            }

            _categories.Clear();
            _categories.AddRange(orderedCategories);
            PersistLocked();
            return true;
        }
    }

    // !SECTION 分类管理

    public bool SetEnabled(string id, bool enabled)
    {
        lock (_gate)
        {
            var item = _items.FirstOrDefault(candidate => candidate.Id == id);
            if (item is null)
            {
                return false;
            }

            item.Enabled = enabled;
            PersistLocked();
            return true;
        }
    }

    public void IncrementUsage(string id)
    {
        lock (_gate)
        {
            var item = _items.FirstOrDefault(candidate => candidate.Id == id);
            if (item is not null)
            {
                item.UsageCount++;
                PersistLocked();
            }
        }
    }

    private void PersistLocked()
    {
        _storage.Save(_items, _categories);
    }

    private static List<PromptItem> CreateDefaultItems() =>
    [
        new PromptItem
        {
            Id = "mvp-polish",
            Name = "中文润色",
            Content = "请对下面这段文字进行学术化润色，保持原意并改善逻辑与表达。",
            UsageCount = 0,
            Enabled = true
        },
        new PromptItem
        {
            Id = "mvp-rewrite",
            Name = "中文改写",
            Content = "请在保持原意的基础上改写下面这段文字，使表达更加清晰、自然、准确。",
            UsageCount = 0,
            Enabled = true
        },
        new PromptItem
        {
            Id = "mvp-translate",
            Name = "中文翻译",
            Content = "请将下面这段文字准确翻译成英文，并保持术语和语气的一致性。",
            UsageCount = 0,
            Enabled = true
        }
    ];

    private static PromptItem Normalize(PromptItem item) => new()
    {
        Id = string.IsNullOrWhiteSpace(item.Id) ? Guid.NewGuid().ToString("N") : item.Id,
        Name = item.Name?.Trim() ?? string.Empty,
        Content = item.Content ?? string.Empty,
        CategoryId = item.CategoryId ?? string.Empty,
        PinyinAliases = item.PinyinAliases?
            .Select(alias => alias.DeepCopy())
            .ToList() ?? [],
        UsageCount = Math.Max(0, item.UsageCount),
        Enabled = item.Enabled
    };

    private static PromptCategory NormalizeCategory(PromptCategory category) => new()
    {
        Id = category.Id.Trim(),
        Name = category.Name.Trim()
    };

    // !SECTION 修改与持久化
}
