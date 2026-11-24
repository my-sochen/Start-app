using System.Windows.Input;
using StartApp.Core;

namespace StartApp.Presentation;

public sealed partial class MainViewModel
{
    public ICommand CreateWorkspaceCommand { get; private set; } = null!;
    /// <summary>Create-form design (optional image/color at creation, spec Nov 12 morning): the view opens the same file
    /// dialog as עצב כרטיס and assigns NewWorkspaceImagePath.</summary>
    public ICommand PickCreateImageCommand { get; private set; } = null!;
    public ICommand RemoveCreateImageCommand { get; private set; } = null!;
    public ICommand ApplyCreatePaletteColorCommand { get; private set; } = null!;

    private void InitWorkspaceCreateCommands()
    {
        CreateWorkspaceCommand = Cmd(_ => Create(), _ => !ReadOnly && !string.IsNullOrWhiteSpace(NewName));
    }

    private void InitWorkspaceCreateDesignCommands()
    {
        PickCreateImageCommand = Cmd(_ => PickCreateImageRequested?.Invoke(), _ => !ReadOnly);
        RemoveCreateImageCommand = Cmd(_ => NewWorkspaceImagePath = "", _ => NewWorkspaceImagePath.Length > 0 && !ReadOnly);
        ApplyCreatePaletteColorCommand = Cmd(p => NewWorkspaceColor = (string)p!, p => p is string && !ReadOnly);
    }

}
