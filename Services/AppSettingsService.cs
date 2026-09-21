using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using ZCue.Models;

namespace ZCue.Services;

public sealed class AppSettingsService
{
    private readonly object _gate = new();
    private readonly string _filePath;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
    private AppSettings _current;

    public AppSettingsService()
    {
        _filePath = Path.Combine(GetDataDirectory(), "settings.json");
        _current = Load();
    }

    private static string GetDataDirectory()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(
            string.IsNullOrWhiteSpace(localAppData) ? AppContext.BaseDirectory : localAppData,
            "ZCue");
    }

    public event Action<AppSettings>? Changed;

    public AppSettings Current
    {
        get
        {
            lock (_gate)
            {
                return _current.Clone();
            }
        }
    }

    public void Update(Action<AppSettings> update)
    {
        ArgumentNullException.ThrowIfNull(update);

        AppSettings snapshot;
        lock (_gate)
        {
            update(_current);
            Normalize(_current);
            SaveLocked();
            snapshot = _current.Clone();
        }

        Changed?.Invoke(snapshot);
    }

    private AppSettings Load()
    {
        if (!File.Exists(_filePath))
        {
            return new AppSettings();
        }

        try
        {
            var settings = JsonSerializer.Deserialize<AppSettings>(
                File.ReadAllText(_filePath),
                _jsonOptions);
            return Normalize(settings ?? new AppSettings());
        }
        catch
        {
            return new AppSettings();
        }
    }

    private static AppSettings Normalize(AppSettings settings)
    {
        if (!Enum.IsDefined(typeof(AppThemeMode), settings.ThemeMode))
        {
            settings.ThemeMode = AppThemeMode.System;
        }

        settings.CnWakeThreshold = Math.Clamp(settings.CnWakeThreshold, 1, 5);
        settings.PinWakeThreshold = Math.Clamp(settings.PinWakeThreshold, 1, 5);
        settings.EnWakeThreshold = Math.Clamp(settings.EnWakeThreshold, 1, 5);
        settings.SuggestionBoxWidth = Math.Clamp(settings.SuggestionBoxWidth, 200, 1000);
        if (!Enum.IsDefined(typeof(ApplicationFilterMode), settings.FilterMode))
        {
            settings.FilterMode = ApplicationFilterMode.Blacklist;
        }

        settings.Blacklist = NormalizeProcessNames(settings.Blacklist);
        settings.Whitelist = NormalizeProcessNames(settings.Whitelist);
        return settings;
    }

    private static List<string> NormalizeProcessNames(List<string>? names)
    {
        var normalized = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var name in names ?? [])
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var fileName = Path.GetFileName(name.Trim());
            if (!string.IsNullOrWhiteSpace(fileName) && seen.Add(fileName))
            {
                normalized.Add(fileName);
            }
        }

        return normalized;
    }

    private void SaveLocked()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            var json = JsonSerializer.Serialize(_current, _jsonOptions);
            File.WriteAllText(_filePath, json);
        }
        catch
        {
            // 设置写入失败时保留内存状态，避免影响全局输入监听。
        }
    }

}
