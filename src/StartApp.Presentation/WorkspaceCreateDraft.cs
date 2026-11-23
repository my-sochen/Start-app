using StartApp.Core;

namespace StartApp.Presentation;

/// <summary>Owns new-workspace form values, exact dirty state and detached card preview.
/// No repository, commands, binding notifications or navigation access.</summary>
public sealed class WorkspaceCreateDraft
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string Category { get; set; } = Workspace.DefaultCategory;
    public string Color { get; set; } = "";
    public string ImagePath { get; set; } = "";
    public bool IsDirty => Name.Length > 0 || Description.Length > 0 || Category != Workspace.DefaultCategory || Color.Length > 0 || ImagePath.Length > 0;
    public Workspace Preview => new()
    {
        Name = string.IsNullOrWhiteSpace(Name) ? "פרויקט חדש" : Name,
        Category = Category,
        Color = string.IsNullOrWhiteSpace(Color) ? "#7189BB" : Color,
        ImagePath = ImagePath
    };
}
