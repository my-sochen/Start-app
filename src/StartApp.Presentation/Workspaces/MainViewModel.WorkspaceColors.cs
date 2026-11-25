using System.Windows.Input;
using StartApp.Core;

namespace StartApp.Presentation;

public sealed record AdvancedColorChoice(string Target, string Color);

public sealed partial class MainViewModel
{
    public ICommand ApplyAdvancedColorCommand { get; private set; } = null!;
    public bool CanChooseWorkspaceColor => !ReadOnly;
    public IReadOnlyList<string> AdvancedColorOptions => FilterColorOptions.Where(c => !DesignPalette.Contains(c, StringComparer.OrdinalIgnoreCase)).ToArray();
    public bool HasAdvancedColorOptions => AdvancedColorOptions.Count > 0;
    private void InitWorkspaceColorCommands()
    {
        ApplyAdvancedColorCommand = Cmd(p =>
        {
            if (p is not AdvancedColorChoice choice || !CanApplyAdvancedColor(choice)) return;
            var color = WorkspaceColors.Normalize(choice.Color);
            if (choice.Target == "design") { DesignColor = color; _cardDesign.AdvancedColorToRemember = color; }
            else { NewWorkspaceColor = color; _workspaceCreate.AdvancedColorToRemember = color; }
        }, p => p is AdvancedColorChoice choice && CanApplyAdvancedColor(choice));
    }
    // History and the final card value commit together. Failed saves retain the pending choice for retry.
    private static void RememberAdvancedColor(Catalog catalog, string? pending, string savedColor)
    {
        if (pending != null && string.Equals(pending, savedColor, StringComparison.OrdinalIgnoreCase))
            WorkspaceColors.Remember(catalog.Store, pending);
    }
    private bool CanApplyAdvancedColor(AdvancedColorChoice choice) => !ReadOnly &&
        (choice.Target == "create" || choice.Target == "design" && DesignEditOpen) &&
        !string.IsNullOrEmpty(choice.Color) && Catalog.ValidateColor(choice.Color) == null;
}
