using System.Windows.Input;
using StartApp.Core;

namespace StartApp.Presentation;

public sealed partial class MainViewModel
{
    /// <summary>עריכת כתובת from the row actions: opens the dedicated address popup (פופאפ עריכת כתובת).</summary>
    public ICommand EditAddressCommand { get; private set; } = null!;
    public ICommand SaveAddressCommand { get; private set; } = null!;
    /// <summary>The copy button inside the address popup: copies the full value as currently shown.</summary>
    public ICommand CopyFullAddressCommand { get; private set; } = null!;
    /// <summary>The popup's X: closes silently when untouched, otherwise asks via the unsaved-exit dialog.</summary>
    public ICommand CloseAddressEditCommand { get; private set; } = null!;

    private void InitAddressEditorCommands()
    {
        EditAddressCommand = Cmd(p => BeginItemEdit((ItemRow)p!), p => !ReadOnly && Selected != null && p is ItemRow && !ItemEditDirty && !AddressEditDirty);
        SaveAddressCommand = Cmd(_ => SaveAddress(), _ => AddressEditOpen && AddressEditDirty && !ReadOnly);
        CopyFullAddressCommand = Cmd(_ => { CopyAddressRequested?.Invoke(EditAddress); Status = "Address copied"; }, _ => AddressEditOpen);
        CloseAddressEditCommand = Cmd(_ => CloseAddressEdit(), _ => AddressEditOpen);
    }

    // ---- address popup (פופאפ עריכת כתובת): full address, editable, copy button, save applies to this instance only ----
    private readonly AddressEditSession<StagedRow> _addressEdit = new();
    public bool AddressEditOpen
    {
        get => _addressEdit.IsOpen;
        private set
        {
            if (_addressEdit.IsOpen != value) { _addressEdit.IsOpen = value; Raise(nameof(AddressEditOpen)); }
            if (!value) _addressEdit.ClearTarget();
            RaiseCommands();
        }
    }
    public string EditAddress { get => _addressEdit.Address; set { var text = value ?? ""; if (_addressEdit.Address != text) { _addressEdit.Address = text; Raise(nameof(EditAddress)); } Raise(nameof(AddressEditDirty)); RaiseCommands(); } }
    public bool AddressEditDirty => _addressEdit.IsDirty;
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
    private void BeginAddressEdit(ItemRow row) { if (!TryOpenEditorForm()) return; _addressEdit.StagedTarget = null; _addressEdit.WorkspaceId = Selected?.Id ?? ""; _addressEdit.InstanceId = row.InstanceId; _addressEdit.OriginalAddress = row.Target; EditAddress = row.Target; AddressEditOpen = true; }
    /// <summary>עריכת כתובת on a staged row: the same address popup a saved item gets, but שמור writes the staged row's
    /// address (staged until the capture's own שמור) instead of the store.</summary>
    private void BeginAddressEditStaged(StagedRow row) { if (!TryOpenEditorForm()) return; _addressEdit.InstanceId = ""; _addressEdit.WorkspaceId = ""; _addressEdit.StagedTarget = row; _addressEdit.OriginalAddress = row.Target; EditAddress = row.Target; AddressEditOpen = true; }
    private bool SaveAddress()
    {
        if (_addressEdit.StagedTarget is { } sa)
        {
            var errS = Catalog.ValidateInputTarget(sa.Type, EditAddress);
            if (errS != null) { FieldError("address", errS == Catalog.WebsiteTargetError ? errS : "הכתובת אינה תקינה. הזינו כתובת מלאה בהתאם לסוג הפריט.", "Not saved: " + errS); return false; }
            sa.Target = EditAddress; Status = "Address updated on the staged item (saved to the workspace on שמור)"; AddressEditOpen = false;
            return true;
        }
        // Same binding rule as the item editor: validate the workspace+instance captured when the popup
        // opened, never the current selection; a missing record keeps the typed address in the form.
        var wsA = _current.Workspaces.FirstOrDefault(w => w.Id == _addressEdit.WorkspaceId);
        var m = wsA?.Items.FirstOrDefault(x => (x.Id ?? x.ItemId) == _addressEdit.InstanceId);
        if (m is null || !_current.Items.TryGetValue(m.ItemId, out var it)) { Status = "Not saved: the saved item record is missing; your address is still in the form"; return false; }
        var err = Catalog.ValidateInputTarget(it.Type, EditAddress);
        if (err != null) { FieldError("address", err == Catalog.WebsiteTargetError ? err : "הכתובת אינה תקינה. הזינו כתובת מלאה בהתאם לסוג הפריט.", "Not saved: " + err); return false; }
        // The change applies to the captured workspace only, for every type including sites: ReplaceTargetLocal re-points this one
        // instance at the new address; the same item in other workspaces and other instances here keep their own address.
        var ok = Apply(c =>
        {
            // Same inside-the-mutation validation as the item editor: Mutate reloads disk state, so the
            // precheck snapshot can be stale; a missing captured record aborts with a handled ArgumentException.
            var wsNow = c.Store.Workspaces.FirstOrDefault(w => w.Id == _addressEdit.WorkspaceId);
            var mNow = wsNow?.Items.FirstOrDefault(x => (x.Id ?? x.ItemId) == _addressEdit.InstanceId);
            if (mNow is null || !c.Store.Items.ContainsKey(mNow.ItemId)) throw new ArgumentException("the item no longer exists in the saved data; your address is still in the form");
            c.ReplaceTargetLocal(_addressEdit.WorkspaceId, _addressEdit.InstanceId, EditAddress);
        });
        if (ok) { Status = "Address saved"; AddressEditOpen = false; }
        return ok;
    }
    private void CloseAddressEdit()
    {
        if (AddressEditDirty) { MarkUnsavedHost(false, false, true); _unsavedExit.Continuation = () => AddressEditOpen = false; UnsavedOpen = true; return; }
        AddressEditOpen = false;
    }

}
