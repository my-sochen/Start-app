using System.Collections.ObjectModel;
using System.Windows.Input;
using StartApp.Core;

namespace StartApp.Presentation;

/// <summary>סביבה גלובלית tab (approved 22.11 03:08, visual global-env-f1): one header tab opening a page with three
/// groups — פריטי הסביבה הגלובלית (open with the computer start, always), רשימה לבנה ללכידה (capture display priority
/// ONLY, settled 03:13 "רק עדיפות ללכידה") and רשימה שחורה ללכידה (never shown in any capture, removable defaults).
/// The page reads from the saved store on every open and persists each change immediately.</summary>
public sealed partial class MainViewModel
{
    public sealed class GlobalEntryRow : ObservableBase
    {
        public GlobalEntryRow(GlobalListKind kind, GlobalListEntry entry) { Kind = kind; Entry = entry; }
        public GlobalListKind Kind { get; }
        public GlobalListEntry Entry { get; }
        public string Id => Entry.Id;
        public string Name => Entry.Name;
        public string Target => Entry.Target;
        public string TypeLabel => Entry.Type switch { ItemType.Link => "אתר", ItemType.Program => "תוכנה", ItemType.Folder => "תיקייה", _ => "קובץ" };
        public string IconLetter => string.IsNullOrEmpty(Entry.Name) ? "?" : Entry.Name.Trim()[0].ToString().ToUpperInvariant();
        public string AddedDisplay => Entry.AddedAt is { } a ? $"נוסף ב-{a.ToLocalTime():dd.MM}" : "";
        public string DescriptionLine => Entry.IsDefault ? TypeLabel + " · ברירת מחדל, ניתן להסרה" : string.IsNullOrEmpty(AddedDisplay) ? TypeLabel : TypeLabel + " · " + AddedDisplay;
    }

    private bool _globalConflictOpen;
    public bool GlobalConflictOpen { get => _globalConflictOpen; private set => Set(ref _globalConflictOpen, value); }
    private string _globalConflictMessage = "";
    public string GlobalConflictMessage { get => _globalConflictMessage; private set => Set(ref _globalConflictMessage, value); }
    public ICommand CloseGlobalConflictCommand { get; private set; } = null!;
    public ICommand MoveGlobalConflictCommand { get; private set; } = null!;
    private bool _inGlobalEnv;
    public bool InGlobalEnv { get => _inGlobalEnv; private set { Set(ref _inGlobalEnv, value); RaiseCommands(); } }

    public ObservableCollection<GlobalEntryRow> GlobalItemRows { get; } = new();
    public ObservableCollection<GlobalEntryRow> WhitelistRows { get; } = new();
    public ObservableCollection<GlobalEntryRow> BlacklistRows { get; } = new();

    public ICommand OpenGlobalEnvCommand { get; private set; } = null!;
    public ICommand CloseGlobalEnvCommand { get; private set; } = null!;
    public ICommand StartGlobalAddCommand { get; private set; } = null!;
    public ICommand ConfirmGlobalAddCommand { get; private set; } = null!;
    public ICommand CancelGlobalAddCommand { get; private set; } = null!;
    public ICommand RemoveGlobalEntryCommand { get; private set; } = null!;

    private GlobalListKind? _addingKind;
    /// <summary>Which group's inline add form is open (הוסף אייטם), null when none.</summary>
    public GlobalListKind? AddingKind { get => _addingKind; private set { Set(ref _addingKind, value); Raise(nameof(AddFormVisible)); Raise(nameof(AddFormListTitle)); RaiseCommands(); } }
    public bool AddFormVisible => _addingKind != null;
    public string AddFormListTitle => _addingKind switch
    {
        GlobalListKind.GlobalItems => "הוספה לפריטי הסביבה הגלובלית",
        GlobalListKind.Whitelist => "הוספה לרשימה הלבנה ללכידה",
        GlobalListKind.Blacklist => "הוספה לרשימה השחורה ללכידה",
        _ => "",
    };

    public static IReadOnlyList<string> GlobalEntryTypeLabels { get; } = new[] { "אתר", "תוכנה", "קובץ", "תיקייה" };
    private int _newEntryTypeIndex;
    public int NewEntryTypeIndex { get => _newEntryTypeIndex; set { Set(ref _newEntryTypeIndex, value); } }
    private string _newEntryName = "";
    public string NewEntryName { get => _newEntryName; set { Set(ref _newEntryName, value); RaiseCommands(); } }
    private string _newEntryTarget = "";
    public string NewEntryTarget { get => _newEntryTarget; set { Set(ref _newEntryTarget, value); RaiseCommands(); } }
    private string _globalEnvError = "";
    public string GlobalEnvError { get => _globalEnvError; private set { Set(ref _globalEnvError, value); Raise(nameof(GlobalEnvErrorVisible)); } }
    public bool GlobalEnvErrorVisible => !string.IsNullOrEmpty(_globalEnvError);

