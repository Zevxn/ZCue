using TypeSense.Models;

namespace TypeSense.Services;

/// <summary>
/// MVP 使用内置数据。下一阶段可以将这个服务替换为 JSON/SQLite 存储，调用方无需改变。
/// </summary>
public sealed class PromptCatalogService
{
    private readonly object _gate = new();
    private readonly List<PromptItem> _items =
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

    public void IncrementUsage(string id)
    {
        lock (_gate)
        {
            var item = _items.FirstOrDefault(candidate => candidate.Id == id);
            if (item is not null)
            {
                item.UsageCount++;
            }
        }
    }
}
