namespace StartApp.Presentation;

/// <summary>Owns capture navigation, target identity, search and scanning state. The nested stage
/// type is retained for public API compatibility; no host instance or binding access is held.</summary>
public sealed class CaptureSessionState
{
    public MainViewModel.CaptureStage Stage { get; set; }
    public string WorkspaceId { get; set; } = "";
    public string Search { get; set; } = "";
    public bool Scanning { get; set; }
    public bool IsOpen => Stage != MainViewModel.CaptureStage.None;
    public bool IsNew => Stage == MainViewModel.CaptureStage.NewForm;
    public bool IsPick => Stage == MainViewModel.CaptureStage.PickWorkspace;
    public bool IsSide => Stage == MainViewModel.CaptureStage.SideBySide;
    public bool IsDirty(int stagedCount, bool createDirty) => IsSide && stagedCount > 0 || IsNew && createDirty;
}
