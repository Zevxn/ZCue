namespace ZCue.Models;

public enum PromptAliasKind
{
    FullPinyin,
    Initials
}

public sealed record PromptAliasSegment(
    int SearchStart,
    int SearchLength,
    int NameStart,
    int NameLength,
    bool IsPrimary = true);

public sealed record PromptAlias(
    string Text,
    string SearchText,
    PromptAliasKind Kind,
    List<PromptAliasSegment> Segments,
    bool IsPrimary = true)
{
    // SECTION 高亮映射

    public (int Start, int Length) MapToName(int searchStart, int searchLength, int nameLength)
    {
        if (nameLength <= 0 || searchLength <= 0)
        {
            return (0, 0);
        }

        var searchEnd = searchStart + searchLength;
        var overlappingSegments = Segments
            .Where(segment => segment.SearchStart < searchEnd
                && segment.SearchStart + segment.SearchLength > searchStart)
            .ToArray();

        if (overlappingSegments.Length == 0)
        {
            return (0, 0);
        }

        var start = overlappingSegments.Min(segment => segment.NameStart);
        var end = overlappingSegments.Max(segment => segment.NameStart + segment.NameLength);
        start = Math.Clamp(start, 0, nameLength);
        end = Math.Clamp(Math.Max(start + 1, end), start, nameLength);
        return (start, end - start);
    }

    // !SECTION 高亮映射

    public PromptAlias DeepCopy() => new(
        Text,
        SearchText,
        Kind,
        Segments.Select(segment => segment with { }).ToList(),
        IsPrimary);
}
