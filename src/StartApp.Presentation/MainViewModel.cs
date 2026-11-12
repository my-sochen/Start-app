using System.Runtime.ExceptionServices;
using System.Collections.ObjectModel;
using System.Windows.Input;
using StartApp.Core;

namespace StartApp.Presentation;

/// <summary>One visible instance row. Name/Description are the instance's own values with fallback to the catalog item's original name /
/// no description. Group is the display category (decided: sites, folders, files+programs); order inside a group is undecided, so insertion order is kept.</summary>
public sealed record ItemRow(string InstanceId, string ItemId, string Name, string Target, string Type, string Group, string? Description = null);

/// <summary>UI state only. Persistence via IStoreRepository, OS actions via IItemLauncher. A store that failed to load puts the app in a
/// read-only state with a message; nothing is written until the user decides (no silent reset).</summary>
public sealed class MainViewModel : ObservableBase
{
    private readonly IStoreRepository _repo;
    private readonly IItemLauncher _launcher;
    private readonly IItemPreflight? _preflight;
    private readonly IOpenStateDetector _detector;
    private List<(Item Item, LaunchConfig? Launch, PreflightFinding Finding, OpenState Dup)>? _pendingOpen;
    private Workspace? _selected; private string _status = ""; private bool _readOnly; private string _newName = "";

    public ObservableCollection<Workspace> Workspaces { get; } = new();
    public ObservableCollection<ItemRow> Items { get; } = new();
    private readonly List<RelayCommand> _commands = new();
    private RelayCommand Cmd(Action<object?> run, Func<object?, bool> can) { var c = new RelayCommand(run, can); _commands.Add(c); return c; }
    private void RaiseCommands() { foreach (var c in _commands) c.Raise(); Raise(nameof(OpenDecisionPending)); Raise(nameof(ShowOpenPanel)); Raise(nameof(ConfirmLabel)); Raise(nameof(DuplicatePending)); }
    public ICommand CreateWorkspaceCommand { get; }
    /// <summary>Opens the details screen for a workspace (from its card in the overview).</summary>
    public ICommand OpenDetailsCommand { get; }
    /// <summary>שגר from a card in the overview: opens that workspace's items without navigating.</summary>
    public ICommand OpenWorkspaceForCommand { get; }
    /// <summary>הוסף אייטם from the card's actions menu: opens the details screen and focuses the add form.</summary>
    public ICommand AddItemToCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand SaveDetailsCommand { get; }
    /// <summary>Unsaved-changes exit dialog: שמור וצא.</summary>
    public ICommand SaveAndExitCommand { get; }
    /// <summary>Unsaved-changes exit dialog: צא בלי לשמור.</summary>
    public ICommand ExitWithoutSavingCommand { get; }
    /// <summary>Unsaved-changes exit dialog: X, back to editing with nothing lost.</summary>
    public ICommand CloseUnsavedCommand { get; }
    /// <summary>The הוסף אייטם button in the empty state; the view focuses the add form.</summary>
    public ICommand FocusAddCommand { get; }
    public event Action? FocusAddRequested;
    /// <summary>The empty-workspace dialog's button that leads to the details screen (to add items).</summary>
    public ICommand GoToDetailsCommand { get; }
    public ICommand RemoveItemCommand { get; }
    public ICommand OpenWorkspaceCommand { get; }
    public ICommand AddItemCommand { get; }
    /// <summary>Duplicate-add confirmation: visible while an add waits for the user to approve a second instance of the same target.</summary>
    public bool DuplicatePending => _pendingDup;
    private bool _pendingDup;
    /// <summary>Text shown next to the duplicate confirm/cancel buttons.</summary>
    public string DuplicateMessage { get => _dupMessage; private set => Set(ref _dupMessage, value); }
    private string _dupMessage = "";
    public ICommand ConfirmDuplicateCommand { get; }
    public ICommand CancelDuplicateCommand { get; }
    /// <summary>Set instead of opening when the workspace has no items (the spec's דיאלוג סביבת עבודה ריקה).</summary>
    public bool EmptyWorkspaceOpen { get => _emptyOpen; private set => Set(ref _emptyOpen, value); }
    private bool _emptyOpen;
    public ICommand CloseEmptyWorkspaceCommand { get; }
    /// <summary>The shared unsaved-changes exit dialog (דיאלוג יציאה עם שינויים שלא נשמרו).</summary>
    public bool UnsavedOpen { get => _unsavedOpen; private set => Set(ref _unsavedOpen, value); }
    private bool _unsavedOpen;
    private Action _exitAction = () => { };
    /// <summary>Window-close guard: false means the close was cancelled and the unsaved-changes dialog opened instead.</summary>
    public bool RequestAppClose()
    {
        if (DetailsDirty) { UnsavedForCreate = false; _exitAction = () => AppCloseApproved?.Invoke(); UnsavedOpen = true; return false; }
        if (CreateDirty) { UnsavedForCreate = true; _exitAction = () => AppCloseApproved?.Invoke(); UnsavedOpen = true; return false; }
        return true;
    }
    private void SaveAndExit()
    {
        if (UnsavedForCreate)
        {
            if (string.IsNullOrWhiteSpace(NewName)) { UnsavedOpen = false; _exitAction = () => { }; Status = "Not saved: workspace name is required"; return; }
            Create();
            if (CreateDirty) { UnsavedOpen = false; _exitAction = () => { }; return; } // creation failed; keep editing
        }
        else if (!SaveDetails()) return;
        UnsavedOpen = false; var a = _exitAction; _exitAction = () => { }; a();
    }
    /// <summary>Fired when the user chose שמור וצא or צא בלי לשמור for a window close.</summary>
    public event Action? AppCloseApproved;
    public ICommand ConfirmOpenAnywayCommand { get; }
    public ICommand CancelOpenCommand { get; }
    /// <summary>Per-item problems found before opening; the user decides. Empty when nothing is pending.</summary>
    public ObservableCollection<string> OpenProblems { get; } = new();
    /// <summary>Per-item outcome of the last open (one line per item).</summary>
    public ObservableCollection<string> OpenReport { get; } = new();
    public bool OpenDecisionPending => _pendingOpen != null;
    /// <summary>Label of the confirm button. When every item is skipped by policy, confirming only shows the report, so it says so.</summary>
    public string ConfirmLabel => _pendingOpen != null && _pendingOpen.All(p => NeverLaunched(p.Finding.State)) ? "Show report" : "Open anyway";
    /// <summary>The open panel (with Cancel) is visible while a decision is pending AND while checking or launching, so the user can always cancel.</summary>
    public bool ShowOpenPanel => _pendingOpen != null || _isChecking || _isLaunching;
    public IReadOnlyList<ItemType> ItemTypes { get; } = Enum.GetValues<ItemType>();

