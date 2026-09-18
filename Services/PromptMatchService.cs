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

        var matches = new List<PromptMatch>();

        foreach (var item in promptItems)
        {
            if (!item.Enabled)
            {
                continue;
            }

            PromptMatch? bestMatch = null;
            for (var start = 0; start < triggerText.Length; start++)
            {
                var candidateText = triggerText[start..];
                var candidateMatch = MatchSingle(item, candidateText);
                if (candidateMatch is not null
                    && (bestMatch is null || IsBetterMatch(candidateMatch, bestMatch)))
                {
                    bestMatch = candidateMatch;
                }
            }

            if (bestMatch is not null)
            {
                matches.Add(bestMatch);
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

    private static PromptMatch? MatchSingle(PromptItem item, string triggerText)
    {
        var normalizedInput = triggerText.Trim().ToLowerInvariant();
        if (normalizedInput.Length == 0)
        {
            return null;
        }

        var normalizedAbbreviation = item.Abbreviation.Trim().ToLowerInvariant();
        var normalizedName = item.Name.Trim().ToLowerInvariant();

        if (normalizedAbbreviation.StartsWith(normalizedInput, StringComparison.Ordinal))
        {
            var quality = normalizedAbbreviation.Length == normalizedInput.Length ? 120 : 100;
            return new PromptMatch(
                item,
                triggerText,
                triggerText.Length,
                quality,
                PromptMatchKind.AbbreviationPrefix,
                0,
                Math.Min(normalizedInput.Length, item.Name.Length));
        }

        if (normalizedName.StartsWith(normalizedInput, StringComparison.Ordinal))
        {
            var highlightStart = item.Name.IndexOf(triggerText.Trim(), StringComparison.OrdinalIgnoreCase);
            return new PromptMatch(
                item,
                triggerText,
                triggerText.Length,
                90,
                PromptMatchKind.NameStartsWith,
                Math.Max(0, highlightStart),
                normalizedInput.Length);
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
            return new PromptMatch(
                item,
                triggerText,
                triggerText.Length,
                80,
                PromptMatchKind.AbbreviationContains,
                highlight.Start,
                highlight.Length);
        }

        if (normalizedName.Contains(normalizedInput, StringComparison.Ordinal))
        {
            var highlightStart = item.Name.IndexOf(triggerText.Trim(), StringComparison.OrdinalIgnoreCase);
            return new PromptMatch(
                item,
                triggerText,
                triggerText.Length,
                75,
                PromptMatchKind.NameContains,
                Math.Max(0, highlightStart),
                normalizedInput.Length);
        }

        return null;
    }

    private static bool IsBetterMatch(PromptMatch candidate, PromptMatch current)
    {
        return candidate.QualityScore > current.QualityScore
            || candidate.QualityScore == current.QualityScore
                && candidate.MatchLength > current.MatchLength;
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
