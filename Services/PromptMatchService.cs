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
                    PromptMatchKind.AbbreviationPrefix));
                continue;
            }

            if (normalizedName.StartsWith(normalizedInput, StringComparison.Ordinal))
            {
                matches.Add(new PromptMatch(
                    item,
                    triggerText,
                    triggerText.Length,
                    90,
                    PromptMatchKind.NameStartsWith));
                continue;
            }

            if (normalizedName.Contains(normalizedInput, StringComparison.Ordinal))
            {
                matches.Add(new PromptMatch(
                    item,
                    triggerText,
                    triggerText.Length,
                    75,
                    PromptMatchKind.NameContains));
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
}
