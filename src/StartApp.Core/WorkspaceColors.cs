namespace StartApp.Core;

/// <summary>Shared workspace color rules. History retains first-choice order, independently of card lifetimes.</summary>
public static class WorkspaceColors
{
    public static string Normalize(string color)
    {
        if (string.IsNullOrEmpty(color) || Catalog.ValidateColor(color) != null)
            throw new ArgumentException("Color must be #RRGGBB.", nameof(color));
        return color.ToUpperInvariant();
    }

    public static void Remember(Store store, string color)
    {
        var normalized = Normalize(color);
        if (!store.AdvancedColorHistory.Contains(normalized, StringComparer.OrdinalIgnoreCase))
            store.AdvancedColorHistory.Add(normalized);
    }
}
