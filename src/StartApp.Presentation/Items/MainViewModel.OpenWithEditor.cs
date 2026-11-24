using System.Windows.Input;
using StartApp.Core;

namespace StartApp.Presentation;

public sealed partial class MainViewModel
{
    /// <summary>פתח באמצעות from the row actions: opens the open-with popup (פופאפ פתח באמצעות).</summary>
    public ICommand OpenWithCommand { get; private set; } = null!;
    /// <summary>פתח in the open-with popup: opens with the chosen program now and saves it when הגדר כברירת מחדל is checked.</summary>
    public ICommand OpenWithGoCommand { get; private set; } = null!;
    public ICommand CloseOpenWithCommand { get; private set; } = null!;

    private void InitOpenWithEditorCommands()
    {
        OpenWithCommand = Cmd(p => BeginOpenWith((ItemRow)p!), p => !ReadOnly && Selected != null && p is ItemRow { Type: not "Folder" });
        OpenWithGoCommand = Cmd(_ => OpenWithGo(), _ => OpenWithOpen && !ReadOnly && !IsChecking && !IsLaunching && !SelectionPending && !OpenDecisionPending);
        CloseOpenWithCommand = Cmd(_ => CloseOpenWith(), _ => OpenWithOpen);
    }

    // ---- open-with popup (פופאפ פתח באמצעות): browser for a site, program for a file; per-instance הגדר כברירת מחדל ----
    private readonly OpenWithSession<ItemRow, StagedRow> _openWith = new();
    public bool OpenWithOpen { get => _openWith.IsOpen; private set { if (_openWith.IsOpen != value) { _openWith.IsOpen = value; Raise(nameof(OpenWithOpen)); } RaiseCommands(); } }
    /// <summary>A site asks for a browser; files and folders ask for a program.</summary>
    public string OpenWithLabel => _openWith.SavedTarget?.Type == nameof(ItemType.Link) || _openWith.StagedTarget?.Type == ItemType.Link ? "Browser (full path to its exe); empty = Windows default" : "Program (full path to its exe); empty = Windows default";
    public string OpenWithProgram { get => _openWith.Program; set { var program = value ?? ""; if (_openWith.Program != program) { _openWith.Program = program; Raise(nameof(OpenWithProgram)); } RaiseCommands(); } }
    /// <summary>Save the choice for this instance only, never a Windows default or other workspaces.</summary>
    public bool OpenWithSetDefault { get => _openWith.SetDefault; set { if (_openWith.SetDefault != value) { _openWith.SetDefault = value; Raise(nameof(OpenWithSetDefault)); } } }
    private void BeginOpenWith(ItemRow row)
    {
        if (row.Type == nameof(ItemType.Folder)) return;
        if (row.Type == nameof(ItemType.Program)) { Status = "Open with does not apply to a program item (it is the program)"; return; }
        var m = Selected!.Items.FirstOrDefault(x => (x.Id ?? x.ItemId) == row.InstanceId);
        _openWith.SavedTarget = row; _openWith.StagedTarget = null;
        OpenWithProgram = m?.Launch?.Browser ?? m?.Launch?.OpenWith ?? "";
        OpenWithSetDefault = m?.Launch is not null;
        OpenWithOpen = true; Raise(nameof(OpenWithLabel));
    }
    private void OpenWithGo()
    {
        if (_openWith.StagedTarget is { } sg)
        {
            var prog2 = OpenWithProgram.Trim();
            LaunchConfig? cfg2 = prog2.Length == 0 ? null : sg.Type == ItemType.Link ? new LaunchConfig(Browser: prog2) : new LaunchConfig(OpenWith: prog2);
            if (OpenWithSetDefault) sg.OpenWith = prog2; // a staged default stays staged until שמור
            CloseOpenWith();
            var stagedItem = new Item("staged", sg.Type, sg.Name, sg.Target, string.IsNullOrWhiteSpace(sg.Description) ? null : sg.Description);
            OpenOneCore(sg.Name, stagedItem, cfg2);
            return;
        }
        if (_openWith.SavedTarget is null) return;
        var row = _openWith.SavedTarget; var prog = OpenWithProgram.Trim();
        LaunchConfig? cfg = prog.Length == 0 ? null : row.Type == nameof(ItemType.Link) ? new LaunchConfig(Browser: prog) : new LaunchConfig(OpenWith: prog);
        if (OpenWithSetDefault && !Apply(c => c.SetLaunch(Selected!.Id, row.InstanceId, cfg))) return;
        OpenWithOpen = false;
        var m = Selected!.Items.FirstOrDefault(x => (x.Id ?? x.ItemId) == row.InstanceId);
        if (m is null || !_current.Items.TryGetValue(m.ItemId, out var it)) { Status = row.Name + ": not opened (the saved item record is missing)"; return; }
        // This open uses the choice just made (empty = the Windows default for this open), whether or not it was saved.
        OpenOneCore(row.Name, it, cfg);
    }

}
