using TypeSense.Models;

namespace TypeSense.Services;

public sealed class PromptMatchService
{
    public IReadOnlyList<PromptMatch> Match(
        string triggerText,
        IEnumerable<PromptItem> promptItems,
        int maxResults = 9)
    {
        if (string.IsNullOrWhiteSpace(triggerText) || maxResults <= 0)
        {
            return Array.Empty<PromptMatch>();
        }

        var normalizedInput = triggerText.Trim().ToLowerInvariant();
        var matches = new List<PromptMatch>();

        foreach (var item in promptItems)
        {
            if (!item.Enabled)
            {
                continue;
            }

            var normalizedAbbreviation = item.Abbreviation.Trim().ToLowerInvariant();
            var normalizedName = item.Name.Trim().ToLowerInvariant();

            if (normalizedAbbreviation.StartsWith(normalizedInput, StringComparison.Ordinal))
            {
                var quality = normalizedAbbreviation.Length == normalizedInput.Length ? 120 : 100;
                matches.Add(new PromptMatch(
                    item,
                    triggerText,
                    triggerText.Length,
                    quality,
                    PromptMatchKind.AbbreviationPrefix,
                    0,
                    Math.Min(triggerText.Length, item.Name.Length)));
                continue;
            }

            if (normalizedName.StartsWith(normalizedInput, StringComparison.Ordinal))
            {
                var highlightStart = item.Name.IndexOf(triggerText.Trim(), StringComparison.OrdinalIgnoreCase);
                matches.Add(new PromptMatch(
                    item,
                    triggerText,
                    triggerText.Length,
                    90,
                    PromptMatchKind.NameStartsWith,
                    Math.Max(0, highlightStart),
                    normalizedInput.Length));
                continue;
            }

            var abbreviationMatchStart = normalizedAbbreviation.IndexOf(
                normalizedInput,
                StringComparison.Ordinal);
            if (abbreviationMatchStart > 0)
            {
                var highlight = MapAbbreviationRangeToName(
                    item.Name,
                    normalizedAbbreviation.Length,
                    abbreviationMatchStart,
                    normalizedInput.Length);
                matches.Add(new PromptMatch(
                    item,
                    triggerText,
                    triggerText.Length,
                    80,
                    PromptMatchKind.AbbreviationContains,
                    highlight.Start,
                    highlight.Length));
                continue;
            }

            if (normalizedName.Contains(normalizedInput, StringComparison.Ordinal))
            {
                var highlightStart = item.Name.IndexOf(triggerText.Trim(), StringComparison.OrdinalIgnoreCase);
                matches.Add(new PromptMatch(
                    item,
                    triggerText,
                    triggerText.Length,
                    75,
                    PromptMatchKind.NameContains,
                    Math.Max(0, highlightStart),
                    normalizedInput.Length));
            }
        }

        return matches
            .OrderByDescending(match => match.QualityScore)
            .ThenByDescending(match => match.Item.UsageCount)
            .ThenByDescending(match => match.MatchLength)
            .ThenBy(match => match.Item.Name.Length)
            .ThenBy(match => match.Item.Name, StringComparer.OrdinalIgnoreCase)
            .Take(maxResults)
            .ToArray();
    }

    private static (int Start, int Length) MapAbbreviationRangeToName(
        string name,
        int abbreviationLength,
        int matchStart,
        int matchLength)
    {
        if (string.IsNullOrEmpty(name) || abbreviationLength <= 0)
        {
            return (0, 0);
        }

        if (name.Length == abbreviationLength)
        {
            var directStart = Math.Clamp(matchStart, 0, name.Length);
            var directLength = Math.Min(matchLength, name.Length - directStart);
            return (directStart, directLength);
        }

        var start = (int)Math.Floor((double)matchStart / abbreviationLength * name.Length);
        var end = (int)Math.Ceiling(
            (double)(matchStart + matchLength) / abbreviationLength * name.Length);
        start = Math.Clamp(start, 0, name.Length);
        end = Math.Clamp(Math.Max(start + 1, end), start, name.Length);
        return (start, end - start);
    }
}
