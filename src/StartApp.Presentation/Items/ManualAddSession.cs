using StartApp.Core;

namespace StartApp.Presentation;

/// <summary>Owns manual-add draft values, exact input interpretation and duplicate confirmation state.
/// The host retains notifications, selected workspace, catalog mutation and clear-on-success order.</summary>
public sealed class ManualAddSession
{
    public ItemType Type { get; set; } = ItemType.File;
    public string Target { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public bool DuplicatePending { get; set; }
    public string DuplicateMessage { get; set; } = "";
    public ItemType EffectiveType => Uri.TryCreate(Target, UriKind.Absolute, out var web)
        && (web.Scheme == Uri.UriSchemeHttp || web.Scheme == Uri.UriSchemeHttps) ? ItemType.Link : Type;
    public string? ValidationError => Catalog.ValidateInputTarget(EffectiveType, Target)
        ?? Catalog.ValidateDescription(Description)
        ?? Catalog.ValidateInstanceName(string.IsNullOrWhiteSpace(Name) ? null : Name);
}
