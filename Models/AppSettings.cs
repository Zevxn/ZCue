namespace TypeSense.Models;

public sealed class AppSettings
{
    public AppThemeMode ThemeMode { get; set; } = AppThemeMode.System;

    public bool EnablePinyinWake { get; set; } = true;

    public int CnWakeThreshold { get; set; } = 2;

    public int PinWakeThreshold { get; set; } = 2;

    public int EnWakeThreshold { get; set; } = 2;

    public int SuggestionBoxWidth { get; set; } = 320;

    public bool ShowContentPreview { get; set; } = true;

    public bool EnableNumberSelection { get; set; } = true;

    public bool EnableEnterConfirmation { get; set; } = true;

    public AppSettings Clone() => new()
    {
        ThemeMode = ThemeMode,
        EnablePinyinWake = EnablePinyinWake,
        CnWakeThreshold = CnWakeThreshold,
        PinWakeThreshold = PinWakeThreshold,
        EnWakeThreshold = EnWakeThreshold,
        SuggestionBoxWidth = SuggestionBoxWidth,
        ShowContentPreview = ShowContentPreview,
        EnableNumberSelection = EnableNumberSelection,
        EnableEnterConfirmation = EnableEnterConfirmation
    };
}
