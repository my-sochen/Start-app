using System.Runtime.ExceptionServices;
using System.Collections.ObjectModel;
using System.Windows.Input;
using StartApp.Core;

namespace StartApp.Presentation;

public sealed record ItemRow(string ItemId, string Name, string Target, string Type);

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
    private void RaiseCommands() { foreach (var c in _commands) c.Raise(); Raise(nameof(OpenDecisionPending)); Raise(nameof(ShowOpenPanel)); }
    public ICommand CreateWorkspaceCommand { get; }
    public ICommand DeleteWorkspaceCommand { get; }
    public ICommand RemoveItemCommand { get; }
    public ICommand OpenWorkspaceCommand { get; }
    public ICommand AddItemCommand { get; }
    public ICommand ConfirmOpenAnywayCommand { get; }
    public ICommand CancelOpenCommand { get; }
    /// <summary>Per-item problems found before opening; the user decides. Empty when nothing is pending.</summary>
    public ObservableCollection<string> OpenProblems { get; } = new();
    /// <summary>Per-item outcome of the last open (one line per item).</summary>
    public ObservableCollection<string> OpenReport { get; } = new();
    public bool OpenDecisionPending => _pendingOpen != null;
    /// <summary>The open panel (with Cancel) is visible while a decision is pending AND while checking or launching, so the user can always cancel.</summary>
    public bool ShowOpenPanel => _pendingOpen != null || _isChecking || _isLaunching;
    public IReadOnlyList<ItemType> ItemTypes { get; } = Enum.GetValues<ItemType>();

    public MainViewModel(IStoreRepository repo, IItemLauncher launcher, IItemPreflight? preflight = null, IOpenStateDetector? detector = null)
    {
        _repo = repo; _launcher = launcher; _preflight = preflight; _detector = detector ?? new NoOpenStateDetection();
        CreateWorkspaceCommand = Cmd(_ => Create(), _ => !ReadOnly && NewName.Length > 0);
        DeleteWorkspaceCommand = Cmd(_ => Apply(c => c.DeleteWorkspace(Selected!.Id)), _ => !ReadOnly && Selected != null);
        RemoveItemCommand = Cmd(p => Apply(c => c.RemoveFromWorkspace(Selected!.Id, ((ItemRow)p!).ItemId)), p => !ReadOnly && Selected != null && p is ItemRow);
        AddItemCommand = Cmd(_ => AddItem(), _ => !ReadOnly && Selected != null && NewTarget.Length > 0);
        OpenWorkspaceCommand = Cmd(_ => Open(), _ => Selected != null && !IsChecking && !IsLaunching);
        ConfirmOpenAnywayCommand = Cmd(_ => ConfirmOpen(), _ => _pendingOpen != null);
        CancelOpenCommand = Cmd(_ => { var launching = IsLaunching; ClearPending(); if (!launching) Status = "Open cancelled, nothing was opened"; }, _ => _pendingOpen != null || IsChecking || IsLaunching);
        Reload();
    }

    private void ClearPending() { if (IsChecking) { _checkCts?.Cancel(); _generation++; IsChecking = false; } if (IsLaunching) _launchCts?.Cancel(); _pendingOpen = null; OpenProblems.Clear(); Raise(nameof(OpenDecisionPending)); RaiseCommands(); }

    public Workspace? Selected { get => _selected; set { ClearPending(); Set(ref _selected, value); RefreshItems(); RaiseCommands(); } }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public bool ReadOnly { get => _readOnly; private set { Set(ref _readOnly, value); RaiseCommands(); } }
    public ItemType NewItemType { get => _newType; set => Set(ref _newType, value); }
    public string NewTarget { get => _newTarget; set { Set(ref _newTarget, value); RaiseCommands(); } }
    private ItemType _newType = ItemType.File; private string _newTarget = "";
    public string NewName { get => _newName; set { Set(ref _newName, value); RaiseCommands(); } }

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

    private void RefreshItems()
    {
        Items.Clear(); if (Selected is null) return;
        foreach (var m in Selected.Items)
            if (_current.Items.TryGetValue(m.ItemId, out var it)) Items.Add(new ItemRow(it.Id, it.Name, it.Target, it.Type.ToString()));
    }

    /// <summary>Manual add. The target is used exactly as typed (never trimmed); invalid input is reported, not corrected.</summary>
    private void AddItem()
    {
        var type = NewItemType; var target = NewTarget; var wsId = Selected!.Id;
        var err = Catalog.ValidateTarget(type, target);
        if (err != null) { Status = "Not added: " + err; return; }
        if (Apply(c => { var it = c.GetOrAddItem(type, DisplayName(type, target), target); c.AddToWorkspace(wsId, it.Id); })) NewTarget = "";
    }

    private static string DisplayName(ItemType type, string target)
    {
        if (type == ItemType.Link && Uri.TryCreate(target, UriKind.Absolute, out var u) && u.Host.Length > 0) return u.Host;
        var i = target.LastIndexOfAny(new[] { '\\', '/' });
        return i >= 0 && i < target.Length - 1 ? target[(i + 1)..] : target;
    }

    private void Create() { var n = NewName; if (Apply(c => c.CreateWorkspace(n))) NewName = ""; }

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

    private void Open()
    {
        ClearPending(); OpenReport.Clear();
        if (Selected!.Items.Count == 0) { Status = "Workspace is empty, nothing to open"; return; }
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
        Status = $"{bad.Count} of {plan.Count} {(plan.Count == 1 ? "item needs" : "items need")} your decision. Nothing was opened yet.";
        Raise(nameof(OpenDecisionPending)); RaiseCommands();
    }

    private static string Describe(PreflightFinding f, OpenState dup)
    {
        var parts = new List<string>();
        if (f.State == PreflightState.Missing) parts.Add("not found" + (f.Detail is null ? "" : " (" + f.Detail + ")"));
        else if (f.State == PreflightState.Unsupported) return "will not be opened (" + f.Detail + ")";
        else if (f.State == PreflightState.NeedsDecision) return "will not be opened, it needs an explicit decision for this item (" + f.Detail + ")";
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
