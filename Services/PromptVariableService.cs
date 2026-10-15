using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ZCue.Models;
using WpfClipboard = System.Windows.Clipboard;

namespace ZCue.Services;

public sealed class PromptVariableService
{
    private const string ClipboardReadFailureText = "[无法读取剪贴板]";
    private static readonly Regex VariablePattern = new(
        @"\{\{(?<name>date|time|clipboard)\}\}", RegexOptions.CultureInvariant);

    public IReadOnlyList<PromptContentResult> ResolveAll(
        IReadOnlyList<PromptItem> prompts,
        IReadOnlyList<PromptItem> catalog)
    {
        if (prompts.Count == 0)
        {
            return Array.Empty<PromptContentResult>();
        }

        var references = new PromptReferenceService(catalog);
        var currentItems = catalog.DistinctBy(item => item.Id, StringComparer.Ordinal)
            .ToDictionary(item => item.Id, StringComparer.Ordinal);
        var expanded = prompts.Select(prompt => currentItems.TryGetValue(prompt.Id, out var current)
                ? references.Expand(current)
                : new PromptContentResult(string.Empty, $"指令已被删除：{prompt.Name}"))
            .ToArray();
        var now = DateTime.Now;
        var date = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var time = now.ToString("HH:mm", CultureInfo.InvariantCulture);
        var clipboardText = expanded.Any(result => result.Success
                && result.Content.Contains("{{clipboard}}", StringComparison.Ordinal))
            ? ReadClipboardText()
            : string.Empty;

        return expanded.Select(result => ResolveVariables(result, date, time, clipboardText)).ToArray();
    }

    public PromptContentResult Resolve(PromptItem prompt, IReadOnlyList<PromptItem> catalog)
    {
        return ResolveAll([prompt], catalog)[0];
    }

    private static PromptContentResult ResolveVariables(
        PromptContentResult expanded, string date, string time, string clipboardText)
    {
        if (!expanded.Success)
        {
            return expanded;
        }

        try
        {
            var content = new StringBuilder();
            var offset = 0;
            foreach (Match match in VariablePattern.Matches(expanded.Content))
            {
                PromptReferenceService.AppendBounded(content,
                    expanded.Content.AsSpan(offset, match.Index - offset));
                var value = match.Groups["name"].Value switch
                {
                    "date" => date,
                    "time" => time,
                    _ => clipboardText
                };
                // 单次扫描模板，剪贴板中的变量和引用标记作为普通文字输出。
                PromptReferenceService.AppendBounded(content, value);
                offset = match.Index + match.Length;
            }

            PromptReferenceService.AppendBounded(content, expanded.Content.AsSpan(offset));
            return new PromptContentResult(content.ToString());
        }
        catch (InvalidOperationException exception)
        {
            return new PromptContentResult(string.Empty, exception.Message);
        }
    }

    private static string ReadClipboardText()
    {
        try
        {
            return WpfClipboard.GetText();
        }
        catch
        {
            return ClipboardReadFailureText;
        }
    }
}
