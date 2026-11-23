using StartApp.Core;

namespace StartApp.Presentation;

public sealed partial class MainViewModel
{
    // ---- item edit form (טופס עריכת אייטם): per-instance name and description of one instance ----
    public bool ItemEditOpen { get => _itemEditOpen; private set { Set(ref _itemEditOpen, value); Raise(nameof(EditingItemInstanceId)); RaiseCommands(); } }
    public string EditingItemInstanceId => ItemEditOpen && _editItemWsId == Selected?.Id ? _editItemId : "";
    private bool _itemEditOpen;
    private string _editItemId = "", _editItemWsId = "";
    public string EditItemName { get => _editItemName; set { Set(ref _editItemName, value ?? ""); Raise(nameof(ItemEditDirty)); RaiseCommands(); } }
    private string _editItemName = "";
    public string EditItemDescription { get => _editItemDesc; set { Set(ref _editItemDesc, value ?? ""); Raise(nameof(ItemEditDirty)); RaiseCommands(); } }
    private string _editItemDesc = "";
    /// <summary>The instance's address, editable in the full edit form (spec: כל נתוני האייטם הניתנים לעריכה, כולל הכתובת).</summary>
    public string EditItemTarget { get => _editItemTarget; set { Set(ref _editItemTarget, value ?? ""); Raise(nameof(ItemEditDirty)); RaiseCommands(); } }
    private string _editItemTarget = "";
    private string _origItemName = "", _origItemDesc = "", _origItemTarget = "";
    /// <summary>Any change in the item edit form shows its Save button (same rule as the details screen).</summary>
    private string _editItemOpenWith = "", _origItemOpenWith = "";
    private ItemType _editItemType;
    public string EditItemOpenWithDefaultLabel => _editItemType == ItemType.Folder ? "ללא" : "ברירת המחדל של Windows";
    public bool EditItemOpenWithApplicable => _editItemType != ItemType.Program;
    public string EditItemTargetLabel => _editItemType == ItemType.Link ? "כתובת האתר" : "נתיב";
    public string EditItemOpenWith { get => _editItemOpenWith; set { Set(ref _editItemOpenWith, value ?? ""); Raise(nameof(ItemEditDirty)); RaiseCommands(); } }
    public bool ItemEditDirty => ItemEditOpen && (EditItemName != _origItemName || EditItemDescription != _origItemDesc || EditItemTarget != _origItemTarget || EditItemOpenWith != _origItemOpenWith);
    private void BeginItemEdit(ItemRow row)
    {
        if (!TryOpenEditorForm()) return;
        ClearFieldErrors();
        _editItemWsId = Selected?.Id ?? ""; _editItemId = row.InstanceId; _origItemName = row.Name; _origItemDesc = row.Description ?? ""; _origItemTarget = row.Target;
        _editItemType = Enum.Parse<ItemType>(row.Type);
        var membership = Selected?.Items.FirstOrDefault(m => (m.Id ?? m.ItemId) == row.InstanceId);
        _origItemOpenWith = membership?.Launch?.Browser ?? membership?.Launch?.OpenWith ?? ""; EditItemOpenWith = _origItemOpenWith; Raise(nameof(EditItemOpenWithApplicable)); Raise(nameof(EditItemOpenWithDefaultLabel)); Raise(nameof(EditItemTargetLabel));
        EditItemName = _origItemName; EditItemDescription = _origItemDesc; EditItemTarget = _origItemTarget;
        ItemEditOpen = true; Raise(nameof(EditingItemInstanceId));
    }
    private bool SaveItemEdit()
    {
        if (string.IsNullOrWhiteSpace(EditItemName)) { FieldError("item", "חסר שם לפריט. הזינו שם כדי לשמור.", "Not saved: item name is required"); return false; }
        // Validate against the workspace+instance captured when the editor opened, never the current
        // selection (PR19 review: the row can be deleted or the view switched while the editor is open).
        // A missing target never throws and never silently retargets: the typed changes stay in the form
        // so the user decides (cancel, or re-add the item).
        var ws = _current.Workspaces.FirstOrDefault(w => w.Id == _editItemWsId);
        if (ws is null || !ws.Items.Any(x => (x.Id ?? x.ItemId) == _editItemId))
        { Status = "Not saved: this item was removed; your changes are still in the form - cancel the edit or add the item again"; return false; }
        var editedItem = _current.Items.GetValueOrDefault(ws.Items.First(x => (x.Id ?? x.ItemId) == _editItemId).ItemId);
        if (editedItem is not null && (Catalog.ValidateInputTarget(editedItem.Type, EditItemTarget) ?? Catalog.ValidateDescription(EditItemDescription)) is { } itemError)
        { FieldError("item", itemError == Catalog.WebsiteTargetError ? itemError : "בדקו את כתובת הפריט ואת התיאור לפני השמירה.", "Not saved: " + itemError); return false; }
        var targetChanged = EditItemTarget != _origItemTarget;
        LaunchConfig? editedLaunch = string.IsNullOrWhiteSpace(EditItemOpenWith) ? null : _editItemType == ItemType.Link ? new LaunchConfig(Browser: EditItemOpenWith.Trim()) : new LaunchConfig(OpenWith: EditItemOpenWith.Trim());
        if (EditItemOpenWith != _origItemOpenWith && Catalog.ValidateLaunch(_editItemType, editedLaunch) is { } launchError) { FieldError("item", "יש להזין נתיב מלא לתוכנה או לדפדפן.", launchError); return false; }
        var ok = Apply(c =>
        {
            // Validate again inside the mutation: Mutate reloads the current disk state, so the snapshot
            // precheck above can be stale (the record removed outside this VM without a reload). A missing
            // captured target aborts the mutation with an ArgumentException, which Apply handles as
            // "Save failed, nothing was changed" - no partial write, no throw, the draft stays in the form.
            var wsNow = c.Store.Workspaces.FirstOrDefault(w => w.Id == _editItemWsId);
            var mNow = wsNow?.Items.FirstOrDefault(x => (x.Id ?? x.ItemId) == _editItemId);
            if (mNow is null || !c.Store.Items.ContainsKey(mNow.ItemId)) throw new ArgumentException("the edited item no longer exists in the saved data; your changes are still in the form");
            // A target change is a local edit: only this instance points at the new address (Catalog.ReplaceTargetLocal);
            // the same item in other workspaces keeps its address, and the instance id stays stable.
            if (targetChanged) c.ReplaceTargetLocal(_editItemWsId, _editItemId, EditItemTarget);
            c.UpdateInstance(_editItemWsId, _editItemId, EditItemName, EditItemDescription);
            if (EditItemOpenWith != _origItemOpenWith) c.SetLaunch(_editItemWsId, _editItemId, editedLaunch);
        });
        if (ok) { Status = "Item saved"; ItemEditOpen = false; }
        return ok;
    }
    private void CancelItemEdit()
    {
        if (ItemEditDirty) { MarkUnsavedHost(false, true, false); _exitAction = () => ItemEditOpen = false; UnsavedOpen = true; return; }
        ItemEditOpen = false;
    }
}
