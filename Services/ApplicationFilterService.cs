using System.Diagnostics;
using System.IO;
using ZCue.Infrastructure;
using ZCue.Models;

namespace ZCue.Services;

public sealed class ApplicationFilterService : IDisposable
{
    // SECTION 前台应用识别与范围判断

    private static readonly HashSet<string> _shellProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer.exe",
        "SearchHost.exe",
        "ShellExperienceHost.exe",
        "StartMenuExperienceHost.exe"
    };

    private readonly object _gate = new();
    private readonly AppSettingsService _settings;
    private readonly uint _currentProcessId = unchecked((uint)Environment.ProcessId);
    private readonly string _currentProcessName = Path.GetFileName(
        Environment.ProcessPath ?? "ZCue.exe");
    private FilterSnapshot _snapshot;
    private uint _cachedProcessId;
    private string? _cachedProcessName;
    private string? _lastExternalProcessName;
    private bool _disposed;

    public ApplicationFilterService(AppSettingsService settings)
    {
        _settings = settings;
        _snapshot = CreateSnapshot(settings.Current);
        _cachedProcessId = _currentProcessId;
        _cachedProcessName = _currentProcessName;
        _settings.Changed += HandleSettingsChanged;
    }

    public ApplicationFilterMode FilterMode => Volatile.Read(ref _snapshot).Mode;

    public string? GetForegroundProcessName()
    {
        return GetProcessName(NativeMethods.GetForegroundWindow(), out _);
    }

    public string? GetLastExternalProcessName()
    {
        lock (_gate)
        {
            return _lastExternalProcessName;
        }
    }

    public bool IsCurrentApplicationAllowed()
    {
        return IsApplicationAllowed(NativeMethods.GetForegroundWindow());
    }

    public bool IsApplicationAllowed(IntPtr window)
    {
        var snapshot = Volatile.Read(ref _snapshot);
        var processName = GetProcessName(window, out var processId);
        if (processId == 0 || processId == _currentProcessId || processName is null)
        {
            return false;
        }

        return snapshot.Mode == ApplicationFilterMode.Blacklist
            ? !snapshot.Blacklist.Contains(processName)
            : snapshot.Whitelist.Contains(processName);
    }

    public bool IsBlacklisted(string? processName)
    {
        var normalized = NormalizeProcessName(processName);
        return normalized is not null
            && Volatile.Read(ref _snapshot).Blacklist.Contains(normalized);
    }

    public bool IsWhitelisted(string? processName)
    {
        var normalized = NormalizeProcessName(processName);
        return normalized is not null
            && Volatile.Read(ref _snapshot).Whitelist.Contains(normalized);
    }

    // !SECTION 前台应用识别与范围判断

    // SECTION 应用名单维护

    public bool AddToCurrentList(string? processName)
    {
        var normalized = NormalizeProcessName(processName);
        if (normalized is null)
        {
            return false;
        }

        var added = false;
        _settings.Update(settings =>
        {
            var list = GetCurrentList(settings);
            if (list.Any(name => string.Equals(name, normalized, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            list.Add(normalized);
            added = true;
        });
        return added;
    }

    public bool RemoveFromCurrentList(string? processName)
    {
        var normalized = NormalizeProcessName(processName);
        if (normalized is null)
        {
            return false;
        }

        var removed = false;
        _settings.Update(settings =>
        {
            var list = GetCurrentList(settings);
            var index = list.FindIndex(
                name => string.Equals(name, normalized, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                list.RemoveAt(index);
                removed = true;
            }
        });
        return removed;
    }

    public bool ToggleLastExternalApplication(out string? processName)
    {
        processName = GetLastExternalProcessName();
        var normalized = NormalizeProcessName(processName);
        if (normalized is null)
        {
            return false;
        }

        var added = false;
        _settings.Update(settings =>
        {
            var list = GetCurrentList(settings);
            var index = list.FindIndex(
                name => string.Equals(name, normalized, StringComparison.OrdinalIgnoreCase));
            if (index >= 0)
            {
                list.RemoveAt(index);
            }
            else
            {
                list.Add(normalized);
                added = true;
            }
        });
        return added;
    }

    // !SECTION 应用名单维护

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _settings.Changed -= HandleSettingsChanged;
    }

    // SECTION 进程名缓存与设置更新

    private string? GetProcessName(IntPtr window, out uint processId)
    {
        processId = 0;
        if (window == IntPtr.Zero)
        {
            return null;
        }

        try
        {
            NativeMethods.GetWindowThreadProcessId(window, out processId);
        }
        catch
        {
            processId = 0;
            return null;
        }

        if (processId == 0)
        {
            return null;
        }

        string? processName;
        lock (_gate)
        {
            if (_cachedProcessId != processId)
            {
                _cachedProcessId = processId;
                _cachedProcessName = TryGetProcessName(processId);
            }

            processName = _cachedProcessName;
            if (processId != _currentProcessId
                && processName is not null
                && !_shellProcessNames.Contains(processName))
            {
                _lastExternalProcessName = processName;
            }
        }

        return processName;
    }

    private static string? TryGetProcessName(uint processId)
    {
        try
        {
            using var process = Process.GetProcessById(unchecked((int)processId));
            var processName = process.ProcessName;
            return processName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                ? processName
                : $"{processName}.exe";
        }
        catch
        {
            return null;
        }
    }

    private void HandleSettingsChanged(AppSettings settings)
    {
        Volatile.Write(ref _snapshot, CreateSnapshot(settings));
    }

    private static FilterSnapshot CreateSnapshot(AppSettings settings)
    {
        var mode = Enum.IsDefined(typeof(ApplicationFilterMode), settings.FilterMode)
            ? settings.FilterMode
            : ApplicationFilterMode.Blacklist;
        return new FilterSnapshot(
            mode,
            CreateProcessNameSet(settings.Blacklist),
            CreateProcessNameSet(settings.Whitelist));
    }

    private static HashSet<string> CreateProcessNameSet(IEnumerable<string>? processNames)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var processName in processNames ?? [])
        {
            var normalized = NormalizeProcessName(processName);
            if (normalized is not null)
            {
                names.Add(normalized);
            }
        }

        return names;
    }

    private static List<string> GetCurrentList(AppSettings settings)
    {
        if (settings.FilterMode == ApplicationFilterMode.Whitelist)
        {
            return settings.Whitelist ??= [];
        }

        return settings.Blacklist ??= [];
    }

    private static string? NormalizeProcessName(string? processName)
    {
        if (string.IsNullOrWhiteSpace(processName))
        {
            return null;
        }

        var fileName = Path.GetFileName(processName.Trim());
        return string.IsNullOrWhiteSpace(fileName) ? null : fileName;
    }

    private sealed record FilterSnapshot(
        ApplicationFilterMode Mode,
        HashSet<string> Blacklist,
        HashSet<string> Whitelist);

    // !SECTION 进程名缓存与设置更新
}
