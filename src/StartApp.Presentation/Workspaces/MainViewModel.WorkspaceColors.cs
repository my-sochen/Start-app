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
            if (choice.Target == "design") DesignColor = choice.Color.ToUpperInvariant();
            else NewWorkspaceColor = choice.Color.ToUpperInvariant();
        }, p => p is AdvancedColorChoice choice && CanApplyAdvancedColor(choice));
    }
    private bool CanApplyAdvancedColor(AdvancedColorChoice choice) => !ReadOnly &&
        (choice.Target == "create" || choice.Target == "design" && DesignEditOpen) &&
        !string.IsNullOrEmpty(choice.Color) && Catalog.ValidateColor(choice.Color) == null;
}
