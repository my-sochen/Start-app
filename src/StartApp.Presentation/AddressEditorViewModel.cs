using StartApp.Core;

namespace StartApp.Presentation;

public sealed partial class MainViewModel
{
    // ---- address popup (פופאפ עריכת כתובת): full address, editable, copy button, save applies to this instance only ----
    public bool AddressEditOpen { get => _addressEditOpen; private set { Set(ref _addressEditOpen, value); if (!value) { _addressStaged = null; _addressInstId = ""; _addressWsId = ""; } RaiseCommands(); } }
    private bool _addressEditOpen;
    private string _addressInstId = "", _addressWsId = "";
    public string EditAddress { get => _editAddress; set { Set(ref _editAddress, value ?? ""); Raise(nameof(AddressEditDirty)); RaiseCommands(); } }
    private string _editAddress = "";
    private string _origAddress = "";
    /// <summary>Any change in the address popup shows its Save button (same rule as the other edit screens).</summary>
    public bool AddressEditDirty => AddressEditOpen && EditAddress != _origAddress;
    private StagedRow? _addressStaged;
    // At most one item/address editor is active at a time. Opening another editor while one is dirty is
    // refused with a clear status, so a pending draft is never silently replaced (PR19 review: clicking a
    // second row's Edit dropped the first draft; an explicitly saved popup address was later overwritten by
    // the still-open stale inline draft on save-and-exit). A clean editor closes silently, matching how
    // navigation and deletion already treat clean editors. Commands stay executable so a refused open can
    // report why; the button state mirrors this through CanExecute.
    private bool TryOpenEditorForm()
    {
        if (ItemEditDirty) { Status = "Save or cancel the open item edit first"; return false; }
        if (AddressEditDirty) { Status = "Save or cancel the open address edit first"; return false; }
        ItemEditOpen = false; AddressEditOpen = false;
        return true;
    }
    private void BeginAddressEdit(ItemRow row) { if (!TryOpenEditorForm()) return; _addressStaged = null; _addressWsId = Selected?.Id ?? ""; _addressInstId = row.InstanceId; _origAddress = row.Target; EditAddress = row.Target; AddressEditOpen = true; }
    /// <summary>עריכת כתובת on a staged row: the same address popup a saved item gets, but שמור writes the staged row's
    /// address (staged until the capture's own שמור) instead of the store.</summary>
    private void BeginAddressEditStaged(StagedRow row) { if (!TryOpenEditorForm()) return; _addressInstId = ""; _addressWsId = ""; _addressStaged = row; _origAddress = row.Target; EditAddress = row.Target; AddressEditOpen = true; }
    private bool SaveAddress()
    {
        if (_addressStaged is { } sa)
        {
            var errS = Catalog.ValidateInputTarget(sa.Type, EditAddress);
            if (errS != null) { FieldError("address", errS == Catalog.WebsiteTargetError ? errS : "הכתובת אינה תקינה. הזינו כתובת מלאה בהתאם לסוג הפריט.", "Not saved: " + errS); return false; }
            sa.Target = EditAddress; Status = "Address updated on the staged item (saved to the workspace on שמור)"; AddressEditOpen = false;
            return true;
        }
        // Same binding rule as the item editor: validate the workspace+instance captured when the popup
        // opened, never the current selection; a missing record keeps the typed address in the form.
        var wsA = _current.Workspaces.FirstOrDefault(w => w.Id == _addressWsId);
        var m = wsA?.Items.FirstOrDefault(x => (x.Id ?? x.ItemId) == _addressInstId);
        if (m is null || !_current.Items.TryGetValue(m.ItemId, out var it)) { Status = "Not saved: the saved item record is missing; your address is still in the form"; return false; }
        var err = Catalog.ValidateInputTarget(it.Type, EditAddress);
        if (err != null) { FieldError("address", err == Catalog.WebsiteTargetError ? err : "הכתובת אינה תקינה. הזינו כתובת מלאה בהתאם לסוג הפריט.", "Not saved: " + err); return false; }
        // The change applies to the captured workspace only, for every type including sites: ReplaceTargetLocal re-points this one
        // instance at the new address; the same item in other workspaces and other instances here keep their own address.
        var ok = Apply(c =>
        {
            // Same inside-the-mutation validation as the item editor: Mutate reloads disk state, so the
            // precheck snapshot can be stale; a missing captured record aborts with a handled ArgumentException.
            var wsNow = c.Store.Workspaces.FirstOrDefault(w => w.Id == _addressWsId);
            var mNow = wsNow?.Items.FirstOrDefault(x => (x.Id ?? x.ItemId) == _addressInstId);
            if (mNow is null || !c.Store.Items.ContainsKey(mNow.ItemId)) throw new ArgumentException("the item no longer exists in the saved data; your address is still in the form");
            c.ReplaceTargetLocal(_addressWsId, _addressInstId, EditAddress);
        });
        if (ok) { Status = "Address saved"; AddressEditOpen = false; }
        return ok;
    }
    private void CloseAddressEdit()
    {
        if (AddressEditDirty) { MarkUnsavedHost(false, false, true); _exitAction = () => AddressEditOpen = false; UnsavedOpen = true; return; }
        AddressEditOpen = false;
    }

}
