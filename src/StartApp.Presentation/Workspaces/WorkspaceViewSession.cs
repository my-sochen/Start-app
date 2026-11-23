using StartApp.Core;

namespace StartApp.Presentation;

/// <summary>Owns overview options/filter selections and their sorted projection/chips.
/// No binding collections, notifications, repository writes or window access.</summary>
public sealed class WorkspaceViewSession
{
    public ViewSort Sort { get; set; } = ViewSort.RecentEntries;
    public CardSizeOption CardSize { get; set; } = CardSizeOption.Medium;
    public ViewTemplate Template { get; set; } = ViewTemplate.Cards;
    public string ContainsText { get; set; } = "";
    public string? ItemId { get; set; }
    public string ItemLabel { get; set; } = "";
    public string ItemName { get; set; } = "";
    public HashSet<string> Categories { get; } = new(StringComparer.OrdinalIgnoreCase);
    public HashSet<string> Colors { get; } = new(StringComparer.OrdinalIgnoreCase);
    public bool HasActiveFilters => Categories.Count > 0 || Colors.Count > 0 || ItemId != null || ContainsText.Trim().Length > 0;
    public bool IsSingleContentFilter => ItemId != null && Categories.Count == 0 && Colors.Count == 0 && ContainsText.Trim().Length == 0;
    public IEnumerable<Workspace> Query(IEnumerable<Workspace> workspaces, Store store)
    {
        IEnumerable<Workspace> q = workspaces;
        if (Categories.Count > 0) q = q.Where(w => Categories.Contains(w.EffectiveCategory));
        if (Colors.Count > 0) q = q.Where(w => w.Color != null && Colors.Contains(w.Color));
        if (ItemId != null) q = q.Where(w => w.Items.Any(m => m.ItemId == ItemId));
        var text = ContainsText.Trim();
        if (text.Length > 0) q = q.Where(w => WorkspaceContains(w, store, text));
        q = Sort switch
        {
            ViewSort.CreationDate => q.OrderByDescending(w => w.CreatedAt ?? DateTime.MinValue),
            ViewSort.ItemCount => q.OrderByDescending(w => w.Items.Count),
            ViewSort.LaunchCount => q.OrderByDescending(w => w.LaunchCount),
            ViewSort.Alphabetical => q.OrderBy(w => w.Name, StringComparer.CurrentCulture),
            _ => q.OrderByDescending(w => w.LastOpenedAt ?? DateTime.MinValue),
        };
        return q;
    }

    public IEnumerable<FilterChip> Chips(string? capturedText = null)
    {
        var text = capturedText ?? ContainsText.Trim();
        foreach (var c in Colors.OrderBy(c => c, StringComparer.OrdinalIgnoreCase)) yield return new FilterChip("color:" + c, c, c);
        foreach (var c in Categories.OrderBy(c => c, StringComparer.CurrentCulture)) yield return new FilterChip("cat:" + c, c);
        if (ItemId != null) yield return new FilterChip("item", ItemLabel);
        if (text.Length > 0) yield return new FilterChip("contains", "מכיל: " + text);

    }

    private static bool WorkspaceContains(Workspace w, Store s, string text)
    {
        bool Has(string? v) => v != null && v.Contains(text, StringComparison.OrdinalIgnoreCase);
        if (Has(w.Name) || Has(w.Description)) return true;
        foreach (var m in w.Items)
            if (s.Items.TryGetValue(m.ItemId, out var it) && (Has(m.Name ?? it.Name) || Has(it.Target) || Has(m.Description ?? it.Description)))
                return true;
        return false;
    }

}
