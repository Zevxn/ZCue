namespace TypeSense.Models;

public sealed class PromptItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    public string Name { get; set; } = string.Empty;

    public string Abbreviation { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

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
        Abbreviation = Abbreviation,
        Content = Content,
        UsageCount = UsageCount,
        Enabled = Enabled
    };
}

public enum PromptMatchKind
{
    AbbreviationPrefix,
    AbbreviationContains,
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