    private void InitGlobalEnvCommands()
    {
        CloseGlobalConflictCommand = Cmd(_ => GlobalConflictOpen = false, _ => GlobalConflictOpen);
        MoveGlobalConflictCommand = Cmd(_ => {
            if (_addingKind is not { } kind) return;
            try {
                var saved = _repo.Mutate(c => c.MoveGlobalEntry(kind, GlobalEntryType(_newEntryTypeIndex), _newEntryName, _newEntryTarget));
                Show(saved); RefreshGlobalEnvRows(); GlobalConflictOpen = false; AddingKind = null; GlobalEnvError = "";
            } catch (Exception e) when (e is ArgumentException or InvalidOperationException) { GlobalEnvError = e.Message; GlobalConflictOpen = false; }
        }, _ => GlobalConflictOpen);
        OpenGlobalEnvCommand = Cmd(_ => { InAllItems = false; InSettings = false; RefreshGlobalEnvRows(); InGlobalEnv = true; }, _ => !_inGlobalEnv);
        CloseGlobalEnvCommand = Cmd(_ => { InGlobalEnv = false; AddingKind = null; GlobalEnvError = ""; }, _ => _inGlobalEnv);
        StartGlobalAddCommand = Cmd(p => { if (p is GlobalListKind k) { AddingKind = k; NewEntryName = ""; NewEntryTarget = ""; NewEntryTypeIndex = k == GlobalListKind.Blacklist ? 1 : 0; GlobalEnvError = ""; } }, _ => _inGlobalEnv);
        ConfirmGlobalAddCommand = Cmd(_ => ConfirmGlobalAdd(), _ => _addingKind != null && !string.IsNullOrWhiteSpace(_newEntryName) && !string.IsNullOrWhiteSpace(_newEntryTarget));
        CancelGlobalAddCommand = Cmd(_ => { AddingKind = null; GlobalEnvError = ""; }, _ => _addingKind != null);
        RemoveGlobalEntryCommand = Cmd(p => { if (p is GlobalEntryRow row) RemoveGlobalEntry(row); }, p => p is GlobalEntryRow);
    }

    private static ItemType GlobalEntryType(int index) => index switch { 0 => ItemType.Link, 1 => ItemType.Program, 2 => ItemType.File, _ => ItemType.Folder };

    private void ConfirmGlobalAdd()
    {
        if (_addingKind is not { } kind) return;
        try
        {
            var saved = _repo.Mutate(c => c.AddGlobalEntry(kind, GlobalEntryType(_newEntryTypeIndex), _newEntryName, _newEntryTarget));
            Show(saved);
            RefreshGlobalEnvRows();
            AddingKind = null;
        }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException)
        {
            GlobalEnvError = "לא ניתן להוסיף: " + e.Message;
            if (e.Message is Catalog.WhitelistConflictError or Catalog.BlacklistConflictError)
            { GlobalConflictMessage = e.Message; GlobalConflictOpen = true; }
        }
    }

    private void RemoveGlobalEntry(GlobalEntryRow row)
    {
        var saved = _repo.Mutate(c => c.RemoveGlobalEntry(row.Kind, row.Id));
        Show(saved);
        RefreshGlobalEnvRows();
    }

    /// <summary>--autostart (logon via the installer's Run key): opens every פריט סביבה גלובלית through the same honest
    /// preflight the interactive open uses. Anything not opened is named on the status line, never silently skipped.</summary>
    public void OpenGlobalItemsOnStart()
    {
        var entries = _current.GlobalItems;
        if (entries.Count == 0) { Status = "אין פריטי סביבה גלובלית לפתיחה"; return; }
        var opened = 0; var blocked = new List<string>();
        foreach (var e in entries)
        {
            var it = new Item(e.Id, e.Type, e.Name, e.Target);
            var f = PreflightRules.CheckForm(it, null) ?? _preflight?.Check(it, null) ?? new PreflightFinding(PreflightState.Ready);
            if (f.State != PreflightState.Ready) { blocked.Add(e.Name); continue; }
            var r = _launcher.Launch(it, null);
            if (r.Status == LaunchStatus.Started) opened++; else blocked.Add(e.Name);
        }
        Status = blocked.Count == 0
            ? "נפתחו " + opened + " פריטי סביבה גלובלית"
            : "נפתחו " + opened + "; לא ניתן לפתוח: " + string.Join(", ", blocked);
    }

    private void RefreshGlobalEnvRows()
    {
        static void Fill(ObservableCollection<GlobalEntryRow> rows, GlobalListKind kind, IReadOnlyList<GlobalListEntry> entries)
        {
            rows.Clear();
            foreach (var e in entries) rows.Add(new GlobalEntryRow(kind, e));
        }
        Fill(GlobalItemRows, GlobalListKind.GlobalItems, _current.GlobalItems);
        Fill(WhitelistRows, GlobalListKind.Whitelist, _current.CaptureWhitelist);
        Fill(BlacklistRows, GlobalListKind.Blacklist, _current.CaptureBlacklist);
    }
}
