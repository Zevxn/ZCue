using System.IO;
using System.Text.Json;
using TypeSense.Models;

namespace TypeSense.Services;

public sealed class AppSettingsService
{
    private readonly object _gate = new();
    private readonly string _filePath;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };
    private AppSettings _current;

    public AppSettingsService()
    {
        _filePath = Path.Combine(GetDataDirectory(), "settings.json");
        _current = Load();
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
            return JsonSerializer.Deserialize<AppSettings>(
                       File.ReadAllText(_filePath),
                       _jsonOptions)
                   ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
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

    private static string GetDataDirectory()
    {
        var localAppData = Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData);
        return Path.Combine(
            string.IsNullOrWhiteSpace(localAppData) ? AppContext.BaseDirectory : localAppData,
            "TypeSense");
    }
}
