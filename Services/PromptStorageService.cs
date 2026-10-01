using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ZCue.Models;

namespace ZCue.Services;

public sealed class PromptStorageService
{
    private readonly string _filePath;
    private readonly AppSettingsService? _settings;
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
        _filePath = Path.GetFullPath(filePath);
    }

    public PromptStorageService(AppSettingsService settings)
        : this()
    {
        ArgumentNullException.ThrowIfNull(settings);
        _settings = settings;
    }

    public string FilePath
    {
        get
        {
            var customPath = _settings?.Current.PromptDataFilePath;
            return string.IsNullOrWhiteSpace(customPath) ? _filePath : Path.GetFullPath(customPath);
        }
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
        var filePath = FilePath;
        var hasCustomPath = !string.IsNullOrWhiteSpace(_settings?.Current.PromptDataFilePath);
        if (!File.Exists(filePath))
        {
            if (hasCustomPath)
            {
                throw new FileNotFoundException("找不到自定义提示词数据文件，请检查保存位置是否可用。", filePath);
            }

            return null;
        }

        // 已有文件读取失败时中止启动，避免默认数据覆盖用户文件。
        return ReadDocument(filePath);
    }

    private StorageSnapshot ReadDocument(string filePath)
    {
        var json = File.ReadAllText(filePath);
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Object
            || !document.RootElement.EnumerateObject().Any(property =>
                string.Equals(property.Name, "allCommands", StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidDataException("提示词数据文件格式无效。");
        }

        var storedData = JsonSerializer.Deserialize<StorageDocument>(json, _jsonOptions);
        if (storedData is null)
        {
            throw new InvalidDataException("提示词数据文件中没有可读取的数据。");
        }

        return new StorageSnapshot(
            (storedData.AllCommands ?? [])
                .Where(command => command is not null)
                .Select(ToPromptItem)
                .ToArray(),
            storedData.AllCategories ?? []);
    }

    public void Save(IEnumerable<PromptItem> prompts, IEnumerable<PromptCategory> categories)
    {
        try
        {
            WriteDocument(FilePath, prompts, categories);
        }
        catch
        {
            // 本地数据写入失败时保持内存数据，避免中断全局 Hook。
        }
    }

    // !SECTION 加载与保存

    // SECTION 提示词位置迁移

    public bool ChangeFilePath(
        string filePath,
        IEnumerable<PromptItem> prompts,
        IEnumerable<PromptCategory> categories,
        Action<StorageSnapshot> prepareExistingData)
    {
        if (_settings is null)
        {
            throw new InvalidOperationException("当前存储服务未关联位置设置。");
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(prepareExistingData);
        var fullPath = Path.GetFullPath(filePath);
        if (string.Equals(fullPath, FilePath, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (string.Equals(fullPath, _settings.FilePath, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("不能将提示词保存为软件的设置文件。");
        }

        if (File.Exists(fullPath))
        {
            prepareExistingData(ReadDocument(fullPath));
            _settings.SetPromptDataFilePath(fullPath);
            return true;
        }

        var temporaryPath = fullPath + $".{Guid.NewGuid():N}.tmp";
        var created = false;
        try
        {
            WriteDocument(temporaryPath, prompts, categories);
            File.Move(temporaryPath, fullPath, overwrite: false);
            created = true;
            _settings.SetPromptDataFilePath(fullPath);
        }
        catch
        {
            if (created)
            {
                File.Delete(fullPath);
            }

            throw;
        }
        finally
        {
            if (File.Exists(temporaryPath))
            {
                File.Delete(temporaryPath);
            }
        }

        return false;
    }

    // !SECTION 提示词位置迁移

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
        if (string.Equals(fullPath, FilePath, StringComparison.OrdinalIgnoreCase))
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

    // SECTION 提示词数据映射

    private static PromptItem ToPromptItem(StoredCommand command) => new()
    {
        Id = command.Id,
        Name = command.Key,
        Content = command.Value,
        Enabled = command.Active,
        UsageCount = command.UsageCount,
        CategoryId = command.CategoryId,
        PinyinAliases = PinyinAliasService.CreateAliases(command.Key ?? string.Empty)
    };

    private static StoredCommand FromPromptItem(PromptItem prompt) => new()
    {
        Id = prompt.Id,
        Key = prompt.Name ?? string.Empty,
        Value = prompt.Content,
        Active = prompt.Enabled,
        UsageCount = prompt.UsageCount,
        CategoryId = prompt.CategoryId
    };

    // !SECTION 提示词数据映射

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
    }
}
