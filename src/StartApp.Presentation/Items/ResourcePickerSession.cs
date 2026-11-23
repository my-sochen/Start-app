using StartApp.Core;

namespace StartApp.Presentation;

/// <summary>Owns resource-picker visibility, active type and search draft.
/// Reads through the independent query without changing the store. No commands, bindings or view access.</summary>
public sealed class ResourcePickerSession
{
    public bool IsOpen { get; set; }
    public ItemType Type { get; set; } = ItemType.Link;
    public string Search { get; set; } = "";

    public IEnumerable<ResourcePickerQuery.Result> Read(Store store) =>
        IsOpen ? ResourcePickerQuery.Read(store, Type, Search) : Enumerable.Empty<ResourcePickerQuery.Result>();
}
