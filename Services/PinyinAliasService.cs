using System.Collections.Concurrent;
using System.Text;
using Microsoft.International.Converters.PinYinConverter;
using ZCue.Models;

namespace ZCue.Services;

public sealed class PinyinAliasService
{
    private const int MaxGeneratedPinyinVariants = 64;
    private static readonly ConcurrentDictionary<char, IReadOnlyList<PinyinOption>> PinyinCache = new();

    public bool RefreshAliases(PromptItem item)
    {
        var generatedAliases = BuildGeneratedAliases(item.Name ?? string.Empty);
        var generated = generatedAliases.Full
            .Concat(generatedAliases.Initials)
            .DistinctBy(alias => (alias.Kind, alias.SearchText))
            .ToList();

        if (AreEquivalent(item.PinyinAliases, generated))
        {
            return false;
        }

        item.PinyinAliases = generated;
        return true;
    }

    public static string NormalizeInput(string value)
    {
        return Normalize(value);
    }

    private static bool AreEquivalent(
        IReadOnlyList<PromptAlias>? current,
        IReadOnlyList<PromptAlias> generated)
    {
        if (current is null || current.Count != generated.Count)
        {
            return false;
        }

        for (var index = 0; index < generated.Count; index++)
        {
            var left = current[index];
            var right = generated[index];
            if (!string.Equals(left.Text, right.Text, StringComparison.Ordinal)
                || !string.Equals(left.SearchText, right.SearchText, StringComparison.Ordinal)
                || left.Kind != right.Kind
                || left.IsPrimary != right.IsPrimary
                || left.Segments.Count != right.Segments.Count)
            {
                return false;
            }

            for (var segmentIndex = 0; segmentIndex < right.Segments.Count; segmentIndex++)
            {
                if (left.Segments[segmentIndex] != right.Segments[segmentIndex])
                {
                    return false;
                }
            }
        }

        return true;
    }

    // SECTION 拼音别名构建

    private sealed record PinyinOption(string Text, bool IsPrimary);

    private sealed record PinyinPart(int NameIndex, string Text, bool IsPrimary);

    private sealed record PinyinVariant(
        IReadOnlyList<PinyinPart> Parts,
        bool IsPrimary);

    private static (IReadOnlyList<PromptAlias> Full, IReadOnlyList<PromptAlias> Initials)
        BuildGeneratedAliases(string name)
    {
        var variants = new List<PinyinVariant>
        {
            new(Array.Empty<PinyinPart>(), true)
        };

        for (var nameIndex = 0; nameIndex < name.Length; nameIndex++)
        {
            var options = GetPinyinOptions(name[nameIndex]);
            if (options.Count == 0)
            {
                continue;
            }

            var expanded = new List<PinyinVariant>();
            foreach (var variant in variants)
            {
                foreach (var option in options)
                {
                    var parts = variant.Parts
                        .Append(new PinyinPart(nameIndex, option.Text, option.IsPrimary))
                        .ToArray();
                    expanded.Add(new PinyinVariant(
                        parts,
                        variant.IsPrimary && option.IsPrimary));

                    if (expanded.Count >= MaxGeneratedPinyinVariants)
                    {
                        break;
                    }
                }

                if (expanded.Count >= MaxGeneratedPinyinVariants)
                {
                    break;
                }
            }

            variants = expanded;
        }

        var fullAliases = variants
            .Select(variant => BuildAlias(variant.Parts, PromptAliasKind.FullPinyin, variant.IsPrimary))
            .Where(alias => alias is not null)
            .Cast<PromptAlias>()
            .ToArray();

        var initialAliases = variants
            .Select(variant => BuildAlias(
                variant.Parts
                    .Select(part => new PinyinPart(
                        part.NameIndex,
                        part.Text[..1],
                        part.IsPrimary))
                    .ToArray(),
                PromptAliasKind.Initials,
                variant.IsPrimary))
            .Where(alias => alias is not null)
            .Cast<PromptAlias>()
            .ToArray();

        return (fullAliases, initialAliases);
    }

    private static PromptAlias? BuildAlias(
        IReadOnlyList<PinyinPart> parts,
        PromptAliasKind kind,
        bool isPrimary)
    {
        if (parts.Count == 0)
        {
            return null;
        }

        var text = new StringBuilder();
        var searchText = new StringBuilder();
        var segments = new List<PromptAliasSegment>(parts.Count);
        foreach (var part in parts)
        {
            var searchPart = NormalizeInput(part.Text);
            if (searchPart.Length == 0)
            {
                continue;
            }

            var searchStart = searchText.Length;
            text.Append(part.Text);
            searchText.Append(searchPart);
            segments.Add(new PromptAliasSegment(
                searchStart,
                searchPart.Length,
                part.NameIndex,
                1,
                part.IsPrimary));
        }

        return searchText.Length == 0
            ? null
            : new PromptAlias(
                text.ToString(),
                searchText.ToString(),
                kind,
                segments,
                isPrimary);
    }

    // !SECTION 拼音别名构建

    // SECTION 单字转换

    private static IReadOnlyList<PinyinOption> GetPinyinOptions(char character)
    {
        if (char.IsWhiteSpace(character) || char.IsPunctuation(character))
        {
            return Array.Empty<PinyinOption>();
        }

        return PinyinCache.GetOrAdd(character, ConvertCharacterOptions);
    }

    private static IReadOnlyList<PinyinOption> ConvertCharacterOptions(char character)
    {
        try
        {
            if (ChineseChar.IsValidChar(character))
            {
                var pinyins = new ChineseChar(character).Pinyins
                    .Where(pinyin => !string.IsNullOrWhiteSpace(pinyin))
                    .Select(ConvertPinyin)
                    .Where(pinyin => pinyin.Length > 0)
                    .Distinct(StringComparer.Ordinal)
                    .ToArray();
                return pinyins
                    .Select((pinyin, index) => new PinyinOption(pinyin, index == 0))
                    .ToArray();
            }

            if (IsChineseCharacter(character))
            {
                return Array.Empty<PinyinOption>();
            }

            return char.IsLetterOrDigit(character)
                ? new[] { new PinyinOption(char.ToLowerInvariant(character).ToString(), true) }
                : Array.Empty<PinyinOption>();
        }
        catch
        {
            return Array.Empty<PinyinOption>();
        }
    }

    private static string ConvertPinyin(string pinyin)
    {
        return new string(pinyin
                .Where(char.IsLetter)
                .Select(char.ToLowerInvariant)
                .ToArray())
                .Replace('ü', 'v')
                .Replace('ǖ', 'v')
                .Replace('ǘ', 'v')
                .Replace('ǚ', 'v')
                .Replace('ǜ', 'v');
    }

    private static bool IsChineseCharacter(char character)
    {
        return character is >= '\u3400' and <= '\u9fff'
            or >= '\uf900' and <= '\ufaff';
    }

    private static string Normalize(string value)
    {
        return new string(value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToLowerInvariant)
            .ToArray());
    }

    // !SECTION 单字转换
}
