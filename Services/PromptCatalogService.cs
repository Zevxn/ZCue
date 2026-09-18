using TypeSense.Models;

namespace TypeSense.Services;

public sealed class PromptCatalogService
{
    private readonly object _gate = new();
    private readonly PromptStorageService _storage;
    private readonly List<PromptItem> _items;

    // SECTION 初始化与查询

    public PromptCatalogService(PromptStorageService? storage = null)
    {
        _storage = storage ?? new PromptStorageService();
        var loadedItems = _storage.Load();
        _items = loadedItems is null
            ? CreateDefaultItems()
            : loadedItems
                .Where(item => !string.IsNullOrWhiteSpace(item.Name)
                    && !string.IsNullOrWhiteSpace(item.Abbreviation)
                    && !string.IsNullOrWhiteSpace(item.Content))
                .Select(Normalize)
                .ToList();

        if (loadedItems is null)
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
        _storage.Save(_items);
    }

    private static List<PromptItem> CreateDefaultItems() =>
    [
        new PromptItem
        {
            Id = "mvp-polish",
            Name = "中文润色",
            Abbreviation = "zwrs",
            Content = "请对下面这段文字进行学术化润色，保持原意并改善逻辑与表达。",
            UsageCount = 0,
            Enabled = true
        },
        new PromptItem
        {
            Id = "mvp-rewrite",
            Name = "中文改写",
            Abbreviation = "zwgx",
            Content = "请在保持原意的基础上改写下面这段文字，使表达更加清晰、自然、准确。",
            UsageCount = 0,
            Enabled = true
        },
        new PromptItem
        {
            Id = "mvp-translate",
            Name = "中文翻译",
            Abbreviation = "zwfy",
            Content = "请将下面这段文字准确翻译成英文，并保持术语和语气的一致性。",
            UsageCount = 0,
            Enabled = true
        }
    ];

    private static PromptItem Normalize(PromptItem item) => new()
    {
        Id = string.IsNullOrWhiteSpace(item.Id) ? Guid.NewGuid().ToString("N") : item.Id,
        Name = item.Name?.Trim() ?? string.Empty,
        Abbreviation = item.Abbreviation?.Trim() ?? string.Empty,
        Content = item.Content ?? string.Empty,
        UsageCount = Math.Max(0, item.UsageCount),
        Enabled = item.Enabled
    };

    // !SECTION 修改与持久化
}
