using StartApp.Core;

namespace StartApp.Presentation;

/// <summary>Owns workspace details input and dirty-suppression state.
/// The selected workspace stays with the host, preserving the existing comparison and navigation rules.</summary>
public sealed class WorkspaceDetailsDraft
{
    public string Name { get; set; } = "";
    public string Category { get; set; } = "";
    public string Description { get; set; } = "";
    public bool SuppressDirty { get; set; }

    public bool IsDirty(bool inDetails, Workspace? selected) =>
        !SuppressDirty && inDetails && selected != null &&
        (Name != selected.Name || Category != selected.EffectiveCategory || Description != (selected.Description ?? ""));

    /// <summary>Load all values without publishing intermediate notifications; the host publishes after the complete load.</summary>
    public void Load(Workspace selected)
    {
        Name = selected.Name; Category = selected.EffectiveCategory; Description = selected.Description ?? "";
    }
}
