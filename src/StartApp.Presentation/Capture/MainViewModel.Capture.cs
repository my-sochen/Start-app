using System.Collections.ObjectModel;
using System.Windows.Input;
using StartApp.Core;

namespace StartApp.Presentation;

public sealed partial class MainViewModel
{
    // ---- capture (לכידה, settled Nov 12 07:14-07:45): scan what is open now, show it read-only (checkbox, original name, address under it),
    // add to a NEW workspace (create form, items checked by default) or to an EXISTING one side by side. Staged additions are NOT saved until
    // שמור; the snapshot survives workspace changes and saves; X resets it. Duplicates: one warning per drag, one grouped dialog for add-all/selected. ----
    public static string TypeName(ItemType type) => type switch { ItemType.Link => "אתר", ItemType.Program => "תוכנה", ItemType.File => "קובץ", _ => "תיקייה" };
    public enum CaptureStage { None, NewForm, PickWorkspace, SideBySide }
    /// <summary>One item already saved in the workspace on the side-by-side screen (read-only here).</summary>
    public sealed record CaptureExistingRow(string Name, string Target, string Group, string Type = "File");
    /// <summary>One duplicate in the grouped dialog; the user ticks which ones to add anyway (none ticked by default).</summary>
    public sealed class DupChoice : ObservableBase
    {
        private bool _checked;
        public DupChoice(CaptureRow row) { Row = row; }
        public CaptureRow Row { get; }
        /// <summary>Same site captured in two browsers produces two same-name duplicate rows (identity stays Type+Target,
        /// settled); the browser suffix - the same one the scan list shows - is what lets the user tell them apart here.
        /// Without it both read 'Add again: example.com' and the Chrome one cannot be picked knowingly (P2, Trello ObGZE66t).</summary>
        public string Label => "הוסף שוב: " + Row.Name + Row.BrowserSuffix;
        public bool IsChecked { get => _checked; set => Set(ref _checked, value); }
    }

