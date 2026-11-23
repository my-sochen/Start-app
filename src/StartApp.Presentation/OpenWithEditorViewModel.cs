using StartApp.Core;

namespace StartApp.Presentation;

public sealed partial class MainViewModel
{
    // ---- open-with popup (פופאפ פתח באמצעות): browser for a site, program for a file; per-instance הגדר כברירת מחדל ----
    public bool OpenWithOpen { get => _openWithOpen; private set { Set(ref _openWithOpen, value); RaiseCommands(); } }
    private bool _openWithOpen;
    private ItemRow? _openWithRow;
    private StagedRow? _openWithStaged;
    /// <summary>Which program the popup asks for: a browser for a site, a program for a file/folder.</summary>
    public string OpenWithLabel => _openWithRow?.Type == nameof(ItemType.Link) || _openWithStaged?.Type == ItemType.Link ? "Browser (full path to its exe); empty = Windows default" : "Program (full path to its exe); empty = Windows default";
    public string OpenWithProgram { get => _openWithProgram; set { Set(ref _openWithProgram, value ?? ""); RaiseCommands(); } }
    private string _openWithProgram = "";
    /// <summary>הגדר כברירת מחדל: save the choice for THIS instance only (never a Windows default, never other workspaces).</summary>
    public bool OpenWithSetDefault { get => _openWithSetDefault; set => Set(ref _openWithSetDefault, value); }
    private bool _openWithSetDefault;
    private void BeginOpenWith(ItemRow row)
    {
        if (row.Type == nameof(ItemType.Program)) { Status = "Open with does not apply to a program item (it is the program)"; return; }
        var m = Selected!.Items.FirstOrDefault(x => (x.Id ?? x.ItemId) == row.InstanceId);
        _openWithRow = row; _openWithStaged = null;
        OpenWithProgram = m?.Launch?.Browser ?? m?.Launch?.OpenWith ?? "";
        OpenWithSetDefault = m?.Launch is not null;
        OpenWithOpen = true; Raise(nameof(OpenWithLabel));
    }
    private void OpenWithGo()
    {
        if (_openWithStaged is { } sg)
        {
            var prog2 = OpenWithProgram.Trim();
            LaunchConfig? cfg2 = prog2.Length == 0 ? null : sg.Type == ItemType.Link ? new LaunchConfig(Browser: prog2) : new LaunchConfig(OpenWith: prog2);
            if (OpenWithSetDefault) sg.OpenWith = prog2; // a staged default stays staged until שמור
            CloseOpenWith();
            var stagedItem = new Item("staged", sg.Type, sg.Name, sg.Target, string.IsNullOrWhiteSpace(sg.Description) ? null : sg.Description);
            OpenOneCore(sg.Name, stagedItem, cfg2);
            return;
        }
        if (_openWithRow is null) return;
        var row = _openWithRow; var prog = OpenWithProgram.Trim();
        LaunchConfig? cfg = prog.Length == 0 ? null : row.Type == nameof(ItemType.Link) ? new LaunchConfig(Browser: prog) : new LaunchConfig(OpenWith: prog);
        if (OpenWithSetDefault && !Apply(c => c.SetLaunch(Selected!.Id, row.InstanceId, cfg))) return;
        OpenWithOpen = false;
        var m = Selected!.Items.FirstOrDefault(x => (x.Id ?? x.ItemId) == row.InstanceId);
        if (m is null || !_current.Items.TryGetValue(m.ItemId, out var it)) { Status = row.Name + ": not opened (the saved item record is missing)"; return; }
        // This open uses the choice just made (empty = the Windows default for this open), whether or not it was saved.
        OpenOneCore(row.Name, it, cfg);
    }

}
