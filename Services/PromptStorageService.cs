using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ZCue.Models;

namespace ZCue.Services;

public sealed class PromptStorageService
{
    private readonly string _filePath;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    public PromptStorageService()
        : this(Path.Combine(GetDataDirectory(), "prompts.json"))
    {
    }

    public PromptStorageService(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = filePath;
    }

    private static string GetDataDirectory()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(
            string.IsNullOrWhiteSpace(localAppData) ? AppContext.BaseDirectory : localAppData,
            "ZCue");
    }

    // SECTION 加载与保存

    public StorageSnapshot? Load()
    {
        if (!File.Exists(_filePath))
        {
            return null;
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.EnumerateObject().Any(property =>
                    string.Equals(property.Name, "allCommands", StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }

            var storedData = JsonSerializer.Deserialize<StorageDocument>(json, _jsonOptions);
            if (storedData is null)
            {
                return null;
            }

            return new StorageSnapshot(
                (storedData.AllCommands ?? [])
                    .Where(command => command is not null)
                    .Select(ToPromptItem)
                    .ToArray(),
                storedData.AllCategories ?? []);
        }
        catch
        {
            // 保留损坏文件，调用方使用默认数据启动，避免启动失败。
            return null;
        }
    }

    public void Save(IEnumerable<PromptItem> prompts, IEnumerable<PromptCategory> categories)
    {
        try
        {
            WriteDocument(_filePath, prompts, categories);
        }
        catch
        {
            // 本地数据写入失败不应中断全局 Hook；下次启动仍可使用默认数据。
        }
    }

    // !SECTION 加载与保存

    // SECTION 外部文件导入与导出

    public StorageSnapshot LoadImportFile(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var json = File.ReadAllText(filePath);
        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        if (root.ValueKind == JsonValueKind.Array)
        {
            var commands = JsonSerializer.Deserialize<List<StoredCommand>>(json, _jsonOptions) ?? [];
            return new StorageSnapshot(
                commands.Where(command => command is not null)
                    .Select(ToPromptItem)
                    .ToArray(),
                []);
        }

        if (root.ValueKind != JsonValueKind.Object
            || !TryGetPropertyIgnoreCase(root, "allCommands", out var commandsElement)
            || (commandsElement.ValueKind != JsonValueKind.Array
                && commandsElement.ValueKind != JsonValueKind.Null))
        {
            throw new InvalidDataException("文件不是受支持的提示词 JSON 格式。");
        }

        if (TryGetPropertyIgnoreCase(root, "allCategories", out var categoriesElement)
            && categoriesElement.ValueKind != JsonValueKind.Array
            && categoriesElement.ValueKind != JsonValueKind.Null)
        {
            throw new InvalidDataException("文件中的分类数据格式无效。");
        }

        var storedData = JsonSerializer.Deserialize<StorageDocument>(json, _jsonOptions)
            ?? throw new InvalidDataException("文件中没有可读取的提示词数据。");
        return new StorageSnapshot(
            (storedData.AllCommands ?? [])
                .Where(command => command is not null)
                .Select(ToPromptItem)
                .ToArray(),
            storedData.AllCategories ?? []);
    }

    public void ExportToFile(
        string filePath,
        IEnumerable<PromptItem> prompts,
        IEnumerable<PromptCategory> categories)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        var fullPath = Path.GetFullPath(filePath);
        if (string.Equals(fullPath, Path.GetFullPath(_filePath), StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("不能将导出文件保存为 ZCue 当前的数据文件。");
        }

        WriteDocument(fullPath, prompts, categories);
    }

    private void WriteDocument(
        string filePath,
        IEnumerable<PromptItem> prompts,
        IEnumerable<PromptCategory> categories)
    {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var storedData = new StorageDocument
        {
            AllCommands = prompts.Select(FromPromptItem).ToList(),
            AllCategories = categories.Select(category => category.Clone()).ToList()
        };
        var json = JsonSerializer.Serialize(storedData, _jsonOptions);
        File.WriteAllText(filePath, json);
    }

    private static bool TryGetPropertyIgnoreCase(
        JsonElement element,
        string propertyName,
        out JsonElement value)
    {
        foreach (var property in element.EnumerateObject())
        {
            if (string.Equals(property.Name, propertyName, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }

        value = default;
        return false;
    }

    // !SECTION 外部文件导入与导出

    // SECTION 插件数据映射

    private static PromptItem ToPromptItem(StoredCommand command) => new()
    {
        Id = command.Id,
        Name = command.Key,
        Content = command.Value,
        Enabled = command.Active,
        UsageCount = command.UsageCount,
        CategoryId = command.CategoryId,
        PinyinAliases = command.PinyinAliases?
            .Select(alias => alias.DeepCopy())
            .ToList() ?? []
    };

    private static StoredCommand FromPromptItem(PromptItem prompt) => new()
    {
        Id = prompt.Id,
        Key = prompt.Name,
        Value = prompt.Content,
        Active = prompt.Enabled,
        UsageCount = prompt.UsageCount,
        CategoryId = prompt.CategoryId,
        PinyinAliases = prompt.PinyinAliases?
            .Select(alias => alias.DeepCopy())
            .ToList() ?? []
    };

    // !SECTION 插件数据映射

    public sealed record StorageSnapshot(
        IReadOnlyList<PromptItem> Prompts,
        IReadOnlyList<PromptCategory> Categories);

    private sealed class StorageDocument
    {
        public StorageDocument()
        {
        }

        [JsonPropertyName("allCommands")]
        public List<StoredCommand> AllCommands { get; set; } = [];

        [JsonPropertyName("allCategories")]
        public List<PromptCategory> AllCategories { get; set; } = [];
    }

    private sealed class StoredCommand
    {
        public StoredCommand()
        {
        }

        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("key")]
        public string Key { get; set; } = string.Empty;

        [JsonPropertyName("value")]
        public string Value { get; set; } = string.Empty;

        [JsonPropertyName("active")]
        public bool Active { get; set; } = true;

        [JsonPropertyName("usageCount")]
        public int UsageCount { get; set; }

        [JsonPropertyName("categoryId")]
        public string CategoryId { get; set; } = string.Empty;

        [JsonPropertyName("pinyinAliases")]
        public List<PromptAlias>? PinyinAliases { get; set; } = [];
    }
}
