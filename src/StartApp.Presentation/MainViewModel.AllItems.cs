using System.Collections.ObjectModel;
using System.Windows.Input;
using StartApp.Core;
namespace StartApp.Presentation;
public sealed partial class MainViewModel
{
    public sealed record AllItemRow(string ItemId, string Name, string Target, string Type, string Group, string TypeLabel, IReadOnlyList<Workspace> Workspaces, bool MixedTypes = false, string Description = "", LaunchConfig? Launch = null);
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
        OpenAllItemCommand = Cmd(p => { if (p is AllItemRow row && !row.MixedTypes && _current.Items.TryGetValue(row.ItemId, out var item)) OpenOneCore(row.Name, item, row.Launch); }, p => InAllItems && !ReadOnly && p is AllItemRow { MixedTypes: false } && !IsChecking && !IsLaunching && !SelectionPending && !OpenDecisionPending);
    }
    private IEnumerable<(Workspace Workspace, Membership Member, Item Item)> ItemInstances() => _current.Workspaces.SelectMany(w => w.Items.Where(m => _current.Items.ContainsKey(m.ItemId)).Select(m => (w, m, _current.Items[m.ItemId])));
    // The general list groups by every effective item field, not the shared resource id.
    // Instance ids, workspace ids and order are locations, not editable item values.
    private sealed record ItemIdentity(string Type, string Name, string Target, string Description, string Browser, string OpenWith);
    private static ItemIdentity IdentityOf(Item item, Membership member) => new(item.Type.ToString(), member.Name ?? item.Name, item.Target, member.Description ?? item.Description ?? "", member.Launch?.Browser ?? "", member.Launch?.OpenWith ?? "");
    private static ItemIdentity IdentityOf(AllItemRow row) => new(row.Type, row.Name, row.Target, row.Description, row.Launch?.Browser ?? "", row.Launch?.OpenWith ?? "");
    private IEnumerable<(Workspace Workspace, Membership Member, Item Item)> MatchingInstances(AllItemRow row) => ItemInstances().Where(x => IdentityOf(x.Item, x.Member) == IdentityOf(row));
    private AllItemRow MakeAllItemRow(IEnumerable<(Workspace Workspace, Membership Member, Item Item)> instances)
    {
        var rows = instances.ToArray(); var first = rows[0]; var mixed = rows.Select(x => x.Item.Type).Distinct().Count() > 1;
        var label = mixed ? "סוגים שונים: יש לבחור מופע מתוך סביבה" : first.Item.Type switch { ItemType.Link => "אתר", ItemType.Program => "תוכנה", ItemType.File => "קובץ", _ => "תיקייה" };
        return new(first.Item.Id, first.Member.Name ?? first.Item.Name, first.Item.Target, first.Item.Type.ToString(), GroupOf(first.Item.Type), label, rows.Select(x => x.Workspace).DistinctBy(w => w.Id).ToArray(), mixed, first.Member.Description ?? first.Item.Description ?? "", first.Member.Launch);
    }
    private void ShowAllItems()
    {
        void Show() { ItemEditOpen = false; AddressEditOpen = false; OpenWithOpen = false; InCreate = false; InDetails = false; InSettings = false; InGlobalEnv = false; _allItemsSearch = ""; Raise(nameof(AllItemsSearch)); RefreshAllItems(); InAllItems = true; }
        if (DetailsDirty || ItemEditDirty || AddressEditDirty || DesignDirty || CreateDirty) { MarkUnsavedHost(InCreate && CreateDirty, ItemEditDirty, AddressEditDirty, DesignDirty); _exitAction = Show; UnsavedOpen = true; return; }
        Show();
    }
    private void RefreshAllItems()
    {
        var all = ItemInstances().GroupBy(x => IdentityOf(x.Item, x.Member)).Select(MakeAllItemRow).ToArray();
        _allItemsTotalCount = all.Length; AllItemRows.Clear(); var query = AllItemsSearch.Trim();
        foreach (var row in all)
            if (query.Length == 0 || row.Name.Contains(query, StringComparison.OrdinalIgnoreCase) || row.Target.Contains(query, StringComparison.OrdinalIgnoreCase) || row.Workspaces.Any(w => w.Name.Contains(query, StringComparison.OrdinalIgnoreCase))) AllItemRows.Add(row);
        Raise(nameof(AllItemsCountText));
    }
}
