using System.IO;
using System.Text.Json;
using TypeSense.Models;

namespace TypeSense.Services;

public sealed class PromptStorageService
{
    private readonly string _filePath;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public PromptStorageService()
    {
        _filePath = Path.Combine(GetDataDirectory(), "prompts.json");
    }

    public IReadOnlyList<PromptItem>? Load()
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<List<PromptItem>>(
                File.ReadAllText(_filePath),
                _jsonOptions);
        }
        catch
        {
            // 保留损坏文件，调用方使用默认数据启动，避免启动失败。
            return null;
        }
    }

    public void Save(IEnumerable<PromptItem> items)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            var json = JsonSerializer.Serialize(items, _jsonOptions);
            File.WriteAllText(_filePath, json);
        }
        catch
        {
            // 本地数据写入失败不应中断全局 Hook；下次启动仍可使用默认数据。
        }
    }

    private static string GetDataDirectory()
    {
        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(
            string.IsNullOrWhiteSpace(localAppData) ? AppContext.BaseDirectory : localAppData,
            "TypeSense");
    }
}
