using TypeSense.Models;

namespace TypeSense.Services;

public sealed class PromptMatchService
{
    private readonly record struct AliasSearchMatch(
        int SearchStart,
        int SearchLength,
        bool IsExact,
        bool UsedAlternateInitial);

    private readonly PinyinAliasService _aliasService = new();

    public IReadOnlyList<PromptMatch> Match(
        string triggerText,
        IEnumerable<PromptItem> promptItems,
        int maxResults = 9)
    {
        if (string.IsNullOrWhiteSpace(triggerText) || maxResults <= 0)
        {
            return Array.Empty<PromptMatch>();
        }

        var minimumMatchLength = GetMinimumMatchLength(triggerText);
        if (triggerText.Length < minimumMatchLength)
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

            var aliases = _aliasService.GetAliases(item);
            PromptMatch? bestMatch = null;
            for (var start = 0; start <= triggerText.Length - minimumMatchLength; start++)
            {
                var candidateText = triggerText[start..];
                var candidateMatch = MatchSingle(item, candidateText, aliases);
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

    // SECTION 别名匹配与排序

    private static PromptMatch? MatchSingle(
        PromptItem item,
        string triggerText,
        IReadOnlyList<PromptAlias> aliases)
    {
        var normalizedInput = triggerText.Trim().ToLowerInvariant();
        if (normalizedInput.Length == 0)
        {
            return null;
        }

        PromptMatch? bestMatch = null;
        foreach (var alias in aliases)
        {
            var input = alias.Kind == PromptAliasKind.FullPinyin
                ? PinyinAliasService.NormalizeInput(normalizedInput)
                : normalizedInput;
            if (input.Length == 0)
            {
                continue;
            }

            var aliasMatch = alias.Kind == PromptAliasKind.FullPinyin
                ? FindFullPinyinMatch(alias, input)
                : FindSubstringMatch(alias, input);
            if (aliasMatch is null)
            {
                continue;
            }

            if (alias.Kind == PromptAliasKind.FullPinyin
                && !alias.IsPrimary
                && aliasMatch.Value.UsedAlternateInitial)
            {
                continue;
            }

            var quality = GetAliasQuality(
                alias,
                aliasMatch.Value.SearchStart,
                aliasMatch.Value.IsExact);
            var highlight = alias.MapToName(
                aliasMatch.Value.SearchStart,
                aliasMatch.Value.SearchLength,
                item.Name.Length);
            var match = new PromptMatch(
                item,
                triggerText,
                triggerText.Length,
                quality,
                ToMatchKind(alias.Kind, aliasMatch.Value.SearchStart),
                highlight.Start,
                highlight.Length);
            if (bestMatch is null || IsBetterMatch(match, bestMatch))
            {
                bestMatch = match;
            }
        }

        var normalizedName = item.Name.Trim().ToLowerInvariant();
        var nameStart = normalizedName.IndexOf(normalizedInput, StringComparison.Ordinal);
        if (nameStart >= 0)
        {
            var nameMatch = new PromptMatch(
                item,
                triggerText,
                triggerText.Length,
                nameStart == 0 ? 90 : 75,
                nameStart == 0 ? PromptMatchKind.NameStartsWith : PromptMatchKind.NameContains,
                nameStart,
                normalizedInput.Length);
            if (bestMatch is null || IsBetterMatch(nameMatch, bestMatch))
            {
                bestMatch = nameMatch;
            }
        }

        return bestMatch;
    }

    private static AliasSearchMatch? FindSubstringMatch(PromptAlias alias, string input)
    {
        var aliasStart = alias.SearchText.IndexOf(input, StringComparison.Ordinal);
        return aliasStart < 0
            ? null
            : new AliasSearchMatch(
                aliasStart,
                input.Length,
                aliasStart == 0 && input.Length == alias.SearchText.Length,
                false);
    }

    private static AliasSearchMatch? FindFullPinyinMatch(PromptAlias alias, string input)
    {
        for (var startSegmentIndex = 0; startSegmentIndex < alias.Segments.Count; startSegmentIndex++)
        {
            var remaining = input;
            var matchStart = alias.Segments[startSegmentIndex].SearchStart;
            var matchedSearchEnd = matchStart;
            var lastSegmentIndex = startSegmentIndex - 1;
            var usedAlternateInitial = false;

            for (var segmentIndex = startSegmentIndex;
                 segmentIndex < alias.Segments.Count && remaining.Length > 0;
                 segmentIndex++)
            {
                var segment = alias.Segments[segmentIndex];
                var pinyin = alias.SearchText.Substring(segment.SearchStart, segment.SearchLength);
                var consumedLength = 0;

                if (remaining.StartsWith(pinyin, StringComparison.Ordinal))
                {
                    consumedLength = pinyin.Length;
                }
                else
                {
                    var initial = GetPinyinInitial(pinyin);
                    var startsWithDigraph = StartsWithPinyinDigraph(remaining);
                    if (initial.Length > 1
                        && remaining.StartsWith(initial, StringComparison.Ordinal))
                    {
                        consumedLength = initial.Length;
                    }
                    else if (!startsWithDigraph
                        && initial.Length > 0
                        && remaining.StartsWith(initial[..1], StringComparison.Ordinal))
                    {
                        consumedLength = 1;
                    }

                    if (consumedLength > 0)
                    {
                        usedAlternateInitial |= !segment.IsPrimary;
                    }
                }

                if (consumedLength == 0)
                {
                    break;
                }

                remaining = remaining[consumedLength..];
                matchedSearchEnd = segment.SearchStart + consumedLength;
                lastSegmentIndex = segmentIndex;
            }

            if (remaining.Length == 0 && lastSegmentIndex >= startSegmentIndex)
            {
                var isExact = lastSegmentIndex == alias.Segments.Count - 1
                    && matchedSearchEnd == alias.SearchText.Length;
                return new AliasSearchMatch(
                    matchStart,
                    matchedSearchEnd - matchStart,
                    isExact,
                    usedAlternateInitial);
            }
        }

        return null;
    }

    private static string GetPinyinInitial(string pinyin)
    {
        return pinyin.StartsWith("zh", StringComparison.Ordinal)
            || pinyin.StartsWith("ch", StringComparison.Ordinal)
            || pinyin.StartsWith("sh", StringComparison.Ordinal)
            ? pinyin[..2]
            : pinyin.Length == 0 ? string.Empty : pinyin[..1];
    }

    private static bool StartsWithPinyinDigraph(string input)
    {
        return input.StartsWith("zh", StringComparison.Ordinal)
            || input.StartsWith("ch", StringComparison.Ordinal)
            || input.StartsWith("sh", StringComparison.Ordinal);
    }

    private static int GetAliasQuality(
        PromptAlias alias,
        int aliasStart,
        bool isExact)
    {
        var isPrefix = aliasStart == 0;
        return alias.Kind switch
        {
            PromptAliasKind.Initials => isPrefix
                ? isExact ? 115 : 105
                : 85,
            PromptAliasKind.FullPinyin => isPrefix
                ? isExact ? 110 : 100
                : 80,
            _ => 0
        };
    }

    private static int GetMinimumMatchLength(string triggerText)
    {
        return IsChineseCharacter(triggerText[^1]) ? 1 : 2;
    }

    private static bool IsChineseCharacter(char value)
    {
        return value is >= '\u3400' and <= '\u9fff'
            or >= '\uf900' and <= '\ufaff';
    }

    private static PromptMatchKind ToMatchKind(PromptAliasKind aliasKind, int aliasStart)
    {
        return aliasKind switch
        {
            PromptAliasKind.Initials => PromptMatchKind.PinyinInitial,
            PromptAliasKind.FullPinyin => PromptMatchKind.PinyinFull,
            _ => PromptMatchKind.NameContains
        };
    }

    private static bool IsBetterMatch(PromptMatch candidate, PromptMatch current)
    {
        return candidate.QualityScore > current.QualityScore
            || candidate.QualityScore == current.QualityScore
                && candidate.MatchLength > current.MatchLength;
    }

    // !SECTION 别名匹配与排序
}
