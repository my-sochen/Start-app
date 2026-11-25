namespace StartApp.Core;

/// <summary>Shared workspace color rules. History retains first-choice order, independently of card lifetimes.</summary>
public static class WorkspaceColors
{
    public static IReadOnlyList<string> BasePalette { get; } = Array.AsReadOnly(new[]
    {
        "#7189BB", "#8BAA9D", "#B4A0C5", "#E6CE9A", "#D6AEB5", "#D9DEE6",
        "#90B5B8", "#D9B390", "#C78F87"
    });
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
