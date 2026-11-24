using System.Windows.Input;
using StartApp.Core;

namespace StartApp.Presentation;

public sealed partial class MainViewModel
{
    /// <summary>עריכה from the row: opens the item edit form (טופס עריכת אייטם) for that one instance.</summary>
    public ICommand EditItemCommand { get; private set; } = null!;
    public ICommand SaveItemEditCommand { get; private set; } = null!;
    /// <summary>ביטול in the item edit form: closes immediately when untouched, otherwise asks via the unsaved-exit dialog.</summary>
    public ICommand CancelItemEditCommand { get; private set; } = null!;

    private void InitItemEditorCommands()
    {
        EditItemCommand = Cmd(p => BeginItemEdit((ItemRow)p!), p => !ReadOnly && Selected != null && p is ItemRow && !ItemEditDirty && !AddressEditDirty);
        SaveItemEditCommand = Cmd(_ => SaveItemEdit(), _ => ItemEditOpen && ItemEditDirty && !ReadOnly);
        CancelItemEditCommand = Cmd(_ => CancelItemEdit(), _ => ItemEditOpen);
    }

    // ---- item edit form (טופס עריכת אייטם): per-instance name and description of one instance ----
    private readonly ItemEditSession _itemEdit = new();
    public bool ItemEditOpen { get => _itemEdit.IsOpen; private set { if (_itemEdit.IsOpen != value) { _itemEdit.IsOpen = value; Raise(nameof(ItemEditOpen)); } Raise(nameof(EditingItemInstanceId)); RaiseCommands(); } }
    public string EditingItemInstanceId => ItemEditOpen && _itemEdit.WorkspaceId == Selected?.Id ? _itemEdit.InstanceId : "";
    public string EditItemName { get => _itemEdit.Name; set { var text = value ?? ""; if (_itemEdit.Name != text) { _itemEdit.Name = text; Raise(nameof(EditItemName)); } Raise(nameof(ItemEditDirty)); RaiseCommands(); } }
    public string EditItemDescription { get => _itemEdit.Description; set { var text = value ?? ""; if (_itemEdit.Description != text) { _itemEdit.Description = text; Raise(nameof(EditItemDescription)); } Raise(nameof(ItemEditDirty)); RaiseCommands(); } }
    public string EditItemTarget { get => _itemEdit.Target; set { var text = value ?? ""; if (_itemEdit.Target != text) { _itemEdit.Target = text; Raise(nameof(EditItemTarget)); } Raise(nameof(ItemEditDirty)); RaiseCommands(); } }
    public string EditItemOpenWithDefaultLabel => _itemEdit.Type == ItemType.Folder ? "ללא" : "ברירת המחדל של Windows";
    public bool EditItemOpenWithApplicable => _itemEdit.Type != ItemType.Program;
    public string EditItemTargetLabel => _itemEdit.Type == ItemType.Link ? "כתובת האתר" : "נתיב";
    public string EditItemOpenWith { get => _itemEdit.OpenWith; set { var text = value ?? ""; if (_itemEdit.OpenWith != text) { _itemEdit.OpenWith = text; Raise(nameof(EditItemOpenWith)); } Raise(nameof(ItemEditDirty)); RaiseCommands(); } }
    public bool ItemEditDirty => _itemEdit.IsDirty;
    private void BeginItemEdit(ItemRow row)
    {
        if (!TryOpenEditorForm()) return;
        ClearFieldErrors();
        _itemEdit.CaptureOriginal(Selected?.Id ?? "", row.InstanceId, row.Name, row.Description, row.Target, Enum.Parse<ItemType>(row.Type));
        var membership = Selected?.Items.FirstOrDefault(m => (m.Id ?? m.ItemId) == row.InstanceId);
        _itemEdit.OriginalOpenWith = membership?.Launch?.Browser ?? membership?.Launch?.OpenWith ?? ""; EditItemOpenWith = _itemEdit.OriginalOpenWith; Raise(nameof(EditItemOpenWithApplicable)); Raise(nameof(EditItemOpenWithDefaultLabel)); Raise(nameof(EditItemTargetLabel));
        EditItemName = _itemEdit.OriginalName; EditItemDescription = _itemEdit.OriginalDescription; EditItemTarget = _itemEdit.OriginalTarget;
        ItemEditOpen = true; Raise(nameof(EditingItemInstanceId));
    }
    private bool SaveItemEdit()
    {
        if (string.IsNullOrWhiteSpace(EditItemName)) { FieldError("item", "חסר שם לפריט. הזינו שם כדי לשמור.", "Not saved: item name is required"); return false; }
        // Validate against the workspace+instance captured when the editor opened, never the current
        // selection (PR19 review: the row can be deleted or the view switched while the editor is open).
        // A missing target never throws and never silently retargets: the typed changes stay in the form
        // so the user decides (cancel, or re-add the item).
        var ws = _current.Workspaces.FirstOrDefault(w => w.Id == _itemEdit.WorkspaceId);
        if (ws is null || !ws.Items.Any(x => (x.Id ?? x.ItemId) == _itemEdit.InstanceId))
        { Status = "Not saved: this item was removed; your changes are still in the form - cancel the edit or add the item again"; return false; }
        var editedItem = _current.Items.GetValueOrDefault(ws.Items.First(x => (x.Id ?? x.ItemId) == _itemEdit.InstanceId).ItemId);
        if (editedItem is not null && (Catalog.ValidateInputTarget(editedItem.Type, EditItemTarget) ?? Catalog.ValidateDescription(EditItemDescription)) is { } itemError)
        { FieldError("item", itemError == Catalog.WebsiteTargetError ? itemError : "בדקו את כתובת הפריט ואת התיאור לפני השמירה.", "Not saved: " + itemError); return false; }
        var targetChanged = EditItemTarget != _itemEdit.OriginalTarget;
        LaunchConfig? editedLaunch = string.IsNullOrWhiteSpace(EditItemOpenWith) ? null : _itemEdit.Type == ItemType.Link ? new LaunchConfig(Browser: EditItemOpenWith.Trim()) : new LaunchConfig(OpenWith: EditItemOpenWith.Trim());
        if (EditItemOpenWith != _itemEdit.OriginalOpenWith && Catalog.ValidateLaunch(_itemEdit.Type, editedLaunch) is { } launchError) { FieldError("item", "יש להזין נתיב מלא לתוכנה או לדפדפן.", launchError); return false; }
        var ok = Apply(c =>
        {
            // Validate again inside the mutation: Mutate reloads the current disk state, so the snapshot
            // precheck above can be stale (the record removed outside this VM without a reload). A missing
            // captured target aborts the mutation with an ArgumentException, which Apply handles as
            // "Save failed, nothing was changed" - no partial write, no throw, the draft stays in the form.
            var wsNow = c.Store.Workspaces.FirstOrDefault(w => w.Id == _itemEdit.WorkspaceId);
            var mNow = wsNow?.Items.FirstOrDefault(x => (x.Id ?? x.ItemId) == _itemEdit.InstanceId);
            if (mNow is null || !c.Store.Items.ContainsKey(mNow.ItemId)) throw new ArgumentException("the edited item no longer exists in the saved data; your changes are still in the form");
            // A target change is a local edit: only this instance points at the new address (Catalog.ReplaceTargetLocal);
            // the same item in other workspaces keeps its address, and the instance id stays stable.
            if (targetChanged) c.ReplaceTargetLocal(_itemEdit.WorkspaceId, _itemEdit.InstanceId, EditItemTarget);
            c.UpdateInstance(_itemEdit.WorkspaceId, _itemEdit.InstanceId, EditItemName, EditItemDescription);
            if (EditItemOpenWith != _itemEdit.OriginalOpenWith) c.SetLaunch(_itemEdit.WorkspaceId, _itemEdit.InstanceId, editedLaunch);
        });
        if (ok) { Status = "Item saved"; ItemEditOpen = false; }
        return ok;
    }
    private void CancelItemEdit()
    {
        if (ItemEditDirty) { MarkUnsavedHost(false, true, false); _unsavedExit.Continuation = () => ItemEditOpen = false; UnsavedOpen = true; return; }
        ItemEditOpen = false;
    }
}
