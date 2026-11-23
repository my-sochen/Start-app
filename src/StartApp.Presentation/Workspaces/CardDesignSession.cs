using StartApp.Core;

namespace StartApp.Presentation;

/// <summary>One card-design editing session. Owns its captured workspace, draft and original values.
/// No repository, commands, window or MainViewModel access.</summary>
public sealed class CardDesignSession
{
    public string WorkspaceId { get; private set; } = "";
    public bool IsOpen { get; set; }
    public string Color { get; set; } = "";
    public string ImagePath { get; set; } = "";
    public string OriginalColor { get; private set; } = "";
    public string OriginalImagePath { get; private set; } = "";
    public bool IsDirty => IsOpen && (Color != OriginalColor || ImagePath != OriginalImagePath);

    /// <summary>Capture identity and originals before the host publishes the draft property notifications.</summary>
    public void CaptureOriginal(Workspace workspace)
    {
        WorkspaceId = workspace.Id;
        OriginalColor = workspace.Color ?? "";
        OriginalImagePath = workspace.ImagePath ?? "";
    }
}
