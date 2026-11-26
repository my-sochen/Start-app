using StartApp.Core;

namespace StartApp.Presentation;

/// <summary>One item edit's captured identity, original values and mutable draft.
/// No store, persistence, commands, MainViewModel or view access. Closing preserves the draft.</summary>
public sealed class ItemEditSession
{
    public string WorkspaceId { get; private set; } = "";
    public string InstanceId { get; private set; } = "";
    public ItemType Type { get; private set; }
    public bool IsOpen { get; set; }
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Target { get; set; } = "";
    public string OpenWith { get; set; } = "";
    public string OriginalName { get; private set; } = "";
    public string OriginalDescription { get; private set; } = "";
    public string OriginalTarget { get; private set; } = "";
    public string OriginalOpenWith { get; set; } = "";
    public bool IsDirty => IsOpen && (Name != OriginalName || Description != OriginalDescription || Target != OriginalTarget || OpenWith != OriginalOpenWith);

    /// <summary>Capture identity and original values before the host publishes draft property notifications.
    /// OriginalOpenWith is captured separately after the host resolves instance launch preferences.</summary>
    public void CaptureOriginal(string workspaceId, string instanceId, string name, string? description, string target, ItemType type)
    {
        WorkspaceId = workspaceId; InstanceId = instanceId; Type = type;
        OriginalName = name; OriginalDescription = description ?? ""; OriginalTarget = target;
    }
}
