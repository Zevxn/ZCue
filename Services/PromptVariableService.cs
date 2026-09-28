using System.Globalization;
using WpfClipboard = System.Windows.Clipboard;

namespace ZCue.Services;

public sealed class PromptVariableService
{
    private const string ClipboardVariable = "{{clipboard}}";
    private const string DateVariable = "{{date}}";
    private const string TimeVariable = "{{time}}";
    private const string ClipboardReadFailureText = "[无法读取剪贴板]";

    public IReadOnlyList<string> ResolveAll(IReadOnlyList<string> templates)
    {
        if (templates.Count == 0)
        {
            return Array.Empty<string>();
        }

        var now = DateTime.Now;
        var date = now.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var time = now.ToString("HH:mm", CultureInfo.InvariantCulture);
        var clipboardText = templates.Any(template =>
                template.Contains(ClipboardVariable, StringComparison.Ordinal))
            ? ReadClipboardText()
            : string.Empty;

        return templates
            .Select(template => template
                .Replace(DateVariable, date, StringComparison.Ordinal)
                .Replace(TimeVariable, time, StringComparison.Ordinal)
                .Replace(ClipboardVariable, clipboardText, StringComparison.Ordinal))
            .ToArray();
    }

    public string Resolve(string template)
    {
        return ResolveAll([template])[0];
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
