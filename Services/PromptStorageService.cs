using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
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
        : this(Path.Combine(GetDataDirectory(), "prompts.json"))
    {
    }

    public PromptStorageService(string filePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        _filePath = filePath;
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
            if (document.RootElement.ValueKind == JsonValueKind.Array)
            {
                var legacyPrompts = JsonSerializer.Deserialize<List<PromptItem>>(
                    json,
                    _jsonOptions) ?? [];
                return new StorageSnapshot(legacyPrompts, [], RequiresMigration: true);
            }

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
                storedData.AllCategories ?? [],
                RequiresMigration: false);
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
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            var storedData = new StorageDocument
            {
                AllCommands = prompts.Select(FromPromptItem).ToList(),
                AllCategories = categories.Select(category => category.Clone()).ToList()
            };
            var json = JsonSerializer.Serialize(storedData, _jsonOptions);
            File.WriteAllText(_filePath, json);
        }
        catch
        {
            // 本地数据写入失败不应中断全局 Hook；下次启动仍可使用默认数据。
        }
    }

    // !SECTION 加载与保存

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

    private static string GetDataDirectory()
    {
        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(
            string.IsNullOrWhiteSpace(localAppData) ? AppContext.BaseDirectory : localAppData,
            "TypeSense");
    }

    public sealed record StorageSnapshot(
        IReadOnlyList<PromptItem> Prompts,
        IReadOnlyList<PromptCategory> Categories,
        bool RequiresMigration);

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
