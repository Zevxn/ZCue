using System.Text;
using System.Text.RegularExpressions;
using ZCue.Models;

namespace ZCue.Services;

public sealed record PromptContentResult(string Content, string? Error = null)
{
    public bool Success => Error is null;
    public string Preview => Error is null ? Content : $"[引用错误：{Error}]";
}

public sealed class PromptReferenceService
{
    public const int MaximumDepth = 16;
    public const int MaximumContentLength = 100_000;
    private const int MaximumExpansions = 4096;
    private static readonly Regex ReferencePattern = new(
        @"(?<escape>\\)?\{\{prompt:(?<id>[^{}\r\n]*)\}\}", RegexOptions.CultureInvariant);
    private readonly Dictionary<string, PromptItem> _items;

    public PromptReferenceService(IEnumerable<PromptItem> items)
    {
        _items = items.DistinctBy(item => item.Id, StringComparer.Ordinal)
            .ToDictionary(item => item.Id, StringComparer.Ordinal);
    }

    // SECTION 引用展开

    public PromptContentResult Expand(PromptItem item)
    {
        var path = new List<PromptItem>();
        var expansions = 0;
        try
        {
            return new PromptContentResult(ExpandItem(item, path, ref expansions));
        }
        catch (InvalidOperationException exception)
        {
            return new PromptContentResult(string.Empty, exception.Message);
        }
    }

    private string ExpandItem(PromptItem item, List<PromptItem> path, ref int expansions)
    {
        if (path.Any(parent => parent.Id == item.Id))
        {
            throw new InvalidOperationException(
                $"循环引用：{string.Join(" → ", path.Select(parent => parent.Name).Append(item.Name))}");
        }

        if (path.Count >= MaximumDepth)
        {
            throw new InvalidOperationException($"引用层级不能超过 {MaximumDepth} 层。");
        }

        if (++expansions > MaximumExpansions)
        {
            throw new InvalidOperationException("引用展开次数过多，请减少重复嵌套。");
        }

        path.Add(item);
        try
        {
            var content = new StringBuilder();
            var offset = 0;
            foreach (Match match in ReferencePattern.Matches(item.Content))
            {
                AppendBounded(content, item.Content.AsSpan(offset, match.Index - offset));
                if (match.Groups["escape"].Success)
                {
                    // 转义后的标记只输出一次，不重新解释为引用。
                    AppendBounded(content, match.Value.AsSpan(1));
                }
                else
                {
                    var id = match.Groups["id"].Value;
                    if (!_items.TryGetValue(id, out var referenced))
                    {
                        throw new InvalidOperationException(
                            $"“{item.Name}”引用的指令不存在：{(id.Length == 0 ? "空 ID" : id)}");
                    }

                    AppendBounded(content, ExpandItem(referenced, path, ref expansions));
                }

                offset = match.Index + match.Length;
            }

            AppendBounded(content, item.Content.AsSpan(offset));
            return content.ToString();
        }
        finally
        {
            path.RemoveAt(path.Count - 1);
        }
    }

    internal static void AppendBounded(StringBuilder builder, ReadOnlySpan<char> text)
    {
        if (text.Length > MaximumContentLength - builder.Length)
        {
            throw new InvalidOperationException($"展开后的内容不能超过 {MaximumContentLength:N0} 个字符。");
        }

        builder.Append(text);
    }

    // !SECTION 引用展开

    // SECTION 依赖查询

    public static IReadOnlyList<string> GetReferenceIds(string content) => ReferencePattern.Matches(content)
        .Cast<Match>()
        .Where(match => !match.Groups["escape"].Success)
        .Select(match => match.Groups["id"].Value)
        .Distinct(StringComparer.Ordinal)
        .ToArray();

    public IReadOnlySet<string> GetDependencyIds(IEnumerable<string> rootIds)
    {
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<string>(rootIds);
        while (pending.TryPop(out var id))
        {
            if (!visited.Add(id) || !_items.TryGetValue(id, out var item))
            {
                continue;
            }

            foreach (var referenceId in GetReferenceIds(item.Content))
            {
                pending.Push(referenceId);
            }
        }

        return visited;
    }

    public IReadOnlyList<PromptItem> GetAffectedByDeletion(IReadOnlySet<string> deletedIds)
    {
        // 沿反向关系传播，包含间接引用方；同批删除的指令不列为受影响项。
        var affectedIds = new HashSet<string>(deletedIds, StringComparer.Ordinal);
        var references = _items.Values.ToDictionary(item => item.Id, item => GetReferenceIds(item.Content));
        bool changed;
        do
        {
            changed = false;
            foreach (var (id, targets) in references)
            {
                if (!affectedIds.Contains(id) && targets.Any(affectedIds.Contains))
                {
                    affectedIds.Add(id);
                    changed = true;
                }
            }
        } while (changed);

        return _items.Values.Where(item => affectedIds.Contains(item.Id) && !deletedIds.Contains(item.Id))
            .ToArray();
    }

    // !SECTION 依赖查询
}
