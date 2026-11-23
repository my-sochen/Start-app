using StartApp.Core;
namespace StartApp.Presentation;

public sealed partial class MainViewModel
{
    public SavedItemTransfer? SavedTransferFromDetail(ItemRow row) => SavedTransferFromInstance(Selected?.Id, row.InstanceId);
    public SavedItemTransfer? SavedTransferFromAll(AllItemRow row)
    {
        if (row.MixedTypes) return null;
        var first = MatchingInstances(row).FirstOrDefault();
        return first.Member is null ? null : SavedTransferFromInstance(first.Workspace.Id, first.Member.Id ?? first.Member.ItemId);
    }
    public SavedItemTransfer? SavedTransferFromGlobal(GlobalEntryRow row) => _current is not null && new Catalog(_current).GlobalEntries(row.Kind).Any(e => e.Id == row.Id)
        ? new(row.Entry.Type, row.Name, row.Target, "", null, ListKind: row.Kind, EntryId: row.Id) : null;
    private SavedItemTransfer? SavedTransferFromInstance(string? workspaceId, string? instanceId)
    {
        var w = _current.Workspaces.FirstOrDefault(w => w.Id == workspaceId);
        var m = w?.Items.FirstOrDefault(m => (m.Id ?? m.ItemId) == instanceId);
        if (m is null || !_current.Items.TryGetValue(m.ItemId, out var item)) return null;
        return new(item.Type, m.Name ?? item.Name, item.Target, m.Description ?? item.Description ?? "", m.Launch, w!.Id, m.Id ?? m.ItemId);
    }
    public bool SavedItemTransferAllowed => !CaptureOpen && ItemActionsAllowed && !GlobalConflictOpen && !UnsavedOpen && !DetailsDirty && !CreateDirty && !DesignDirty;
    public IReadOnlyList<Workspace> SavedItemDestinations(SavedItemTransfer source) => SavedItemTransferService.Destinations(_current, source);
    private void RefreshAfterSavedTransfer()
    {
        RefreshItems(); RefreshGlobalEnvRows();
        if (InAllItems) RefreshAllItems();
    }
    public bool AddSavedItemToWorkspaces(SavedItemTransfer source, IReadOnlyList<string> ids)
    {
        if (!SavedItemTransferAllowed) return false;
        var ok = Apply(c => SavedItemTransferService.AddToWorkspaces(c, source, ids));
        if (ok) { RefreshAfterSavedTransfer(); ShowToast("האייטם נוסף לסביבות שנבחרו. המקור לא השתנה.", false); }
        return ok;
    }
    public bool CreateWorkspaceFromSavedItem(SavedItemTransfer source, string name)
    {
        if (!SavedItemTransferAllowed) return false;
        var ok = Apply(c => SavedItemTransferService.AddToNewWorkspace(c, source, name));
        if (ok) { RefreshAfterSavedTransfer(); ShowToast("נוצרה סביבה חדשה עם האייטם. המקור לא השתנה.", false); }
        return ok;
    }
    public string SavedItemListConflict { get; private set; } = "";
    public bool AddSavedItemToList(SavedItemTransfer source, GlobalListKind destination, bool confirmedMove = false)
    {
        SavedItemListConflict = "";
        if (!SavedItemTransferAllowed) return false;
        try
        {
            var saved = _repo.Mutate(c => SavedItemTransferService.AddToGlobalList(c, source, destination, confirmedMove));
            Show(saved); RefreshAfterSavedTransfer(); ShowToast("האייטם נמצא ברשימה המבוקשת.", false); return true;
        }
        catch (ArgumentException e) when (e.Message is Catalog.WhitelistConflictError or Catalog.BlacklistConflictError)
        { SavedItemListConflict = e.Message; return false; }
        catch (Exception e) when (e is ArgumentException or InvalidOperationException or System.IO.IOException or UnauthorizedAccessException or StoreLockedException)
        { Status = e.Message; ShowToast("האייטם לא נוסף. הנתונים לא השתנו.", true, e.Message); return false; }
    }
}
