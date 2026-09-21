using System.Text.Json.Serialization;

namespace ZCue.Models;

public sealed class PromptItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public string CategoryId { get; set; } = string.Empty;

    [JsonIgnore]
    public string CategoryName { get; set; } = string.Empty;

    public List<PromptAlias> PinyinAliases { get; set; } = [];

    public string Preview => Content
        .Replace('\r', ' ')
        .Replace('\n', ' ')
        .Trim();

    public int UsageCount { get; set; }

    public bool Enabled { get; set; } = true;

    public PromptItem Clone() => new()
    {
        Id = Id,
        Name = Name,
        Content = Content,
        CategoryId = CategoryId,
        PinyinAliases = PinyinAliases?
            .Select(alias => alias.DeepCopy())
            .ToList() ?? [],
        UsageCount = UsageCount,
        Enabled = Enabled
    };
}

public enum PromptMatchKind
{
    PinyinFull,
    PinyinInitial,
    NameStartsWith,
    NameContains
}

public sealed record PromptMatch(
    PromptItem Item,
    string TriggerText,
    int MatchLength,
    int QualityScore,
    PromptMatchKind MatchKind,
    int HighlightStart,
    int HighlightLength);
