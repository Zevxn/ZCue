using System.ComponentModel;
using System.Runtime.CompilerServices;
using TypeSense.Models;
using TypeSense.Services;

namespace TypeSense.ViewModels;

public sealed class SettingsViewModel : INotifyPropertyChanged
{
    private readonly AppSettingsService _settings;
    private readonly StartupService _startupService;
    private readonly Func<bool> _isListeningEnabled;
    private readonly Action<bool> _setListeningEnabled;

    public SettingsViewModel(
        AppSettingsService settings,
        StartupService startupService,
        Func<bool> isListeningEnabled,
        Action<bool> setListeningEnabled)
    {
        _settings = settings;
        _startupService = startupService;
        _isListeningEnabled = isListeningEnabled;
        _setListeningEnabled = setListeningEnabled;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public event Action<string>? ErrorOccurred;

    // SECTION 设置属性

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

    // SECTION 设置刷新

    public void Refresh()
    {
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
        OnPropertyChanged(nameof(LaunchAtStartup));
    }

    // !SECTION 设置刷新

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
