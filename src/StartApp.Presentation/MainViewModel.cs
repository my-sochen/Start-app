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
    private List<(Item Item, LaunchConfig? Launch, PreflightFinding Finding)>? _pendingOpen;
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

    public MainViewModel(IStoreRepository repo, IItemLauncher launcher, IItemPreflight? preflight = null)
    {
        _repo = repo; _launcher = launcher; _preflight = preflight;
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

    /// <summary>Every item is checked first. With no problems everything is launched. With problems nothing starts; the user sees each item's reason and
    /// chooses to open anyway or cancel. After "anyway", Unsupported items (alias, non-http(s) link, bad config) are never launched; Missing and
    /// Unavailable items are attempted exactly as saved, with no substitute app, and each outcome is reported per item. Additive: nothing is closed.
    /// Already-open (duplicate) detection is not implemented.</summary>
    private void Open()
    {
        ClearPending(); OpenReport.Clear();
        if (Selected!.Items.Count == 0) { Status = "Workspace is empty, nothing to open"; return; }
        var plan = new List<(Item, LaunchConfig?, PreflightFinding)>();
        foreach (var m in Selected!.Items)
            if (_current.Items.TryGetValue(m.ItemId, out var it))
                plan.Add((it, m.Launch, PreflightRules.CheckForm(it, m.Launch) ?? _preflight?.Check(it, m.Launch) ?? new(PreflightState.Ready)));
        var bad = plan.Where(p => p.Item3.State != PreflightState.Ready).ToList();
        if (bad.Count == 0) { Launch(plan); return; }
        _pendingOpen = plan;
        foreach (var b in bad) OpenProblems.Add($"{b.Item1.Name}: {Describe(b.Item3)}");
        Status = $"{bad.Count} of {plan.Count} items have problems. Nothing was opened yet.";
        Raise(nameof(OpenDecisionPending)); RaiseCommands();
    }

    private static string Describe(PreflightFinding f) => f.State switch
    {
        PreflightState.Missing => "not found" + (f.Detail is null ? "" : " (" + f.Detail + ")"),
        PreflightState.Unsupported => "will not be opened (" + f.Detail + ")",
        _ => "could not be checked" + (f.Detail is null ? "" : " (" + f.Detail + ")"),
    };

    private void ConfirmOpen() { var plan = _pendingOpen!; ClearPending(); Launch(plan); }

    private void Launch(List<(Item Item, LaunchConfig? Launch, PreflightFinding Finding)> plan)
    {
        OpenReport.Clear(); int ok = 0, refused = 0;
        foreach (var p in plan)
        {
            if (p.Finding.State == PreflightState.Unsupported) { refused++; OpenReport.Add($"{p.Item.Name}: not opened ({p.Finding.Detail})"); continue; }
            var r = _launcher.Launch(p.Item, p.Launch);
            if (r.Status == LaunchStatus.Started) { ok++; OpenReport.Add($"{p.Item.Name}: started"); }
            else OpenReport.Add($"{p.Item.Name}: not opened ({r.Detail ?? r.Status.ToString()})");
        }
        var notOpened = plan.Count - ok;
        Status = notOpened == 0 ? $"Opened {ok} items" : $"Opened {ok} of {plan.Count} items ({notOpened} not opened{(refused > 0 ? $", {refused} never opened by policy" : "")})";
    }
}
