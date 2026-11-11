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
    private void RaiseCommands() { foreach (var c in _commands) c.Raise(); }
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
    public IReadOnlyList<ItemType> ItemTypes { get; } = Enum.GetValues<ItemType>();

    public MainViewModel(IStoreRepository repo, IItemLauncher launcher, IItemPreflight? preflight = null, IOpenStateDetector? detector = null)
    {
        _repo = repo; _launcher = launcher; _preflight = preflight; _detector = detector ?? new NoOpenStateDetection();
        CreateWorkspaceCommand = Cmd(_ => Create(), _ => !ReadOnly && NewName.Length > 0);
        DeleteWorkspaceCommand = Cmd(_ => Apply(c => c.DeleteWorkspace(Selected!.Id)), _ => !ReadOnly && Selected != null);
        RemoveItemCommand = Cmd(p => Apply(c => c.RemoveFromWorkspace(Selected!.Id, ((ItemRow)p!).ItemId)), p => !ReadOnly && Selected != null && p is ItemRow);
        AddItemCommand = Cmd(_ => AddItem(), _ => !ReadOnly && Selected != null && NewTarget.Length > 0);
        OpenWorkspaceCommand = Cmd(_ => Open(), _ => Selected != null);
        ConfirmOpenAnywayCommand = Cmd(_ => ConfirmOpen(), _ => _pendingOpen != null);
        CancelOpenCommand = Cmd(_ => { ClearPending(); Status = "Open cancelled, nothing was opened"; }, _ => _pendingOpen != null);
        Reload();
    }

    private void ClearPending() { _pendingOpen = null; OpenProblems.Clear(); Raise(nameof(OpenDecisionPending)); RaiseCommands(); }

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
    private void Open()
    {
        ClearPending(); OpenReport.Clear();
        if (Selected!.Items.Count == 0) { Status = "Workspace is empty, nothing to open"; return; }
        var plan = new List<(Item, LaunchConfig?, PreflightFinding, OpenState)>();
        foreach (var m in Selected!.Items)
            if (_current.Items.TryGetValue(m.ItemId, out var it))
            {
                var f = PreflightRules.CheckForm(it, m.Launch) ?? _preflight?.Check(it, m.Launch) ?? new(PreflightState.Ready);
                var dup = f.State == PreflightState.Unsupported ? OpenState.NotOpen : _detector.Detect(it, m.Launch);
                plan.Add((it, m.Launch, f, dup));
            }
        var bad = plan.Where(p => p.Item3.State != PreflightState.Ready || p.Item4 != OpenState.NotOpen).ToList();
        if (bad.Count == 0) { Launch(plan); return; }
        _pendingOpen = plan;
        foreach (var b in bad) OpenProblems.Add($"{b.Item1.Name}: {Describe(b.Item3, b.Item4)}");
        Status = $"{bad.Count} of {plan.Count} items need your decision. Nothing was opened yet.";
        Raise(nameof(OpenDecisionPending)); RaiseCommands();
    }

    private static string Describe(PreflightFinding f, OpenState dup)
    {
        var parts = new List<string>();
        if (f.State == PreflightState.Missing) parts.Add("not found" + (f.Detail is null ? "" : " (" + f.Detail + ")"));
        else if (f.State == PreflightState.Unsupported) return "will not be opened (" + f.Detail + ")";
        else if (f.State == PreflightState.Unavailable) parts.Add("could not be checked" + (f.Detail is null ? "" : " (" + f.Detail + ")"));
        if (dup == OpenState.Open) parts.Add("already open (confirmed), opening again will create a duplicate");
        else if (dup == OpenState.Unknown) parts.Add("cannot tell whether it is already open, opening may create a duplicate");
        return string.Join("; ", parts);
    }

    private void ConfirmOpen() { var plan = _pendingOpen!; ClearPending(); Launch(plan); }

    private void Launch(List<(Item Item, LaunchConfig? Launch, PreflightFinding Finding, OpenState Dup)> plan)
    {
        OpenReport.Clear(); int ok = 0, refused = 0;
        foreach (var p in plan)
        {
            if (p.Finding.State == PreflightState.Unsupported) { refused++; OpenReport.Add($"{p.Item.Name}: skipped, not opened ({p.Finding.Detail})"); continue; }
            var r = _launcher.Launch(p.Item, p.Launch);
            if (r.Status == LaunchStatus.Started) { ok++; OpenReport.Add($"{p.Item.Name}: open request sent (not proof it loaded)" + (p.Dup == OpenState.Open ? "; was already open (confirmed), so this may be a duplicate" : p.Dup == OpenState.Unknown ? "; could not tell if it was already open, so it may be a duplicate" : "")); }
            else OpenReport.Add($"{p.Item.Name}: failed ({r.Detail ?? r.Status.ToString()})");
        }
        var notOpened = plan.Count - ok;
        Status = notOpened == 0 ? $"Open requests sent for {ok} items" : $"Open requests sent for {ok} of {plan.Count} items ({notOpened} not opened{(refused > 0 ? $", {refused} skipped by policy" : "")})";
    }
}