    public MainViewModel(IStoreRepository repo, IItemLauncher launcher, IItemPreflight? preflight = null, IOpenStateDetector? detector = null)
    {
        _repo = repo; _launcher = launcher; _preflight = preflight; _detector = detector ?? new NoOpenStateDetection();
        CreateWorkspaceCommand = Cmd(_ => Create(), _ => !ReadOnly && NewName.Length > 0);
        // No workspace deletion: the user explicitly rejected it from the card actions (Nov 11); the menu is
        // הצג / שגר / הוסף אייטם / עצב כרטיס / ערוך קטגוריה only. Catalog.DeleteWorkspace stays unexposed.
        OpenDetailsCommand = Cmd(p => OpenDetails((Workspace)p!), p => p is Workspace);
        OpenWorkspaceForCommand = Cmd(p => { Selected = (Workspace)p!; Open(); }, p => p is Workspace && !IsChecking && !IsLaunching);
        AddItemToCommand = Cmd(p => { OpenDetails((Workspace)p!); FocusAddRequested?.Invoke(); }, p => p is Workspace);
        BackCommand = Cmd(_ => Back(), _ => InDetails);
        SaveDetailsCommand = Cmd(_ => SaveDetails(), _ => InDetails && DetailsDirty && !ReadOnly);
        SaveAndExitCommand = Cmd(_ => SaveAndExit(), _ => UnsavedOpen);
        ExitWithoutSavingCommand = Cmd(_ => { UnsavedOpen = false; if (UnsavedForCreate) { NewName = ""; NewWorkspaceDescription = ""; } else { _detailsDirtyBypass = true; LoadDetails(); _detailsDirtyBypass = false; } var a = _exitAction; _exitAction = () => { }; a(); }, _ => UnsavedOpen);
        CloseUnsavedCommand = Cmd(_ => { UnsavedOpen = false; _exitAction = () => { }; }, _ => UnsavedOpen);
        FocusAddCommand = Cmd(_ => FocusAddRequested?.Invoke(), _ => InDetails);
        RemoveItemCommand = Cmd(p => Apply(c => c.RemoveInstance(Selected!.Id, ((ItemRow)p!).InstanceId)), p => !ReadOnly && Selected != null && p is ItemRow);
        AddItemCommand = Cmd(_ => AddItem(), _ => !ReadOnly && Selected != null && NewTarget.Length > 0);
        ConfirmDuplicateCommand = Cmd(_ => { _pendingDup = false; DuplicateMessage = ""; AddItem(force: true); }, _ => _pendingDup);
        CancelDuplicateCommand = Cmd(_ => ClearDup("Add cancelled, nothing was added"), _ => _pendingDup);
        CloseEmptyWorkspaceCommand = Cmd(_ => EmptyWorkspaceOpen = false, _ => _emptyOpen);
        GoToDetailsCommand = Cmd(_ => { EmptyWorkspaceOpen = false; if (Selected != null) { LoadDetails(); InDetails = true; } }, _ => _emptyOpen && Selected != null);
        OpenWorkspaceCommand = Cmd(_ => Open(), _ => Selected != null && !IsChecking && !IsLaunching);
        ConfirmOpenAnywayCommand = Cmd(_ => ConfirmOpen(), _ => _pendingOpen != null);
        CancelOpenCommand = Cmd(_ => { var launching = IsLaunching; ClearPending(); if (!launching) Status = "Open cancelled, nothing was opened"; }, _ => _pendingOpen != null || IsChecking || IsLaunching);
        Reload();
    }

