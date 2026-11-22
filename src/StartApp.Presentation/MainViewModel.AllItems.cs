using System.Collections.ObjectModel;
using System.Windows.Input;
using StartApp.Core;

namespace StartApp.Presentation;

public sealed partial class MainViewModel
{
    public sealed record AllItemRow(string ItemId, string Name, string Target, string Type, string Group, string TypeLabel, IReadOnlyList<Workspace> Workspaces, string Aliases)
    {
        public bool HasSingleWorkspace => Workspaces.Count == 1;
    }
    private bool _inAllItems;
    public bool InAllItems { get => _inAllItems; private set { Set(ref _inAllItems, value); Raise(nameof(InOverview)); RaiseCommands(); } }
    private string _allItemsSearch = "";
    public string AllItemsSearch { get => _allItemsSearch; set { Set(ref _allItemsSearch, value ?? ""); RefreshAllItems(); } }
    public ObservableCollection<AllItemRow> AllItemRows { get; } = new();
    public string AllItemsCountText => string.IsNullOrWhiteSpace(AllItemsSearch)
        ? $"{_current.Items.Count} אייטמים · {_current.Workspaces.Count} סביבות"
        : $"{AllItemRows.Count} מתוך {_current.Items.Count} אייטמים · {_current.Workspaces.Count} סביבות";
    public ICommand OpenAllItemsCommand { get; private set; } = null!;
    public ICommand CloseAllItemsCommand { get; private set; } = null!;
    public ICommand OpenAllItemWorkspaceCommand { get; private set; } = null!;
    public ICommand OpenAllItemCommand { get; private set; } = null!;

    private void InitAllItemsCommands()
    {
        OpenAllItemsCommand = Cmd(_ => ShowAllItems(), _ => !InAllItems && !CaptureOpen && !AddFormVisible && !UnsavedOpen && !IsChecking && !IsLaunching && !SelectionPending && !OpenDecisionPending);
        CloseAllItemsCommand = Cmd(_ => InAllItems = false, _ => InAllItems);
        OpenAllItemWorkspaceCommand = Cmd(p =>
        {
            if (p is Workspace selected && _current.Workspaces.FirstOrDefault(w => w.Id == selected.Id) is { } fresh)
            { InAllItems = false; OpenDetails(fresh); }
        }, p => InAllItems && p is Workspace);
        OpenAllItemCommand = Cmd(p =>
        {
            if (p is AllItemRow row && _current.Items.TryGetValue(row.ItemId, out var item)) OpenOneCore(row.Name, item, null);
        }, p => InAllItems && !ReadOnly && p is AllItemRow && !IsChecking && !IsLaunching && !SelectionPending && !OpenDecisionPending);

    }
    public IReadOnlyList<Workspace> AllItemWorkspaces(AllItemRow row) => _current.Workspaces.Where(w => w.Items.Any(m => m.ItemId == row.ItemId)).ToArray();
    public void ActOnAllItem(AllItemRow row, Workspace workspace, bool delete)
    {
        if (!InAllItems || ReadOnly || IsChecking || IsLaunching || SelectionPending || OpenDecisionPending) return;
        var fresh = _current.Workspaces.FirstOrDefault(w => w.Id == workspace.Id);
        var member = fresh?.Items.FirstOrDefault(m => m.ItemId == row.ItemId);
        if (fresh is null || member is null) return;
        var instanceId = member.Id ?? member.ItemId;
        if (delete)
        {
            if (Apply(c => c.RemoveInstance(fresh.Id, instanceId))) { RefreshAllItems(); ShowToast("האייטם הוסר מהסביבה " + fresh.Name, false); }
            else ShowToast("האייטם לא הוסר. הנתונים לא השתנו.", true, Status);
            return;
        }
        InAllItems = false; OpenDetails(fresh);
        if (Selected?.Id == fresh.Id && Items.FirstOrDefault(i => i.InstanceId == instanceId) is { } detail) BeginItemEdit(detail);
    }
    private void ShowAllItems()
    {
        void Show()
        {
            ItemEditOpen = false; AddressEditOpen = false; OpenWithOpen = false; InCreate = false; InDetails = false; InSettings = false; InGlobalEnv = false;
            _allItemsSearch = ""; Raise(nameof(AllItemsSearch)); RefreshAllItems(); InAllItems = true;
        }
        if (DetailsDirty || ItemEditDirty || AddressEditDirty || DesignDirty || CreateDirty)
        {
            MarkUnsavedHost(InCreate && CreateDirty, ItemEditDirty, AddressEditDirty, DesignDirty);
            _exitAction = Show; UnsavedOpen = true; return;
        }
        Show();
    }
    private void RefreshAllItems()
    {
        AllItemRows.Clear();
        var query = AllItemsSearch.Trim();
        foreach (var item in _current.Items.Values)
        {
            var workspaces = _current.Workspaces.Where(w => w.Items.Any(m => m.ItemId == item.Id)).ToArray();
            var aliases = string.Join(" ", workspaces.SelectMany(w => w.Items.Where(m => m.ItemId == item.Id).Select(m => m.Name ?? "")));
            if (query.Length > 0 && !new[] { item.Name, item.Target, aliases, string.Join(" ", workspaces.Select(w => w.Name)) }.Any(s => s.Contains(query, StringComparison.OrdinalIgnoreCase))) continue;
            var typeLabel = item.Type switch { ItemType.Link => "אתר", ItemType.Program => "תוכנה", ItemType.File => "קובץ", _ => "תיקייה" };
            AllItemRows.Add(new(item.Id, item.Name, item.Target, item.Type.ToString(), GroupOf(item.Type), typeLabel, workspaces, aliases));
        }
        Raise(nameof(AllItemsCountText));
    }
}
