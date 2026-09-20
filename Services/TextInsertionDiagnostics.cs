using System.IO;

namespace TypeSense.Services;

internal static class TextInsertionDiagnostics
{
    private static readonly object Gate = new();
    private static readonly string LogPath = Path.Combine(Path.GetTempPath(), "TypeSense-text-insertion.log");

    internal static void Write(string message)
    {
        try
        {
            lock (Gate)
            {
                File.AppendAllText(
                    LogPath,
                    $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
            }
        }
        catch
        {
            // 诊断日志不能影响全局键盘监听和文本插入。
        }
    }
}
