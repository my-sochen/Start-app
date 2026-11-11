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
    public IReadOnlyList<ItemType> ItemTypes { get; } = Enum.GetValues<ItemType>();

    public MainViewModel(IStoreRepository repo, IItemLauncher launcher)
    {
        _repo = repo; _launcher = launcher;
        CreateWorkspaceCommand = Cmd(_ => Create(), _ => !ReadOnly && NewName.Length > 0);
        DeleteWorkspaceCommand = Cmd(_ => Apply(c => c.DeleteWorkspace(Selected!.Id)), _ => !ReadOnly && Selected != null);
        RemoveItemCommand = Cmd(p => Apply(c => c.RemoveFromWorkspace(Selected!.Id, ((ItemRow)p!).ItemId)), p => !ReadOnly && Selected != null && p is ItemRow);
        AddItemCommand = Cmd(_ => AddItem(), _ => !ReadOnly && Selected != null && NewTarget.Length > 0);
        OpenWorkspaceCommand = Cmd(_ => Open(), _ => Selected != null);
        Reload();
    }

    public Workspace? Selected { get => _selected; set { Set(ref _selected, value); RefreshItems(); RaiseCommands(); } }
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

    /// <summary>Additive opening is not implemented yet: reports what the adapter says, per item, never as overall success unless every item started.</summary>
    private void Open()
    {
        var results = new List<LaunchResult>();
        if (Selected!.Items.Count == 0) { Status = "Workspace is empty, nothing to open"; return; }
        foreach (var m in Selected!.Items)
            if (_current.Items.TryGetValue(m.ItemId, out var it)) results.Add(_launcher.Launch(it, m.Launch));
        var ok = results.Count(r => r.Status == LaunchStatus.Started);
        Status = ok == results.Count ? $"Opened {ok} items" : $"Opened {ok} of {results.Count} items (partial: {results.Count - ok} not opened)";
    }
}
