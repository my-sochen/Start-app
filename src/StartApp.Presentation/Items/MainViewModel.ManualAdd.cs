using System.Windows.Input;
using StartApp.Core;

namespace StartApp.Presentation;

public sealed partial class MainViewModel
{
    /// <summary>The הוסף אייטם button in the empty state; the view focuses the add form.</summary>
    public ICommand FocusAddCommand { get; private set; } = null!;
    public ICommand AddItemCommand { get; private set; } = null!;
    public ICommand ConfirmDuplicateCommand { get; private set; } = null!;
    public ICommand CancelDuplicateCommand { get; private set; } = null!;

    private void InitManualAddFocusCommand()
    {
        FocusAddCommand = Cmd(_ => FocusAddRequested?.Invoke(), _ => InDetails);
    }

    private void InitManualAddCommands()
    {
        AddItemCommand = Cmd(_ => AddItem(), _ => !ReadOnly && Selected != null && NewTarget.Length > 0);
        ConfirmDuplicateCommand = Cmd(_ => { _manualAdd.DuplicatePending = false; DuplicateMessage = ""; AddItem(force: true); }, _ => _manualAdd.DuplicatePending);
        CancelDuplicateCommand = Cmd(_ => ClearDup("Add cancelled, nothing was added"), _ => _manualAdd.DuplicatePending);
    }

}
