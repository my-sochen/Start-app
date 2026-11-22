namespace StartApp.Core;

/// <summary>Matching rules for the סביבה גלובלית capture lists (approved 22.11 03:08, whitelist scope settled 03:13:
/// "רק עדיפות ללכידה" — display priority only). Captured targets are usually full paths while list rows are often bare
/// program names (chrome.exe), so matching normalizes to the final path segment and compares case-insensitively; a same-type
/// name match also counts. Links match on their full normalized target, not a file segment.</summary>
public static class GlobalEnvRules
{
    public static bool Matches(GlobalListEntry entry, ItemType type, string name, string target)
    {
        if (entry.Type == type && string.Equals(entry.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase)) return true;
        if (entry.Type != type) return false;
        return string.Equals(Normalize(entry.Type, entry.Target), Normalize(type, target), StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsBlacklisted(IReadOnlyList<GlobalListEntry> blacklist, CapturedItem item) =>
        blacklist.Any(e => Matches(e, item.Type, item.Name, item.Target));

    /// <summary>Whitelist rows not already in the scan, to pin at the top of the capture results ("יוצגו תמיד בראש תוצאות
    /// הלכידה, גם אם לא זוהו פתוחים"). Priority only: pinning never checks a row the user did not check.</summary>
    public static IReadOnlyList<CapturedItem> WhitelistPins(IReadOnlyList<GlobalListEntry> whitelist, IReadOnlyList<CapturedItem> survivingScan) =>
        whitelist
            .Where(w => !survivingScan.Any(i => Matches(w, i.Type, i.Name, i.Target)))
            .Select(w => new CapturedItem(w.Type, w.Name, w.Target))
            .ToList();

    private static string Normalize(ItemType type, string target)
    {
        var t = (target ?? "").Trim().Trim('"');
        if (type == ItemType.Link) return t.TrimEnd('/');
        var i = Math.Max(t.LastIndexOf('\\'), t.LastIndexOf('/'));
        return i >= 0 ? t[(i + 1)..] : t;
    }
}