    public ObservableCollection<CaptureRow> CaptureRows { get; } = new();
    public ObservableCollection<string> CaptureNotes { get; } = new();
    public ObservableCollection<Workspace> CaptureWorkspaceResults { get; } = new();
    public ObservableCollection<CaptureExistingRow> CaptureWorkspaceItems { get; } = new();
    private readonly StagedItemCollection<StagedRow> _stagedItems = new(row => row.Group);
    public ObservableCollection<StagedRow> StagedRows => _stagedItems.Rows;
    private readonly CaptureDuplicateSession<CaptureRow, DupChoice> _captureDuplicates = new();
    public ObservableCollection<DupChoice> CaptureDupChoices => _captureDuplicates.Choices;
    private readonly CaptureSessionState _captureSession = new();
    public CaptureStage CaptureStageNow { get => _captureSession.Stage; private set { if (_captureSession.Stage != value) { _captureSession.Stage = value; Raise(nameof(CaptureStageNow)); } Raise(nameof(CaptureOpen)); Raise(nameof(IsCaptureNew)); Raise(nameof(IsCapturePick)); Raise(nameof(IsCaptureSide)); Raise(nameof(CaptureDirty)); Raise(nameof(CaptureTargetHeader)); RaiseCommands(); } }
    public bool CaptureOpen => _captureSession.IsOpen;
    public bool IsCaptureNew => _captureSession.IsNew;
    public bool IsCapturePick => _captureSession.IsPick;
    public bool IsCaptureSide => _captureSession.IsSide;
    public bool CaptureEmpty => CaptureOpen && !IsScanning && CaptureRows.Count == 0;
    /// <summary>True while a scan runs in the background; the capture screen is already up and shows a scanning state.</summary>
    public bool IsScanning { get => _captureSession.Scanning; private set { if (_captureSession.Scanning != value) { _captureSession.Scanning = value; Raise(nameof(IsScanning)); } Raise(nameof(CaptureEmpty)); RaiseCommands(); } }
    /// <summary>The in-flight background scan; tests await it. Null until the first capture entry.</summary>
    public Task? CurrentScan { get; private set; }
    /// <summary>Hard bound for one scan: a source stuck in COM/UI Automation is abandoned after this, never waited on forever.</summary>
    public TimeSpan ScanTimeout { get; set; } = TimeSpan.FromSeconds(10);
    /// <summary>Anti-flicker floor so the scanning state is actually visible (and steady) on fast scans.</summary>
    public TimeSpan ScanMinVisible { get; set; } = TimeSpan.FromMilliseconds(700);
    private readonly CaptureScanLifetime _captureScan = new();
    /// <summary>Without ticked rows the button adds everything; with ticked rows it becomes הוסף נבחרים (settled).</summary>
    public string CaptureAddLabel
    {
        get
        {
            var count = CaptureRows.Count(r => r.IsChecked);
            return count == 0 ? "הוסף הכל ←" : count == 1 ? "הוסף נבחר אחד ←" : $"הוסף {count} נבחרים ←";
        }
    }
    public string CaptureSelectionText => CaptureRows.Count(r => r.IsChecked) + " נבחרו";
    public string CaptureTargetHeader => IsCaptureSide ? "היעד: " + CaptureWorkspaceName : IsCaptureNew ? "סביבה חדשה" : "בחירת סביבה";
    public string CaptureWorkspaceName => _current.Workspaces.FirstOrDefault(w => w.Id == _captureSession.WorkspaceId)?.Name ?? "";
    public string CaptureSearch { get => _captureSession.Search; set { var text = value ?? ""; if (_captureSession.Search != text) { _captureSession.Search = text; Raise(nameof(CaptureSearch)); } RefreshCaptureResults(); } }
    /// <summary>Unsaved capture work: staged additions on the side-by-side screen, or a filled-in new-workspace form.</summary>
    public bool CaptureDirty => _captureSession.IsDirty(StagedRows.Count, CreateDirty);
    public bool UnsavedForCapture { get => _unsavedExit.ForCapture; private set { if (_unsavedExit.ForCapture != value) { _unsavedExit.ForCapture = value; Raise(nameof(UnsavedForCapture)); } } }
    public bool CaptureDupSinglePending => _captureDuplicates.SinglePending;
    public string CaptureDupSingleMessage { get => _captureDuplicates.SingleMessage; private set { if (_captureDuplicates.SingleMessage != value) { _captureDuplicates.SingleMessage = value; Raise(nameof(CaptureDupSingleMessage)); } } }
    public bool CaptureDupGroupPending { get => _captureDuplicates.GroupPending; private set { if (_captureDuplicates.GroupPending != value) { _captureDuplicates.GroupPending = value; Raise(nameof(CaptureDupGroupPending)); } RaiseCommands(); } }
    public ICommand CaptureNewCommand { get; private set; } = null!;
    public ICommand CaptureExistingCommand { get; private set; } = null!;
    public ICommand AddFromOpenCommand { get; private set; } = null!;
    public ICommand CapturePickWorkspaceCommand { get; private set; } = null!;
    public ICommand CaptureAddCommand { get; private set; } = null!;
    public ICommand CaptureDropCommand { get; private set; } = null!;
    public ICommand CaptureSaveCommand { get; private set; } = null!;
    public ICommand CaptureBackCommand { get; private set; } = null!;
    public ICommand CaptureCloseCommand { get; private set; } = null!;
    public ICommand CaptureCreateCommand { get; private set; } = null!;
    public ICommand RemoveStagedCommand { get; private set; } = null!;
    public ICommand EditStagedCommand { get; private set; } = null!;
    public ICommand CloseStagedEditCommand { get; private set; } = null!;
    public ICommand StagedCopyAddressCommand { get; private set; } = null!;
    public ICommand StagedMoveUpCommand { get; private set; } = null!;
    public ICommand StagedMoveDownCommand { get; private set; } = null!;
    public ICommand StagedOpenWithCommand { get; private set; } = null!;
    public ICommand StagedEditAddressCommand { get; private set; } = null!;
    public ICommand ConfirmCaptureDupSingleCommand { get; private set; } = null!;
    public ICommand CancelCaptureDupSingleCommand { get; private set; } = null!;
    public ICommand ConfirmCaptureDupGroupCommand { get; private set; } = null!;
    public ICommand CancelCaptureDupGroupCommand { get; private set; } = null!;
    private bool CaptureIdle => _captureDuplicates.IsIdle;
    private void InitCaptureCommands()
    {
        CaptureNewCommand = Cmd(_ => StartCapture(CaptureStage.NewForm, true), _ => !ReadOnly && !CaptureOpen && InOverview && !IsChecking && !IsLaunching && !SelectionPending && !OpenDecisionPending);
        CaptureExistingCommand = Cmd(_ => StartCapture(CaptureStage.PickWorkspace, false), _ => !ReadOnly && !CaptureOpen && InOverview && !IsChecking && !IsLaunching && !SelectionPending && !OpenDecisionPending);
        OpenSettingsCommand = Cmd(_ => { InAllItems = false; InGlobalEnv = false; InSettings = true; }, _ => !_inSettings);
        CloseSettingsCommand = Cmd(_ => InSettings = false, _ => _inSettings);
        AddFromOpenCommand = Cmd(_ => StartCapture(CaptureStage.SideBySide, false, Selected!.Id), _ => !ReadOnly && !CaptureOpen && InDetails && Selected != null && !IsChecking && !IsLaunching && !SelectionPending && !OpenDecisionPending);
        CapturePickWorkspaceCommand = Cmd(p => EnterSideBySide(((Workspace)p!).Id), p => p is Workspace && _captureSession.Stage == CaptureStage.PickWorkspace);
        CaptureAddCommand = Cmd(_ => AddCaptured(), _ => IsCaptureSide && CaptureRows.Count > 0 && CaptureIdle && !ReadOnly);
        CaptureDropCommand = Cmd(p => DropCaptured((CaptureRow)p!), p => p is CaptureRow && IsCaptureSide && CaptureIdle && !ReadOnly);
        CaptureSaveCommand = Cmd(_ => SaveStaged(), _ => IsCaptureSide && StagedRows.Count > 0 && CaptureIdle && !ReadOnly);
        CaptureBackCommand = Cmd(_ => CaptureBack(), _ => IsCaptureSide && CaptureIdle);
        CaptureCloseCommand = Cmd(_ => CaptureClose(), _ => CaptureOpen && CaptureIdle);
        CaptureCreateCommand = Cmd(_ => CreateFromCapture(), _ => IsCaptureNew && NewName.Length > 0 && !ReadOnly && !IsScanning);
        // Only an explicit per-row action expands editing. Switching preserves unsaved row values.
        EditStagedCommand = Cmd(p =>
        {
            foreach (var row in StagedRows) row.IsEditing = ReferenceEquals(row, p);
        }, p => p is StagedRow row && StagedRows.Contains(row) && IsCaptureSide && CaptureIdle && !ReadOnly);
        CloseStagedEditCommand = Cmd(p => ((StagedRow)p!).IsEditing = false,
            p => p is StagedRow row && StagedRows.Contains(row) && IsCaptureSide);
        RemoveStagedCommand = Cmd(p => { StagedRows.Remove((StagedRow)p!); Raise(nameof(CaptureDirty)); RaiseCommands(); }, p => p is StagedRow && IsCaptureSide);
        // Settled capture rule: a staged item offers the FULL item action set (מחיקה, העתק כתובת, עריכת כתובת, פתח באמצעות,
        // הזז למעלה/למטה; שיתוף לסביבה אחרת stays visible-disabled while that action is undefined) with staged semantics:
        // edits and a chosen default stay staged until שמור, and the source capture list is never changed.
        StagedCopyAddressCommand = Cmd(p => { CopyAddressRequested?.Invoke(((StagedRow)p!).Target); Status = "Address copied"; }, p => p is StagedRow && IsCaptureSide);
        // Enabled at the group edge exactly like the details screen's MoveItemUp/DownCommand: the four-group boundary is
        // enforced inside MoveStaged as a no-op, not by disabling the entry.
        StagedMoveUpCommand = Cmd(p => MoveStaged((StagedRow)p!, -1), p => p is StagedRow && IsCaptureSide && !ReadOnly);
        StagedMoveDownCommand = Cmd(p => MoveStaged((StagedRow)p!, 1), p => p is StagedRow && IsCaptureSide && !ReadOnly);
        StagedEditAddressCommand = Cmd(p => BeginAddressEditStaged((StagedRow)p!), p => p is StagedRow && IsCaptureSide && !ReadOnly && !ItemEditDirty && !AddressEditDirty);
        StagedOpenWithCommand = Cmd(p => BeginOpenWithStaged((StagedRow)p!), p => p is StagedRow { OpenWithEnabled: true } && IsCaptureSide && CaptureIdle && !ReadOnly);
        ConfirmCaptureDupSingleCommand = Cmd(_ => { var r = _captureDuplicates.SingleTarget!; _captureDuplicates.SingleTarget = null; Raise(nameof(CaptureDupSinglePending)); Stage(r); }, _ => _captureDuplicates.SingleTarget != null);
        CancelCaptureDupSingleCommand = Cmd(_ => { _captureDuplicates.SingleTarget = null; Raise(nameof(CaptureDupSinglePending)); RaiseCommands(); }, _ => _captureDuplicates.SingleTarget != null);
        ConfirmCaptureDupGroupCommand = Cmd(_ => { foreach (var c in CaptureDupChoices.Where(c => c.IsChecked).ToList()) Stage(c.Row); CaptureDupChoices.Clear(); CaptureDupGroupPending = false; }, _ => _captureDuplicates.GroupPending);
        CancelCaptureDupGroupCommand = Cmd(_ => { CaptureDupChoices.Clear(); CaptureDupGroupPending = false; Status = "הכפילויות לא נוספו"; }, _ => _captureDuplicates.GroupPending);
    }
    private void OnCaptureCheckChanged() { Raise(nameof(CaptureAddLabel)); Raise(nameof(CaptureSelectionText)); }
    private void StartCapture(CaptureStage stage, bool checkedByDefault, string? wsId = null)
    {
        CaptureRows.Clear(); CaptureNotes.Clear();
        StagedRows.Clear(); CaptureDupChoices.Clear(); _captureDuplicates.SingleTarget = null; CaptureDupGroupPending = false; CaptureSearch = "";
        Raise(nameof(CaptureAddLabel)); Raise(nameof(CaptureSelectionText)); Raise(nameof(CaptureEmpty)); Raise(nameof(CaptureDupSinglePending));
        // The screen opens right away; the scan itself runs off the UI thread, so a stuck COM/UIA call can never freeze the window.
        if (wsId != null) EnterSideBySide(wsId);
        else { RefreshCaptureResults(); CaptureStageNow = stage; }
        CurrentScan = RunScanAsync(checkedByDefault);
    }
    /// <summary>Background scan: bounded by <see cref="ScanTimeout"/>, cancelled by X or superseded by a newer scan (late results are
    /// discarded, never applied), with a visible scanning state. Unexpected faults are not swallowed: they reach <see cref="UnexpectedError"/>.
    /// Unlike the open flow there is no Guard generation here; <see cref="CaptureScanLifetime"/> plays that role for scans.</summary>
    private async Task RunScanAsync(bool checkedByDefault)
    {
        var (gen, cts) = _captureScan.Begin();
        IsScanning = true; Status = "Scanning open items...";
        CaptureScan scan;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        if (_captureScan.HasOutstandingScan)
        {
            scan = new CaptureScan(Array.Empty<CapturedItem>(), new[] { "Earlier scans never came back (a stuck call cannot be aborted), so no new scan was started; items can still be added by hand" });
        }
        else
        {
            _captureScan.WorkerStarted();
            try { scan = await CaptureScanning.BoundedAsync(_capturer, ScanTimeout, cts.Token, _captureScan.WorkerReturned); }
            catch (OperationCanceledException) { return; } // X or a newer scan: nothing to apply
            catch (Exception e) when (e is IOException or InvalidOperationException or UnauthorizedAccessException or NotSupportedException)
            { scan = new CaptureScan(Array.Empty<CapturedItem>(), new[] { "Scan failed: " + e.GetType().Name }); }
            catch (Exception e)
            {
                if (_captureScan.IsCurrent(gen)) { IsScanning = false; Status = "Scan stopped by an unexpected error: " + e.GetType().Name; }
                var h = UnexpectedError; if (h is null) throw; h(e); return;
            }
        }
        if (!_captureScan.IsCurrent(gen) || cts.IsCancellationRequested) return;
        var floor = ScanMinVisible - sw.Elapsed;
        if (floor > TimeSpan.Zero)
        {
            try { await Task.Delay(floor, cts.Token); } catch (OperationCanceledException) { return; }
            if (!_captureScan.IsCurrent(gen)) return;
        }
        var surviving = scan.Items.Where(i => !GlobalEnvRules.IsBlacklisted(_current.CaptureBlacklist, i)).ToList();
        foreach (var i in GlobalEnvRules.WhitelistPins(_current.CaptureWhitelist, surviving).Concat(surviving)) CaptureRows.Add(new CaptureRow(i, checkedByDefault, OnCaptureCheckChanged));
        foreach (var n in scan.Notes)
        {
            // This adapter emits one fixed capabilities paragraph. It belongs in help, not a routine warning list.
            if (n.StartsWith("Sites: scans open browser tabs", StringComparison.Ordinal) || n == "Files: documents open inside programs are not detected by the current version") continue;
            CaptureNotes.Add(n);
        }
        if (CaptureNotes.Count > 0) ShowToast("הלכידה הושלמה עם מגבלה או תקלה. ייתכן שחסרים פריטים; אפשר להוסיף ידנית או לנסות שוב.", true, string.Join(Environment.NewLine, CaptureNotes));
        IsScanning = false;
        Raise(nameof(CaptureAddLabel)); Raise(nameof(CaptureSelectionText)); RaiseCommands();
        Status = scan.Items.Count == 0 ? "Nothing open was detected" : $"{scan.Items.Count} open {(scan.Items.Count == 1 ? "item" : "items")} detected";
    }
    private void RefreshCaptureResults()
    {
        CaptureWorkspaceResults.Clear();
        foreach (var w in Workspaces.Where(w => _captureSession.Search.Length == 0 || w.Name.Contains(_captureSession.Search, StringComparison.OrdinalIgnoreCase) || w.EffectiveCategory.Contains(_captureSession.Search, StringComparison.OrdinalIgnoreCase)))
            CaptureWorkspaceResults.Add(w);
    }
    private void EnterSideBySide(string wsId)
    {
        _captureSession.WorkspaceId = wsId; StagedRows.Clear(); RefreshCaptureWorkspaceItems(); Raise(nameof(CaptureWorkspaceName)); CaptureStageNow = CaptureStage.SideBySide;
    }
    private void RefreshCaptureWorkspaceItems()
    {
        CaptureWorkspaceItems.Clear();
        var w = _current.Workspaces.FirstOrDefault(x => x.Id == _captureSession.WorkspaceId); if (w is null) return;
        foreach (var m in w.Items.Select((m, i) => (m, i)).OrderBy(x => _current.Items.TryGetValue(x.m.ItemId, out var g) ? GroupRank(g.Type) : 4).ThenBy(x => x.i))
            if (_current.Items.TryGetValue(m.m.ItemId, out var it)) CaptureWorkspaceItems.Add(new CaptureExistingRow(m.m.Name ?? it.Name, it.Target, GroupOf(it.Type), it.Type.ToString()));
    }
    private bool IsCaptureDuplicate(CaptureRow r) =>
        new Catalog(_current).HasTarget(_captureSession.WorkspaceId, r.Type, r.Target) || StagedRows.Any(s => s.Type == r.Type && s.Target == r.Target);
    private void Stage(CaptureRow r) { StagedRows.Add(new StagedRow(r.Item)); Raise(nameof(CaptureDirty)); RaiseCommands(); }
    /// <summary>One item dragged (or dropped by the view) onto the workspace side: a duplicate gets its own warning for that one drag.</summary>
    public void DropCaptured(CaptureRow r)
    {
        if (!IsCaptureSide || !CaptureIdle) return;
        if (IsCaptureDuplicate(r))
        {
            _captureDuplicates.SingleTarget = r; CaptureDupSingleMessage = $"הפריט '{r.Name}' כבר קיים בסביבה או ברשימת הפריטים שנוספו. להוסיף אותו שוב כעותק נפרד?";
            Raise(nameof(CaptureDupSinglePending)); RaiseCommands(); return;
        }
        Stage(r);
    }
    /// <summary>הוסף הכל / הוסף נבחרים: new addresses are staged at once; every duplicate goes into ONE grouped dialog where the user picks which to add.</summary>
    private void AddCaptured()
    {
        var ticked = CaptureRows.Where(r => r.IsChecked).ToList();
        var rows = ticked.Count > 0 ? ticked : CaptureRows.ToList();
        var dups = rows.Where(IsCaptureDuplicate).ToList();
        foreach (var r in rows.Except(dups)) Stage(r);
        if (dups.Count > 0) { CaptureDupChoices.Clear(); foreach (var d in dups) CaptureDupChoices.Add(new DupChoice(d)); CaptureDupGroupPending = true; }
        Status = $"נוספו {rows.Count - dups.Count} פריטים למסך, השינויים עדיין לא נשמרו" + (dups.Count > 0 ? $"; {dups.Count} פריטים כבר קיימים בסביבה וממתינים לבחירתך" : "");
    }
    /// <summary>Staged reorder mirrors the details screen: הזז swaps with the next staged row IN THE SAME display
    /// group; at the group's edge it does nothing (same boundary rule as Catalog.MoveInstance).</summary>
    private void MoveStaged(StagedRow row, int delta)
    {
        if (_stagedItems.MoveWithinGroup(row, delta)) RaiseCommands();
    }
    /// <summary>פתח באמצעות on a staged row: the same popup a saved item gets, but a chosen default lands on the staged
    /// row's open-with field (written to the store only on שמור) and Go opens the item straight from the staged values.</summary>
    private void BeginOpenWithStaged(StagedRow row)
    {
        _openWith.SavedTarget = null; _openWith.StagedTarget = row;
        OpenWithProgram = row.OpenWith;
        OpenWithSetDefault = row.OpenWith.Trim().Length > 0;
        OpenWithOpen = true; Raise(nameof(OpenWithLabel));
    }
    private void CloseOpenWith() { OpenWithOpen = false; _openWith.ClearTargets(); }
    private bool SaveStaged()
    {
        var wsId = _captureSession.WorkspaceId; var staged = StagedRows.ToList();
        foreach (var s in staged)
        {
            var ow = s.OpenWith.Trim(); LaunchConfig? launch = ow.Length == 0 ? null : s.Type == ItemType.Link ? new LaunchConfig(Browser: ow) : new LaunchConfig(OpenWith: ow);
            var err = Catalog.ValidateInputTarget(s.Type, s.Target) ?? Catalog.ValidateDescription(s.Description) ?? Catalog.ValidateLaunch(s.Type, launch);
            if (err != null) { s.IsEditing = true; Status = $"Not saved ({s.OriginalName}): " + err; return false; }
        }
        var ok = Apply(c =>
        {
            foreach (var s in staged)
            {
                var ow = s.OpenWith.Trim(); LaunchConfig? launch = ow.Length == 0 ? null : s.Type == ItemType.Link ? new LaunchConfig(Browser: ow) : new LaunchConfig(OpenWith: ow);
                var it = c.GetOrAddItem(s.Type, s.OriginalName, s.Target);
                var nm = string.IsNullOrWhiteSpace(s.Name) || s.Name == it.Name ? null : s.Name;
                c.AddToWorkspace(wsId, it.Id, launch, nm, s.Description);
            }
        });
        if (!ok) return false;
        StagedRows.Clear(); RefreshCaptureWorkspaceItems(); Raise(nameof(CaptureDirty)); RaiseCommands();
        Status = $"Saved {staged.Count} {(staged.Count == 1 ? "item" : "items")} to the workspace; the capture list stays open";
        return true;
    }
    /// <summary>A captured site keeps the browser it was found in as its default browser on the new item (spec Nov 11:
    /// דפדפן שזוהה בזמן הלכידה יישמר; profiles deferred). Anything without a retained browser opens with the Windows default.</summary>
    private static LaunchConfig? RetainedBrowserLaunch(CapturedItem i) =>
        i.Type == ItemType.Link && !string.IsNullOrEmpty(i.Browser) ? new LaunchConfig(Browser: i.Browser) : null;
    private bool CreateFromCapture()
    {
        var n = NewName; var d = NewWorkspaceDescription; var cat = NewWorkspaceCategory;
        if (string.IsNullOrWhiteSpace(n) || Catalog.ValidateDescription(d) is not null) { FieldError("create", "הזינו שם לסביבה ותיאור באורך עד 500 תווים.", "Not created: invalid workspace fields"); return false; } string? id = null;
        var chosen = CaptureRows.Where(r => r.IsChecked).Select(r => r.Item).ToList();
        if (string.IsNullOrWhiteSpace(n)) { Status = "Not created: workspace name is required"; return false; }
        var color = NewWorkspaceColor.Trim(); var image = NewWorkspaceImagePath.Trim();
        if (color.Length == 0) color = WorkspaceColors.Default;
        if (ValidateCreateDesign(color, image) is { } de) { FieldError("create", "בדקו את הצבע ואת נתיב התמונה. צבע יש להזין בצורה #RRGGBB ותמונה כנתיב מלא.", "Not created: " + de); return false; }
        foreach (var i in chosen)
        {
            var err = Catalog.ValidateInputTarget(i.Type, i.Target) ?? Catalog.ValidateLaunch(i.Type, RetainedBrowserLaunch(i));
            if (err != null) { Status = $"Not created ({i.Name}): " + err; return false; }
        }
        var ok = Apply(c =>
        {
            id = c.CreateWorkspace(n, d).Id; c.SetCategory(id, cat);
            c.SetCardDesign(id, color.Length > 0 ? color : null, image.Length > 0 ? image : null);
            RememberAdvancedColor(c, _workspaceCreate.AdvancedColorToRemember, color);
            foreach (var i in chosen) { var it = c.GetOrAddItem(i.Type, i.Name, i.Target); c.AddToWorkspace(id, it.Id, RetainedBrowserLaunch(i)); }
        });
        if (!ok) return false;
        ResetCreateForm();
        ResetCapture();
        Selected = Workspaces.FirstOrDefault(w => w.Id == id);
        LoadDetails(); InDetails = true;
        Status = $"Workspace created with {chosen.Count} captured {(chosen.Count == 1 ? "item" : "items")}";
        return true;
    }
    private bool CaptureGuardSave()
    {
        if (IsCaptureNew)
        {
            if (string.IsNullOrWhiteSpace(NewName)) { UnsavedOpen = false; _unsavedExit.ClearContinuation(); FieldError("create", "חסר שם לסביבה. הזינו שם כדי לשמור.", "Not saved: workspace name is required"); return false; }
            return CreateFromCapture();
        }
        return SaveStaged();
    }
    private void DiscardCaptureChanges()
    {
        StagedRows.Clear();
        if (IsCaptureNew) { ResetCreateForm(); }
        Raise(nameof(CaptureDirty));
    }
    private void CaptureBack()
    {
        if (StagedRows.Count > 0) { MarkUnsavedHost(false, false, false, false, true); _unsavedExit.Continuation = BackToPick; UnsavedOpen = true; return; }
        BackToPick();
    }
    private void BackToPick() { StagedRows.Clear(); _captureSession.WorkspaceId = ""; CaptureSearch = ""; RefreshCaptureResults(); CaptureStageNow = CaptureStage.PickWorkspace; }
    private void CaptureClose()
    {
        if (CaptureDirty) { MarkUnsavedHost(false, false, false, false, true); _unsavedExit.Continuation = ResetCapture; UnsavedOpen = true; return; }
        ResetCapture();
    }
    /// <summary>X: the capture list is dropped; opening capture again scans what is open at that moment. What was already saved stays saved.</summary>
    private void ResetCapture()
    {
        _captureScan.CancelAndInvalidate(); // a scan still in flight is discarded, never applied
        IsScanning = false;
        CaptureRows.Clear(); CaptureNotes.Clear(); StagedRows.Clear(); CaptureDupChoices.Clear(); CaptureWorkspaceItems.Clear(); _captureDuplicates.SingleTarget = null; _captureSession.WorkspaceId = "";
        CaptureDupGroupPending = false; CaptureSearch = "";
        if (IsCaptureNew) ResetCreateForm(); // X-discard from capture-new: the shared create form fields leave with the capture screen
        Raise(nameof(CaptureDupSinglePending)); Raise(nameof(CaptureEmpty)); Raise(nameof(CaptureAddLabel));
        CaptureStageNow = CaptureStage.None;
    }
}
