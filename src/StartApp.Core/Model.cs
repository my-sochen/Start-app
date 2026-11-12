namespace StartApp.Core;

public enum ItemType { Link, File, Folder, Program }

/// <summary>Catalog item: the resource itself (identity = type + exact target) with its original name.
/// Schema-2 files may carry a shared description on the item; it still loads and shows as a fallback, but new
/// descriptions are per workspace instance (see <see cref="Membership"/>), so editing one workspace never changes another.</summary>
public sealed record Item(string Id, ItemType Type, string Name, string Target, string? Description = null);

/// <summary>Per-instance launch preference (פתח באמצעות → הגדר כברירת מחדל): Browser for a link, OpenWith for a file/folder.
/// Null = the Windows default is used. The choice lives on the instance, is honored only inside Start-app, and never changes Windows associations.</summary>
public sealed record LaunchConfig(string? OpenWith = null, string? Browser = null);

/// <summary>One appearance of a catalog item in a workspace. The same target may appear more than once in a workspace
/// (after a user-confirmed duplicate warning); each appearance is a separate instance with its own id, name, description
/// and launch config, and changing one instance never changes another. Name/Description null = fall back to the catalog
/// item's original name / no description. Id is assigned when the instance is added; instances in older files get one on load.</summary>
public sealed record Membership(string ItemId, LaunchConfig? Launch = null, string? Id = null, string? Name = null, string? Description = null);

public sealed class Workspace
{
    /// <summary>Built-in default category, used when none is chosen. Custom categories and their management are a separate feature.</summary>
    public const string DefaultCategory = "כללי";
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string? Description { get; set; }
    /// <summary>User-chosen category; null or empty means the built-in default.</summary>
    public string? Category { get; set; }
    /// <summary>Creation time (UTC), set when the workspace is created. Null in files written before this field existed.</summary>
    public DateTime? CreatedAt { get; set; }
    /// <summary>Last time open requests were actually sent for this workspace (שגר). Null = never opened.</summary>
    public DateTime? LastOpenedAt { get; set; }
    public string EffectiveCategory => string.IsNullOrWhiteSpace(Category) ? DefaultCategory : Category;
    public List<Membership> Items { get; set; } = new();
}

public sealed class Store
{
    public int SchemaVersion { get; set; } = 3;
    public Dictionary<string, Item> Items { get; set; } = new();
    public List<Workspace> Workspaces { get; set; } = new();
}
