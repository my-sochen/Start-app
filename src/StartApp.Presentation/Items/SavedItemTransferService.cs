using StartApp.Core;
namespace StartApp.Presentation;

/// <summary>Immutable saved-source snapshot. Locations identify which saved instance supplied its effective fields.</summary>
public sealed record SavedItemTransfer(ItemType Type, string Name, string Target, string Description, LaunchConfig? Launch,
    string? WorkspaceId = null, string? InstanceId = null, GlobalListKind? ListKind = null, string? EntryId = null);

/// <summary>Copies saved item values without changing source memberships. Repository mutation owns atomicity.</summary>
public static class SavedItemTransferService
{
    public static void ValidateSource(Catalog c, SavedItemTransfer source)
    {
        bool current;
        if (source.ListKind is { } kind)
            current = c.GlobalEntries(kind).Any(e => e.Id == source.EntryId && e.Type == source.Type && e.Name == source.Name && e.Target == source.Target);
        else
        {
            var w = c.Store.Workspaces.FirstOrDefault(w => w.Id == source.WorkspaceId);
            var m = w?.Items.FirstOrDefault(m => (m.Id ?? m.ItemId) == source.InstanceId);
            current = m is not null && c.Store.Items.TryGetValue(m.ItemId, out var item) && item.Type == source.Type && item.Target == source.Target &&
                (m.Name ?? item.Name) == source.Name && (m.Description ?? item.Description ?? "") == source.Description && m.Launch == source.Launch;
        }
        if (!current) throw new ArgumentException("האייטם השתנה או הוסר. פתחו שוב את התפריט.");
        if (Catalog.ValidateInputTarget(source.Type, source.Target) is { } error) throw new ArgumentException(error);
    }
    public static IReadOnlyList<Workspace> Destinations(Store store, SavedItemTransfer source) => store.Workspaces.ToArray();

    public static bool IsSourceList(SavedItemTransfer source, GlobalListKind destination) => source.ListKind == destination;

    public static void AddToWorkspaces(Catalog c, SavedItemTransfer source, IReadOnlyList<string> ids)
    {
        ValidateSource(c, source);
        if (ids.Count == 0 || ids.Distinct().Count() != ids.Count) throw new ArgumentException("בחרו סביבה להוספה.");
        foreach (var id in ids)
            if (!c.Store.Workspaces.Any(w => w.Id == id))
                throw new ArgumentException("סביבת היעד הוסרה. פתחו שוב את הרשימה.");
        var item = c.GetOrAddItem(source.Type, source.Name, source.Target);
        foreach (var id in ids)
        {
            var member = c.AddToWorkspace(id, item.Id, source.Launch, source.Name);
            c.UpdateInstance(id, member.Id!, source.Name, source.Description);
        }
    }
    public static string AddToNewWorkspace(Catalog c, SavedItemTransfer source, string name)
    {
        ValidateSource(c, source);
        var w = c.CreateWorkspace(name);
        AddToWorkspaces(c, source, new[] { w.Id });
        return w.Id;
    }
    public static void AddToGlobalList(Catalog c, SavedItemTransfer source, GlobalListKind destination, bool confirmedMove = false)
    {
        ValidateSource(c, source);
        if (confirmedMove) c.MoveGlobalEntry(destination, source.Type, source.Name, source.Target);
        else c.AddGlobalEntry(destination, source.Type, source.Name, source.Target);
    }
}
