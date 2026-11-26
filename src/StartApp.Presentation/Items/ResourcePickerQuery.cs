using StartApp.Core;

namespace StartApp.Presentation;

/// <summary>Read-only catalog query for the resource picker. No session, commands, UI or persistence writes.</summary>
public static class ResourcePickerQuery
{
    public sealed record Result(string ItemId, string Name, string Target, int Usage, ItemType Type);

    /// <summary>Catalog names, exact item identity usage, type-local search and stable usage/name ordering.</summary>
    public static IEnumerable<Result> Read(Store store, ItemType type, string search)
    {
        int UsageOf(string itemId) => store.Workspaces.Count(w => w.Items.Any(m => m.ItemId == itemId));
        return store.Items.Values
            .Where(i => i.Type == type)
            .Where(i => search.Length == 0 || i.Name.Contains(search, StringComparison.OrdinalIgnoreCase) || i.Target.Contains(search, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(i => UsageOf(i.Id)).ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase)
            .Select(it => new Result(it.Id, it.Name, it.Target, UsageOf(it.Id), it.Type));
    }
}