    private void ClearPending() { if (IsChecking) { _checkCts?.Cancel(); _generation++; IsChecking = false; } if (IsLaunching) _launchCts?.Cancel(); _pendingOpen = null; OpenProblems.Clear(); Raise(nameof(OpenDecisionPending)); RaiseCommands(); }

    public Workspace? Selected { get => _selected; set { ClearPending(); ClearDup(); EmptyWorkspaceOpen = false; Set(ref _selected, value); if (InDetails && !DetailsDirty) LoadDetails(); RefreshItems(); Raise(nameof(ShowEmptyItemsMessage)); Raise(nameof(CreatedAtText)); RaiseCommands(); } }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public bool ReadOnly { get => _readOnly; private set { Set(ref _readOnly, value); RaiseCommands(); } }
    public ItemType NewItemType { get => _newType; set { Set(ref _newType, value); ClearDup(); } }
    public string NewTarget { get => _newTarget; set { Set(ref _newTarget, value); ClearDup(); RaiseCommands(); } }
    private ItemType _newType = ItemType.File; private string _newTarget = "";
    /// <summary>Optional local name for the instance in this workspace. Empty means the original (derived) name.</summary>
    public string NewItemName { get => _newItemName; set { Set(ref _newItemName, value ?? ""); ClearDup(); } }
    private string _newItemName = "";
    /// <summary>Optional description typed in the add form. Empty means none.</summary>
    public string NewDescription { get => _newDescription; set { Set(ref _newDescription, value ?? ""); ClearDup(); } }
    private string _newDescription = "";

    private void ClearDup(string? status = null) { if (!_pendingDup) return; _pendingDup = false; DuplicateMessage = ""; if (status != null) Status = status; Raise(nameof(DuplicatePending)); RaiseCommands(); }
    public string NewName { get => _newName; set { Set(ref _newName, value); Raise(nameof(CreateDirty)); RaiseCommands(); } }
    /// <summary>Optional workspace description in the create form.</summary>
    public string NewWorkspaceDescription { get => _newWsDesc; set { Set(ref _newWsDesc, value ?? ""); Raise(nameof(CreateDirty)); } }
    private string _newWsDesc = "";
    /// <summary>The create form holds unsaved input; closing the app then goes through the same unsaved-exit
    /// dialog (spec: דיאלוג יציאה עם שינויים שלא נשמרו applies to טופס יצירת סביבה too).</summary>
    public bool CreateDirty => NewName.Length > 0 || NewWorkspaceDescription.Length > 0;
    /// <summary>True while the open unsaved-exit dialog guards the create form rather than the details screen.</summary>
    public bool UnsavedForCreate { get => _unsavedForCreate; private set => Set(ref _unsavedForCreate, value); }
    private bool _unsavedForCreate;

