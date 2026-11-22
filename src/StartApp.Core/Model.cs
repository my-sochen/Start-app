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
    /// <summary>Built-in default category, used when none is chosen. Managed categories (custom add/edit/delete) live in
    /// <see cref="Store.CustomCategories"/> and Catalog; כללי is the one guaranteed built-in.</summary>
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
    /// <summary>How many times open requests were sent for this workspace (כמות שיגורים). 0 in files written before this field existed.</summary>
    public int LaunchCount { get; set; }
    /// <summary>Card design (פופאפ עיצוב כרטיס): user-chosen color as #RRGGBB; null = the app's default look. With an image it is
    /// the frame (thick top, thin other sides); without one it is the card background. Per workspace, never shared.</summary>
    public string? Color { get; set; }
    /// <summary>Card design (פופאפ עיצוב כרטיס): path of the background image the user uploaded; null = none. Stored as a path
    /// reference (copying the file into app storage is an open storage decision). Per workspace, never shared.</summary>
    public string? ImagePath { get; set; }
    public string EffectiveCategory => string.IsNullOrWhiteSpace(Category) ? DefaultCategory : Category;
    public List<Membership> Items { get; set; } = new();
}

public enum GlobalListKind { GlobalItems, Whitelist, Blacklist }

/// <summary>One row of a סביבה גלובלית list (approved 22.11 03:08): an item that opens with the computer start (GlobalItems),
/// or a capture rule (Whitelist = capture display priority only, Blacklist = never shown in any capture, including הוסף ממה שפתוח).
/// IsDefault marks the shipped blacklist defaults (Chrome, Edge, Start-app) that are removable; removing one returns it to captures.</summary>
public sealed class GlobalListEntry
{
    public string Id { get; set; } = "";
    public ItemType Type { get; set; }
    public string Name { get; set; } = "";
    public string Target { get; set; } = "";
    public DateTime? AddedAt { get; set; }
    public bool IsDefault { get; set; }
}

public sealed class Store
{
    public int SchemaVersion { get; set; } = 5;
    public Dictionary<string, Item> Items { get; set; } = new();
    public List<Workspace> Workspaces { get; set; } = new();
    /// <summary>פריטי הסביבה הגלובלית: opened automatically with the computer start, always (schema 5+). Missing on older files = none.</summary>
    public List<GlobalListEntry> GlobalItems { get; set; } = new();
    /// <summary>רשימה לבנה ללכידה: always shown at the top of capture results, even when not detected open (schema 5+).</summary>
    public List<GlobalListEntry> CaptureWhitelist { get; set; } = new();
    /// <summary>רשימה שחורה ללכידה: never shown in any capture, shared by all workspaces (schema 5+). The constructor seeds the
    /// removable defaults (Chrome, Edge, Start-app); a saved file that has the property (even empty) replaces them, so a removed default stays removed.</summary>
    public List<GlobalListEntry> CaptureBlacklist { get; set; } = new()
    {
        new GlobalListEntry { Id = "ge_default_chrome", Type = ItemType.Program, Name = "Chrome", Target = "chrome.exe", IsDefault = true },
        new GlobalListEntry { Id = "ge_default_edge", Type = ItemType.Program, Name = "Edge", Target = "msedge.exe", IsDefault = true },
        new GlobalListEntry { Id = "ge_default_startapp", Type = ItemType.Program, Name = "Start-app", Target = "StartApp.App.exe", IsDefault = true },
    };
    /// <summary>Custom categories the user created (schema 4+). Built-ins are defined in code (Catalog.BuiltInCategories), are never
    /// stored here, and cannot be edited or deleted; custom ones can. Missing/null on older files = none.</summary>
    public List<string> CustomCategories { get; set; } = new();
    /// <summary>סביבת ברירת המחדל (green-dot badge on its card, approved 22.11 handoff): the one workspace marked as
    /// default, or null/empty for "ללא סביבת ברירת מחדל". Missing on older files = none. Setting it from the אופציות menu
    /// and opening it automatically at computer startup are follow-up product work; the badge renders from this field only.</summary>
    public string? DefaultWorkspaceId { get; set; }
}
