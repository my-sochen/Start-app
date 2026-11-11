namespace StartApp.Core;

public enum ItemType { Link, File, Folder, Program }

/// <summary>Catalog item: identity (what it is) plus an optional user-written description of it, shared by every workspace that holds the item. Old files without a description load as null. Launch preferences live on the workspace membership.</summary>
public sealed record Item(string Id, ItemType Type, string Name, string Target, string? Description = null);

/// <summary>Per-workspace launch preferences. PROPOSAL: optional fields, null = OS default.</summary>
public sealed record LaunchConfig(string? OpenWith = null, string? Browser = null);

/// <summary>A workspace's reference to a catalog item, with workspace-local launch config.</summary>
public sealed record Membership(string ItemId, LaunchConfig? Launch = null);

public sealed class Workspace
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    public List<Membership> Items { get; set; } = new();
}

public sealed class Store
{
    public int SchemaVersion { get; set; } = 1;
    public Dictionary<string, Item> Items { get; set; } = new();
    public List<Workspace> Workspaces { get; set; } = new();
}
