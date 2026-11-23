namespace StartApp.Presentation;

/// <summary>Owns an address editor's saved or staged target, original value and draft.
/// The staged target is opaque: this owner never reads or writes it. No store, commands or view dependency.</summary>
public sealed class AddressEditSession<TStaged> where TStaged : class
{
    public bool IsOpen { get; set; }
    public string WorkspaceId { get; set; } = "";
    public string InstanceId { get; set; } = "";
    public TStaged? StagedTarget { get; set; }
    public string Address { get; set; } = "";
    public string OriginalAddress { get; set; } = "";
    public bool IsDirty => IsOpen && Address != OriginalAddress;

    /// <summary>Release targets after the host publishes a close notification, preserving draft text and original.</summary>
    public void ClearTarget()
    {
        StagedTarget = null; InstanceId = ""; WorkspaceId = "";
    }
}