    // ---- navigation and the details screen (פרטי סביבה) ----
    /// <summary>False = overview (workspace cards + create form); true = the details screen of <see cref="Selected"/>.</summary>
    public bool InDetails { get => _inDetails; private set { Set(ref _inDetails, value); Raise(nameof(InOverview)); Raise(nameof(ShowEmptyItemsMessage)); RaiseCommands(); } }
    private bool _inDetails;
    public bool InOverview => !_inDetails;
    /// <summary>Spec strings quoted in Hebrew on the cards, kept verbatim.</summary>
    public const string EmptyWorkspaceTitle = "סביבת עבודה ריקה";
    public const string EmptyItemsMessage = "עדיין אין אייטמים בסביבה";
    /// <summary>The empty state of the details screen: message plus an הוסף אייטם button.</summary>
    public bool ShowEmptyItemsMessage => InDetails && Selected != null && Selected.Items.Count == 0;

    /// <summary>Editable workspace name on the details screen (direct editing, no edit button).</summary>
    public string EditName { get => _editName; set { Set(ref _editName, value ?? ""); RaiseDirty(); } }
    private string _editName = "";
    /// <summary>Editable category; the built-in default is כללי and custom category management is a separate feature.</summary>
    public string EditCategory { get => _editCategory; set { Set(ref _editCategory, value ?? ""); RaiseDirty(); } }
    private string _editCategory = "";
    /// <summary>Editable workspace description.</summary>
    public string EditDescription { get => _editDescription; set { Set(ref _editDescription, value ?? ""); RaiseDirty(); } }
    private string _editDescription = "";
    private bool _detailsDirtyBypass;
    /// <summary>Any change on the details screen shows the Save button (spec: כל שינוי מציג כפתור שמירה).</summary>
    public bool DetailsDirty => !_detailsDirtyBypass && Selected != null && (EditName != Selected.Name || EditCategory != Selected.EffectiveCategory || EditDescription != (Selected.Description ?? ""));
    public string CreatedAtText => Selected?.CreatedAt is { } t ? t.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "unknown";
    /// <summary>Card display: last time the workspace was really opened, or never.</summary>
    public static string LastOpenedTextOf(Workspace w) => w.LastOpenedAt is { } t ? t.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "never";
    private void RaiseDirty() { Raise(nameof(DetailsDirty)); RaiseCommands(); }
    private void LoadDetails()
    {
        if (Selected is null) return;
        _editName = Selected.Name; _editCategory = Selected.EffectiveCategory; _editDescription = Selected.Description ?? "";
        Raise(nameof(EditName)); Raise(nameof(EditCategory)); Raise(nameof(EditDescription)); RaiseDirty(); Raise(nameof(CreatedAtText));
    }
    private void OpenDetails(Workspace w) { ClearPending(); ClearDup(); EmptyWorkspaceOpen = false; Selected = w; LoadDetails(); InDetails = true; }
    private void Back()
    {
        ClearPending(); ClearDup(); EmptyWorkspaceOpen = false;
        if (DetailsDirty) { UnsavedForCreate = false; _exitAction = () => InDetails = false; UnsavedOpen = true; return; }
        InDetails = false;
    }
    private bool SaveDetails()
    {
        var id = Selected!.Id; var n = EditName; var d = EditDescription; var cat = EditCategory;
        if (string.IsNullOrWhiteSpace(n)) { Status = "Not saved: workspace name is required"; return false; }
        var ok = Apply(c => c.UpdateWorkspace(id, n, d, cat == Workspace.DefaultCategory ? null : cat)); // the default stays the default, not an explicit value
        if (ok) { Status = "Workspace details saved"; RaiseDirty(); Raise(nameof(CreatedAtText)); }
        return ok;
    }
    public void Reload()
    {
        try { Show(_repo.Load()); ReadOnly = false; Status = ""; }
        catch (StoreCorruptException e) { ReadOnly = true; Status = "Data file is invalid and was left untouched: " + e.Message; Show(new Store()); }
        catch (StoreTooNewException e) { ReadOnly = true; Status = e.Message; Show(new Store()); }
        // Environmental failures only; programming errors (e.g. NullReference) must stay loud.
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or StoreLockedException)
        { ReadOnly = true; Status = "Data file could not be read and was left untouched: " + e.Message; Show(new Store()); }
    }

    private Store _current = new();
    private void Show(Store s)
    {
        _current = s; var keep = Selected?.Id;
        Workspaces.Clear(); foreach (var w in s.Workspaces) Workspaces.Add(w);
        Selected = Workspaces.FirstOrDefault(w => w.Id == keep) ?? Workspaces.FirstOrDefault();
    }

    private static int GroupRank(ItemType t) => t switch { ItemType.Link => 0, ItemType.Folder => 1, _ => 2 };
    public static string GroupOf(ItemType t) => t switch { ItemType.Link => "Sites", ItemType.Folder => "Folders", _ => "Files & Programs" };

    private void RefreshItems()
    {
        Items.Clear(); Raise(nameof(ShowEmptyItemsMessage)); if (Selected is null) return;
        foreach (var m in Selected.Items.Select((m, i) => (m, i)).OrderBy(x => _current.Items.TryGetValue(x.m.ItemId, out var g) ? GroupRank(g.Type) : 3).ThenBy(x => x.i))
            if (_current.Items.TryGetValue(m.m.ItemId, out var it))
                Items.Add(new ItemRow(m.m.Id ?? m.m.ItemId, it.Id, m.m.Name ?? it.Name, it.Target, it.Type.ToString(), GroupOf(it.Type), m.m.Description ?? it.Description));
    }

    /// <summary>Manual add. The target and name are used exactly as typed (never trimmed); invalid input is reported, not corrected.
    /// The description is stored on the instance, so it never changes another workspace or another instance. Adding a target that already
    /// exists in this workspace first asks for confirmation; confirming adds a second, independent instance.</summary>
    private void AddItem(bool force = false)
    {
        var type = NewItemType; var target = NewTarget; var wsId = Selected!.Id; var desc = NewDescription; var name = NewItemName;
        var err = Catalog.ValidateTarget(type, target) ?? Catalog.ValidateDescription(desc) ?? Catalog.ValidateInstanceName(string.IsNullOrWhiteSpace(name) ? null : name);
        if (err != null) { Status = "Not added: " + err; return; }
        if (!force && new Catalog(_current).HasTarget(wsId, type, target))
        {
            _pendingDup = true; Raise(nameof(DuplicatePending)); RaiseCommands();
            DuplicateMessage = "This target is already in the workspace. Add it again as a separate instance with its own name and settings?";
            return;
        }
        _pendingDup = false; DuplicateMessage = ""; Raise(nameof(DuplicatePending));
        if (Apply(c => { var it = c.GetOrAddItem(type, DisplayName(type, target), target); c.AddToWorkspace(wsId, it.Id, null, string.IsNullOrWhiteSpace(name) ? null : name, desc); }))
        {
            NewTarget = ""; NewDescription = ""; NewItemName = "";
        }
    }

    private static string DisplayName(ItemType type, string target)
    {
        if (type == ItemType.Link && Uri.TryCreate(target, UriKind.Absolute, out var u) && u.Host.Length > 0) return u.Host;
        var i = target.LastIndexOfAny(new[] { '\\', '/' });
        return i >= 0 && i < target.Length - 1 ? target[(i + 1)..] : target;
    }

    /// <summary>Creates the workspace from the create form (name required; כללי is the default category; description optional)
    /// and then opens its details screen, as the spec requires (אחרי היצירה נפתחים פרטי סביבה).</summary>
    private void Create()
    {
        var n = NewName; var d = NewWorkspaceDescription; string? id = null;
        if (Apply(c => id = c.CreateWorkspace(n, d).Id))
        {
            NewName = ""; NewWorkspaceDescription = "";
            Selected = Workspaces.FirstOrDefault(w => w.Id == id);
            LoadDetails(); InDetails = true;
        }
    }

    private bool Apply(Action<Catalog> change)
    {
        try { Show(_repo.Mutate(change)); Status = ""; return true; }
        catch (StoreLockedException e) { Status = e.Message; }
        catch (StoreCorruptException e) { ReadOnly = true; Status = "Data file is invalid and was left untouched: " + e.Message; }
        catch (StoreTooNewException e) { ReadOnly = true; Status = e.Message; }
        catch (Exception e) when (e is IOException or InvalidOperationException or UnauthorizedAccessException) { Status = "Save failed, nothing was changed: " + e.Message; }
        return false;
    }

    /// <summary>Every item is checked first (environment and already-open state). Anything that is not clean is listed with its reason and nothing starts until the
    /// user chooses open anyway or cancel. Already-open is Unknown unless a detector has reliable exact-target evidence: Unknown is reported as "cannot tell whether
    /// this is already open, opening may create a duplicate", never as a duplicate and never as safe. After "anyway", Unsupported items (alias, non-http(s) link,
    /// bad config) are never launched; the rest are attempted exactly as saved, with no substitute app. Nothing is closed. "Started" only means the open request was
    /// sent successfully, not that the document loaded.</summary>
    /// <summary>Overall time allowed for the pre-open checks. Items still unchecked after this are reported as "could not be checked", never as fine.</summary>
    public TimeSpan OpenCheckTimeout { get; set; } = TimeSpan.FromSeconds(10);
    private const int CheckWorkers = 4;
    private int _generation; private CancellationTokenSource? _checkCts; private bool _isChecking;
    private CancellationTokenSource? _launchCts; private bool _isLaunching;
    public bool IsLaunching { get => _isLaunching; private set { Set(ref _isLaunching, value); RaiseCommands(); } }
    public bool IsChecking { get => _isChecking; private set { Set(ref _isChecking, value); RaiseCommands(); } }
    /// <summary>The running (or last) check-and-open operation. UI code never waits on it; tests await it.</summary>
    public Task OpenTask { get; private set; } = Task.CompletedTask;

    private string _openWsId = "";
    private void Open()
    {
        ClearPending(); OpenReport.Clear();
        if (Selected!.Items.Count == 0) { EmptyWorkspaceOpen = true; return; }
        _openWsId = Selected!.Id;
        // Snapshot semantics: what is checked and opened is the saved state at the moment Open was pressed; the store file is not watched.
        var snapshot = Selected!.Items.Select(m => (m, Item: _current.Items.TryGetValue(m.ItemId, out var it) ? it : null)).ToList();
        _checkCts?.Cancel(); _checkCts = new CancellationTokenSource(); var gen = ++_generation;
        IsChecking = true; Status = "Checking items before opening...";
        OpenTask = Guard(gen, CheckThenOpenAsync(snapshot, gen, _checkCts.Token));
    }

    private (Item Item, LaunchConfig? Launch, PreflightFinding Finding, OpenState Dup) Evaluate(Membership m, Item? it)
    {
        if (it is null) return (new Item(m.ItemId, ItemType.File, "(missing item " + m.ItemId + ")", ""), m.Launch, new(PreflightState.Unsupported, "the saved item record is missing from the catalog"), OpenState.NotOpen);
        var f = PreflightRules.CheckForm(it, m.Launch) ?? _preflight?.Check(it, m.Launch) ?? new(PreflightState.Ready);
        var dup = f.State == PreflightState.Unsupported ? OpenState.NotOpen : _detector.Detect(it, m.Launch);
        return (it, m.Launch, f, dup);
    }

    /// <summary>Checks run on at most <see cref="CheckWorkers"/> background workers, never on the caller's thread, with one overall deadline. A newer Open, a
    /// selection change or Cancel makes this run stale: its result is dropped. Probes already blocked in the OS cannot be cancelled but are bounded (see FileSystemProbe).</summary>
    private async Task CheckThenOpenAsync(List<(Membership M, Item? Item)> snapshot, int gen, CancellationToken token)
    {
        var stop = CancellationTokenSource.CreateLinkedTokenSource(token); // deliberately not disposed: a late worker may still call stop.Cancel(), and the parent token source is per operation, so nothing outlives the operation // the deadline stops workers from taking NEW items; a call already running finishes by itself
        var results = new (Item Item, LaunchConfig? Launch, PreflightFinding Finding, OpenState Dup)?[snapshot.Count];
        var next = -1; Exception? fault = null; var gate = new object(); var published = false; var reported = 0; // results[] is written by workers and read once at the deadline: both under gate
        Task Worker() => Task.Factory.StartNew(() => // dedicated threads: these calls block, and must not starve the thread pool (or the deadline timer)
        {
            while (!stop.Token.IsCancellationRequested)
            {
                var i = Interlocked.Increment(ref next); if (i >= snapshot.Count) return;
                try { var r = Evaluate(snapshot[i].M, snapshot[i].Item); lock (gate) { if (!published) results[i] = r; } }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
                { var r = (snapshot[i].Item ?? new Item(snapshot[i].M.ItemId, ItemType.File, snapshot[i].M.ItemId, ""), snapshot[i].M.Launch, new PreflightFinding(PreflightState.Unavailable, e.GetType().Name), OpenState.Unknown); lock (gate) { if (!published) results[i] = r; } }
                catch (Exception e) { Interlocked.CompareExchange(ref fault, e, null); stop.Cancel(); bool late; lock (gate) late = published; if (late && Interlocked.Exchange(ref reported, 1) == 0) ReportLate(e); return; } // after the deadline nobody awaits this worker: report now, not when all workers finish // a programming bug is not "unavailable": stop and surface it
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        var all = Task.WhenAll(Enumerable.Range(0, Math.Min(CheckWorkers, snapshot.Count)).Select(_ => Worker()));
        await Task.WhenAny(all, Task.Delay(OpenCheckTimeout, token)).ConfigureAwait(true);
        // Workers still running at the deadline are not awaited, but a fault they hit is still observed exactly once: either thrown here or reported through ReportLate.
        _ = all.ContinueWith(t => { var late = Volatile.Read(ref fault) ?? t.Exception?.GetBaseException(); if (late != null && Interlocked.Exchange(ref reported, 1) == 0) ReportLate(late); }, TaskScheduler.Default);
        (Item Item, LaunchConfig? Launch, PreflightFinding Finding, OpenState Dup)?[] done;
        lock (gate) { published = true; done = ((Item Item, LaunchConfig? Launch, PreflightFinding Finding, OpenState Dup)?[])results.Clone(); }
        var f0 = Volatile.Read(ref fault);
        if (f0 != null)
        {
            if (Interlocked.Exchange(ref reported, 1) == 0) ExceptionDispatchInfo.Capture(f0).Throw();
            if (gen == _generation) { IsChecking = false; Status = "Open stopped by an unexpected error: " + f0.GetType().Name; } // already reported through ReportLate
            return;
        }
        if (gen != _generation || token.IsCancellationRequested) return;
        stop.Cancel();
        IsChecking = false;
        var plan = new List<(Item Item, LaunchConfig? Launch, PreflightFinding Finding, OpenState Dup)>();
        for (int i = 0; i < snapshot.Count; i++)
        {
            var r = done[i];
            if (r is { } ok) plan.Add(ok);
            else if (snapshot[i].Item is { } it) plan.Add((it, snapshot[i].M.Launch, new(PreflightState.Unavailable, "check did not finish in time"), OpenState.Unknown));
        }
        var bad = plan.Where(p => p.Finding.State != PreflightState.Ready || p.Dup != OpenState.NotOpen).ToList();
        if (bad.Count == 0) { await LaunchAsync(plan); return; }
        _pendingOpen = plan;
        foreach (var b in bad) OpenProblems.Add($"{b.Item.Name}: {Describe(b.Finding, b.Dup)}");
        var skipped = bad.Count(b => NeverLaunched(b.Finding.State)); var confirmable = bad.Count - skipped;
        var onlyUnknown = bad.Count(b => !NeverLaunched(b.Finding.State) && b.Finding.State == PreflightState.Ready && b.Dup == OpenState.Unknown);
        var msg = new List<string>();
        if (skipped > 0) msg.Add($"{skipped} {(skipped == 1 ? "item" : "items")} will be skipped.");
        if (confirmable > 0) msg.Add($"{confirmable} {(confirmable == 1 ? "item" : "items")} can be opened after your confirmation" + (onlyUnknown > 0 ? $" (for {onlyUnknown} we only cannot tell whether {(onlyUnknown == 1 ? "it is" : "they are")} already open)." : "."));
        msg.Add("Nothing has been opened yet.");
        Status = string.Join(" ", msg);
        Raise(nameof(OpenDecisionPending)); RaiseCommands();
    }

    private static string Describe(PreflightFinding f, OpenState dup)
    {
        var parts = new List<string>();
        if (f.State == PreflightState.Missing) parts.Add("not found" + (f.Detail is null ? "" : " (" + f.Detail + ")"));
        else if (f.State == PreflightState.Unsupported) return "will not be opened (" + f.Detail + ")";
        else if (f.State == PreflightState.NeedsDecision) return "will be skipped, not opened (" + f.Detail + ")";
        else if (f.State == PreflightState.Unavailable) parts.Add("could not be checked" + (f.Detail is null ? "" : " (" + f.Detail + ")"));
        if (dup == OpenState.Open) parts.Add("already open (confirmed), opening again will create a duplicate");
        else if (dup == OpenState.Unknown) parts.Add("cannot tell whether it is already open, opening may create a duplicate");
        return string.Join("; ", parts);
    }

    private void ConfirmOpen() { var plan = _pendingOpen!; ClearPending(); var gen = ++_generation; OpenTask = Guard(gen, LaunchAsync(plan)); }

    /// <summary>The last unexpected error that arrived after its operation was already over (late worker fault) and had no <see cref="UnexpectedError"/> subscriber.</summary>
    public Exception? LateError { get; private set; }
    private void ReportLate(Exception e) { var h = UnexpectedError; if (h is null) LateError = e; else h(e); }

    /// <summary>Raised for an unexpected (programming) error in an open operation, after the view model has reset its busy state and published what it knows.
    /// The host decides how to surface it (the App rethrows it on the UI dispatcher so it is not silent). With no subscriber the error is rethrown into <see cref="OpenTask"/>.</summary>
    public event Action<Exception>? UnexpectedError;

    /// <summary>A fire-and-forget command must not leave a silently faulted task. Resets busy flags (only if this operation is still the current one),
    /// shows a status, then hands the error to the host; it is never swallowed.</summary>
    private async Task Guard(int gen, Task t)
    {
        try { await t; }
        catch (Exception e)
        {
            if (gen == _generation) { IsChecking = false; IsLaunching = false; Status = "Open stopped by an unexpected error: " + e.GetType().Name + ". See the report for what was already sent."; }
            var h = UnexpectedError;
            if (h is null) throw;
            h(e);
        }
    }

    private static bool NeverLaunched(PreflightState s) => s is PreflightState.Unsupported or PreflightState.NeedsDecision;

    /// <summary>Starts items one after another on a background worker (Process.Start can block). Cancel stops FURTHER items only: requests already sent cannot be undone
    /// and are reported. A start call that is blocked in the OS cannot be interrupted; only the single launch worker is ever held by it.</summary>
    private async Task LaunchAsync(List<(Item Item, LaunchConfig? Launch, PreflightFinding Finding, OpenState Dup)> plan)
    {
        OpenReport.Clear(); _launchCts = new CancellationTokenSource(); var cts = _launchCts; IsLaunching = true; Status = "Opening items...";
        var res = new List<(string Line, int Ok, int Refused, int NotTried)>(); // filled by the worker; read only after it finished
        Exception? fault = null;
        await Task.Run(() =>
        {
            try { foreach (var p in plan)
            {
                if (cts.IsCancellationRequested) { res.Add(($"{p.Item.Name}: not attempted (cancelled before this item)", 0, 0, 1)); continue; }
                if (NeverLaunched(p.Finding.State)) { res.Add(($"{p.Item.Name}: skipped, not opened ({p.Finding.Detail})", 0, 1, 0)); continue; }
                var r = _launcher.Launch(p.Item, p.Launch);
                if (r.Status == LaunchStatus.Started)
                    res.Add(($"{p.Item.Name}: open request sent (not proof it loaded)" + (p.Dup == OpenState.Open ? "; was already open (confirmed), so this may be a duplicate" : p.Dup == OpenState.Unknown ? "; could not tell if it was already open, so it may be a duplicate" : ""), 1, 0, 0));
                else res.Add(($"{p.Item.Name}: failed ({r.Detail ?? r.Status.ToString()})", 0, 0, 0));
            } }
            catch (Exception e) { fault = e; } // keep the partial results, then surface the bug below
        }).ConfigureAwait(true);
        if (fault != null) foreach (var p in plan.Skip(res.Count)) res.Add(($"{p.Item.Name}: not attempted (stopped by an unexpected error)", 0, 0, 1));
        var lines = res;
        int ok = lines.Sum(l => l.Ok), refused = lines.Sum(l => l.Refused), notTried = lines.Sum(l => l.NotTried);
        foreach (var l in lines) OpenReport.Add(l.Line);
        if (ok > 0) Apply(c => c.TouchLastOpened(_openWsId)); // the card's "last opened" is set only after a real open request went out, on the workspace that was opened even if the selection moved since
        IsLaunching = false;
        if (fault != null) ExceptionDispatchInfo.Capture(fault).Throw();
        var failed = plan.Count - ok - refused - notTried;
        var parts = new List<string>();
        if (refused > 0) parts.Add($"{refused} skipped by policy");
        if (failed > 0) parts.Add($"{failed} could not be started");
        if (notTried > 0) parts.Add($"{notTried} not attempted because you cancelled; requests already sent cannot be undone");
        Status = parts.Count == 0 ? $"Open requests sent for {ok} {(ok == 1 ? "item" : "items")}"
            : $"Open requests sent for {ok} of {plan.Count} items ({string.Join(", ", parts)})";
    }
}
