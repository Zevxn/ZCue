using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using ZCue.Models;
using ZCue.Services;

namespace ZCue.ViewModels;

public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private readonly AppSettingsService _settings;
    private readonly ApplicationFilterService _applicationFilter;
    private readonly StartupService _startupService;
    private readonly Func<bool> _isListeningEnabled;
    private readonly Action<bool> _setListeningEnabled;

    public SettingsViewModel(
        AppSettingsService settings,
        ApplicationFilterService applicationFilter,
        StartupService startupService,
        Func<bool> isListeningEnabled,
        Action<bool> setListeningEnabled)
    {
        _settings = settings;
        _applicationFilter = applicationFilter;
        _startupService = startupService;
        _isListeningEnabled = isListeningEnabled;
        _setListeningEnabled = setListeningEnabled;
        RefreshCurrentApplicationNames();
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event Action<string>? ErrorOccurred;

    public ObservableCollection<string> CurrentApplicationNames { get; } = [];

    // SECTION 设置属性

    public ApplicationFilterMode FilterMode
    {
        get => _settings.Current.FilterMode;
        set
        {
            if (!Enum.IsDefined(typeof(ApplicationFilterMode), value))
            {
                value = ApplicationFilterMode.Blacklist;
            }

            if (_settings.Current.FilterMode == value)
            {
                return;
            }

            _settings.Update(current => current.FilterMode = value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsBlacklistMode));
            OnPropertyChanged(nameof(IsWhitelistMode));
            OnPropertyChanged(nameof(CurrentApplicationListTitle));
            RefreshCurrentApplicationNames();
        }
    }

    public bool IsBlacklistMode
    {
        get => FilterMode == ApplicationFilterMode.Blacklist;
        set
        {
            if (value)
            {
                FilterMode = ApplicationFilterMode.Blacklist;
            }
        }
    }

    public bool IsWhitelistMode
    {
        get => FilterMode == ApplicationFilterMode.Whitelist;
        set
        {
            if (value)
            {
                FilterMode = ApplicationFilterMode.Whitelist;
            }
        }
    }

    public string CurrentApplicationListTitle =>
        FilterMode == ApplicationFilterMode.Blacklist ? "黑名单" : "白名单";

    public AppThemeMode ThemeMode
    {
        get => _settings.Current.ThemeMode;
        set
        {
            if (_settings.Current.ThemeMode == value)
            {
                return;
            }

            _settings.Update(current => current.ThemeMode = value);
            OnPropertyChanged();
        }
    }

    public bool EnablePinyinWake
    {
        get => _settings.Current.EnablePinyinWake;
        set
        {
            _settings.Update(current => current.EnablePinyinWake = value);
            OnPropertyChanged();
        }
    }

    public int CnWakeThreshold
    {
        get => _settings.Current.CnWakeThreshold;
        set
        {
            _settings.Update(current => current.CnWakeThreshold = Math.Clamp(value, 1, 5));
            OnPropertyChanged();
        }
    }

    public int PinWakeThreshold
    {
        get => _settings.Current.PinWakeThreshold;
        set
        {
            _settings.Update(current => current.PinWakeThreshold = Math.Clamp(value, 1, 5));
            OnPropertyChanged();
        }
    }

    public int EnWakeThreshold
    {
        get => _settings.Current.EnWakeThreshold;
        set
        {
            _settings.Update(current => current.EnWakeThreshold = Math.Clamp(value, 1, 5));
            OnPropertyChanged();
        }
    }

    public int SuggestionBoxWidth
    {
        get => _settings.Current.SuggestionBoxWidth;
        set
        {
            _settings.Update(current => current.SuggestionBoxWidth = Math.Clamp(value, 200, 1000));
            OnPropertyChanged();
        }
    }

    public bool IsListeningEnabled
    {
        get => _isListeningEnabled();
        set
        {
            _setListeningEnabled(value);
            OnPropertyChanged();
        }
    }

    public bool ShowContentPreview
    {
        get => _settings.Current.ShowContentPreview;
        set
        {
            _settings.Update(current => current.ShowContentPreview = value);
            OnPropertyChanged();
        }
    }

    public bool EnableNumberSelection
    {
        get => _settings.Current.EnableNumberSelection;
        set
        {
            _settings.Update(current => current.EnableNumberSelection = value);
            OnPropertyChanged();
        }
    }

    public bool EnableEnterConfirmation
    {
        get => _settings.Current.EnableEnterConfirmation;
        set
        {
            _settings.Update(current => current.EnableEnterConfirmation = value);
            OnPropertyChanged();
        }
    }

    public bool EnableAutomaticUpdateNotifications
    {
        get => _settings.Current.EnableAutomaticUpdateNotifications;
        set
        {
            if (_settings.Current.EnableAutomaticUpdateNotifications == value)
            {
                return;
            }

            _settings.Update(current => current.EnableAutomaticUpdateNotifications = value);
            OnPropertyChanged();
        }
    }

    public bool LaunchAtStartup
    {
        get
        {
            try
            {
                return _startupService.IsEnabled();
            }
            catch
            {
                return false;
            }
        }
        set
        {
            try
            {
                _startupService.SetEnabled(value);
                OnPropertyChanged();
            }
            catch (Exception exception)
            {
                ErrorOccurred?.Invoke($"设置开机启动失败：{exception.Message}");
                OnPropertyChanged();
            }
        }
    }

    // !SECTION 设置属性

    // SECTION 应用范围名单

    public void AddLastExternalApplication()
    {
        var processName = _applicationFilter.GetLastExternalProcessName();
        if (string.IsNullOrWhiteSpace(processName))
        {
            ErrorOccurred?.Invoke("无法识别最近使用的应用。请先在目标应用中操作，再打开设置添加。");
            return;
        }

        AddApplication(processName);
    }

    public void AddApplication(string? processName)
    {
        _applicationFilter.AddToCurrentList(processName);
        RefreshCurrentApplicationNames();
    }

    public void RemoveApplication(string? processName)
    {
        _applicationFilter.RemoveFromCurrentList(processName);
        RefreshCurrentApplicationNames();
    }

    private void RefreshCurrentApplicationNames()
    {
        var settings = _settings.Current;
        var names = settings.FilterMode == ApplicationFilterMode.Blacklist
            ? settings.Blacklist
            : settings.Whitelist;

        CurrentApplicationNames.Clear();
        foreach (var name in names)
        {
            CurrentApplicationNames.Add(name);
        }
    }

    // !SECTION 应用范围名单

    // SECTION 设置刷新

    public void Refresh()
    {
        OnPropertyChanged(nameof(FilterMode));
        OnPropertyChanged(nameof(IsBlacklistMode));
        OnPropertyChanged(nameof(IsWhitelistMode));
        OnPropertyChanged(nameof(CurrentApplicationListTitle));
        OnPropertyChanged(nameof(ThemeMode));
        OnPropertyChanged(nameof(EnablePinyinWake));
        OnPropertyChanged(nameof(CnWakeThreshold));
        OnPropertyChanged(nameof(PinWakeThreshold));
        OnPropertyChanged(nameof(EnWakeThreshold));
        OnPropertyChanged(nameof(SuggestionBoxWidth));
        OnPropertyChanged(nameof(IsListeningEnabled));
        OnPropertyChanged(nameof(ShowContentPreview));
        OnPropertyChanged(nameof(EnableNumberSelection));
        OnPropertyChanged(nameof(EnableEnterConfirmation));
        OnPropertyChanged(nameof(EnableAutomaticUpdateNotifications));
        OnPropertyChanged(nameof(LaunchAtStartup));
        RefreshCurrentApplicationNames();
    }

    // !SECTION 设置刷新

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
