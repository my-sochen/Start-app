using System.Collections.ObjectModel;
using System.Windows.Input;
using StartApp.Core;
namespace StartApp.Presentation;
public sealed partial class MainViewModel
{
    public sealed record AllItemRow(string ItemId, string Name, string Target, string Type, string Group, string TypeLabel, IReadOnlyList<Workspace> Workspaces, bool MixedTypes = false);
    private bool _inAllItems;
    public bool InAllItems { get => _inAllItems; private set { Set(ref _inAllItems, value); Raise(nameof(InOverview)); RaiseCommands(); } }
    private string _allItemsSearch = "";
    private int _allItemsTotalCount;
    public string AllItemsSearch { get => _allItemsSearch; set { Set(ref _allItemsSearch, value ?? ""); RefreshAllItems(); } }
    public ObservableCollection<AllItemRow> AllItemRows { get; } = new();
    public string AllItemsCountText => string.IsNullOrWhiteSpace(AllItemsSearch) ? $"{_allItemsTotalCount} אייטמים · {_current.Workspaces.Count} סביבות" : $"{AllItemRows.Count} מתוך {_allItemsTotalCount} אייטמים · {_current.Workspaces.Count} סביבות";
    public ICommand OpenAllItemsCommand { get; private set; } = null!;
    public ICommand CloseAllItemsCommand { get; private set; } = null!;
    public ICommand OpenAllItemCommand { get; private set; } = null!;
    private void InitAllItemsCommands()
    {
        OpenAllItemsCommand = Cmd(_ => ShowAllItems(), _ => !InAllItems && !CaptureOpen && !AddFormVisible && !UnsavedOpen && !IsChecking && !IsLaunching && !SelectionPending && !OpenDecisionPending);
        CloseAllItemsCommand = Cmd(_ => InAllItems = false, _ => InAllItems);
        OpenAllItemCommand = Cmd(p => { if (p is AllItemRow row && !row.MixedTypes && _current.Items.TryGetValue(row.ItemId, out var item)) OpenOneCore(row.Name, item, null); }, p => InAllItems && !ReadOnly && p is AllItemRow { MixedTypes: false } && !IsChecking && !IsLaunching && !SelectionPending && !OpenDecisionPending);
    }
    private IEnumerable<(Workspace Workspace, Membership Member, Item Item)> ItemInstances() => _current.Workspaces.SelectMany(w => w.Items.Where(m => _current.Items.ContainsKey(m.ItemId)).Select(m => (w, m, _current.Items[m.ItemId])));
    private IEnumerable<(Workspace Workspace, Membership Member, Item Item)> MatchingInstances(AllItemRow row) => ItemInstances().Where(x => x.Member.ItemId == row.ItemId);
    private AllItemRow MakeAllItemRow(IEnumerable<(Workspace Workspace, Membership Member, Item Item)> instances)
    {
        var rows = instances.ToArray(); var first = rows[0]; var mixed = rows.Select(x => x.Item.Type).Distinct().Count() > 1;
        var label = mixed ? "סוגים שונים: יש לבחור מופע מתוך סביבה" : first.Item.Type switch { ItemType.Link => "אתר", ItemType.Program => "תוכנה", ItemType.File => "קובץ", _ => "תיקייה" };
        return new(first.Item.Id, first.Item.Name, first.Item.Target, first.Item.Type.ToString(), GroupOf(first.Item.Type), label, rows.Select(x => x.Workspace).DistinctBy(w => w.Id).ToArray(), mixed);
    }
    private void ShowAllItems()
    {
        void Show() { ItemEditOpen = false; AddressEditOpen = false; OpenWithOpen = false; InCreate = false; InDetails = false; InSettings = false; InGlobalEnv = false; _allItemsSearch = ""; Raise(nameof(AllItemsSearch)); RefreshAllItems(); InAllItems = true; }
        if (DetailsDirty || ItemEditDirty || AddressEditDirty || DesignDirty || CreateDirty) { MarkUnsavedHost(InCreate && CreateDirty, ItemEditDirty, AddressEditDirty, DesignDirty); _exitAction = Show; UnsavedOpen = true; return; }
        Show();
    }
    private void RefreshAllItems()
    {
        var all = ItemInstances().GroupBy(x => x.Member.ItemId).Select(MakeAllItemRow).ToArray();
        _allItemsTotalCount = all.Length; AllItemRows.Clear(); var query = AllItemsSearch.Trim();
        foreach (var row in all)
            if (query.Length == 0 || row.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || row.Target.Contains(query, StringComparison.OrdinalIgnoreCase) || row.Workspaces.Any(w => w.Name.Contains(query, StringComparison.OrdinalIgnoreCase)) || MatchingInstances(row).Any(x => (x.Member.Name ?? x.Item.Name).Contains(query, StringComparison.OrdinalIgnoreCase))) AllItemRows.Add(row);
        Raise(nameof(AllItemsCountText));
    }
}
