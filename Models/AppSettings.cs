namespace TypeSense.Models;

public sealed class AppSettings
{
    public bool ShowContentPreview { get; set; } = true;

    public bool ShowGhostPreview { get; set; } = true;

    public bool EnableNumberSelection { get; set; } = true;

    public bool EnableEnterConfirmation { get; set; } = true;

    public AppSettings Clone() => new()
    {
        ShowContentPreview = ShowContentPreview,
        ShowGhostPreview = ShowGhostPreview,
        EnableNumberSelection = EnableNumberSelection,
        EnableEnterConfirmation = EnableEnterConfirmation
    };
}
