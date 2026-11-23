using System.Collections.ObjectModel;
using System.Windows.Input;
using StartApp.Core;

namespace StartApp.Presentation;

/// <summary>One visible instance row. Name/Description are the instance's own values with fallback to the catalog item's original name /
/// no description. Group is the display category (decided Nov 12: sites, folders, files, programs - the earlier files+programs combined group was superseded); order inside a group is undecided, so insertion order is kept.</summary>
public sealed record ItemRow(string InstanceId, string ItemId, string Name, string Target, string Type, string Group, string? Description = null, string? Program = null)
{
    /// <summary>Short name shown next to the program icon (icon + name display, settled Nov 14). The full Program path
    /// itself is untouched: it stays as the tooltip/help-text source and in the open-with popup.</summary>
    public string ProgramDisplayName => ProgramDisplayNames.Resolve(Program);
    /// <summary>A saved program item is itself the program, so its menu has no open-with entry.</summary>
    public bool IsProgram => Type == nameof(ItemType.Program);
}

/// <summary>UI state only. Persistence via IStoreRepository, OS actions via IItemLauncher. A store that failed to load puts the app in a
/// read-only state with a message; nothing is written until the user decides (no silent reset).</summary>
public sealed partial class MainViewModel : ObservableBase
{
    private readonly IStoreRepository _repo;
    private readonly IItemLauncher _launcher;
    private readonly IItemPreflight? _preflight;
    private readonly IOpenStateDetector _detector;
    private readonly IOpenItemCapturer _capturer;
    private Workspace? _selected; private string _status = ""; private bool _readOnly; private string _newName = "";

    public ObservableCollection<Workspace> Workspaces { get; } = new();
    public ObservableCollection<ItemRow> Items { get; } = new();
    private readonly List<RelayCommand> _commands = new();
    private RelayCommand Cmd(Action<object?> run, Func<object?, bool> can) { var c = new RelayCommand(run, can); _commands.Add(c); return c; }
    private void RaiseCommands() { foreach (var c in _commands) c.Raise(); Raise(nameof(OpenDecisionPending)); Raise(nameof(ShowOpenPanel)); Raise(nameof(ConfirmLabel)); Raise(nameof(DuplicatePending)); Raise(nameof(CanBackToOpeningSelection)); }
    public ICommand CreateWorkspaceCommand { get; }
    /// <summary>Opens the details screen for a workspace (from its card in the overview).</summary>
    public ICommand OpenDetailsCommand { get; }
    /// <summary>שגר from a card in the overview: opens that workspace's items without navigating.</summary>
    public ICommand OpenWorkspaceForCommand { get; }
    /// <summary>הוסף אייטם from the card's actions menu: opens the details screen and focuses the add form.</summary>
    public ICommand AddItemToCommand { get; }
    public ICommand BackCommand { get; }
    public ICommand SaveDetailsCommand { get; }
    /// <summary>Unsaved-changes exit dialog: שמור וצא.</summary>
    public ICommand SaveAndExitCommand { get; }
    /// <summary>Unsaved-changes exit dialog: צא בלי לשמור.</summary>
    public ICommand ExitWithoutSavingCommand { get; }
    /// <summary>Unsaved-changes exit dialog: X, back to editing with nothing lost.</summary>
    public ICommand CloseUnsavedCommand { get; }
    /// <summary>The הוסף אייטם button in the empty state; the view focuses the add form.</summary>
    public ICommand FocusAddCommand { get; }
    public event Action? FocusAddRequested;
    /// <summary>The empty-workspace dialog's button that leads to the details screen (to add items).</summary>
    public ICommand GoToDetailsCommand { get; }
    public ICommand RemoveItemCommand { get; }
    /// <summary>לחיצה על שם האייטם בשורה פותחת אותו (spec שורה של אייטם).</summary>
    public ICommand OpenItemCommand { get; }
    /// <summary>העתק כתובת/קישור/נתיב from the row actions: direct copy, no popup. The view puts the target on the clipboard.</summary>
    public ICommand CopyAddressCommand { get; }
    public event Action<string>? CopyAddressRequested;
    /// <summary>עריכה from the row: opens the item edit form (טופס עריכת אייטם) for that one instance.</summary>
    public ICommand EditItemCommand { get; }
    public ICommand SaveItemEditCommand { get; }
    /// <summary>עריכת כתובת from the row actions: opens the dedicated address popup (פופאפ עריכת כתובת).</summary>
    public ICommand EditAddressCommand { get; }
    public ICommand SaveAddressCommand { get; }
    /// <summary>The copy button inside the address popup: copies the full value as currently shown.</summary>
    public ICommand CopyFullAddressCommand { get; }
    /// <summary>The popup's X: closes silently when untouched, otherwise asks via the unsaved-exit dialog.</summary>
    public ICommand CloseAddressEditCommand { get; }
    /// <summary>פתח באמצעות from the row actions: opens the open-with popup (פופאפ פתח באמצעות).</summary>
    public ICommand OpenWithCommand { get; }
    /// <summary>פתח in the open-with popup: opens with the chosen program now and saves it when הגדר כברירת מחדל is checked.</summary>
    public ICommand OpenWithGoCommand { get; }
    public ICommand CloseOpenWithCommand { get; }
    /// <summary>עצב כרטיס from the card actions menu: opens the design popup (פופאפ עיצוב כרטיס) for that workspace.</summary>
    public ICommand DesignCardCommand { get; }
    /// <summary>ערוך קטגוריה from the card actions menu: opens the category popup (פופאפ עריכת קטגוריה) for that workspace.</summary>
    public ICommand EditCategoryPopupCommand { get; }
    /// <summary>העלאת תמונה in the design popup: the view opens a file picker and sets DesignImagePath.</summary>
    public ICommand PickImageCommand { get; }
    public event Action? PickImageRequested;
    /// <summary>Create-form design (optional image/color at creation, spec Nov 12 morning): the view opens the same file
    /// dialog as עצב כרטיס and assigns NewWorkspaceImagePath.</summary>
    public ICommand PickCreateImageCommand { get; }
    public event Action? PickCreateImageRequested;
    public ICommand RemoveCreateImageCommand { get; }
    public ICommand ApplyCreatePaletteColorCommand { get; }
    /// <summary>הסרת תמונה in the design popup: back to the color-as-background look (takes effect on save).</summary>
    public ICommand RemoveImageCommand { get; }
    /// <summary>A preset swatch in the design popup: applies that color to the preview (saved only via Save).</summary>
    public ICommand ApplyPaletteColorCommand { get; }
    public ICommand SaveDesignCommand { get; }
    /// <summary>The design popup's X: closes silently when untouched, otherwise asks via the unsaved-exit dialog.</summary>
    public ICommand CloseDesignCommand { get; }
    /// <summary>Selecting a category in the popup assigns it to the workspace immediately.</summary>
    public ICommand PickCategoryCommand { get; }
    public ICommand AddCategoryCommand { get; }
    /// <summary>Starts renaming a custom category (built-ins never offer this).</summary>
    public ICommand BeginRenameCategoryCommand { get; }
    public ICommand SaveRenameCategoryCommand { get; }
    public ICommand CancelRenameCategoryCommand { get; }
    /// <summary>Deletes a custom category; with assigned workspaces this opens the warning first.</summary>
    public ICommand DeleteCategoryCommand { get; }
    public ICommand CloseCategoryPopupCommand { get; }
    /// <summary>With several assigned workspaces, this button shows the list of them inside the warning.</summary>
    public ICommand ShowCategoryDeleteListCommand { get; }
    public ICommand ConfirmCategoryDeleteCommand { get; }
    public ICommand CancelCategoryDeleteCommand { get; }
    /// <summary>בחר מסביבות קיימות in the add form: opens the existing-resource picker (settled Nov 11).</summary>
    public ICommand OpenPickerCommand { get; }
    /// <summary>One of the four type buttons in the picker (תוכנות / אתרים / קבצים / תיקיות).</summary>
    public ICommand PickTypeCommand { get; }
    /// <summary>Choosing a resource returns to the add form with its address filled; local name/description stay the user's to set before adding.</summary>
    public ICommand PickResourceCommand { get; }
    public ICommand ClosePickerCommand { get; }
    /// <summary>הזז למעלה / הזז למטה: reorder this instance inside its display group.</summary>
    public ICommand MoveItemUpCommand { get; }
    public ICommand MoveItemDownCommand { get; }
    /// <summary>שיתוף לסביבה אחרת is shown in the actions menu only when another workspace exists (the settled display condition);
    /// its popup is still undefined (הפרטים טרם הוגדרו), so the entry stays disabled.</summary>
    public bool HasOtherWorkspaces => Workspaces.Count > 1;
    /// <summary>ביטול in the item edit form: closes immediately when untouched, otherwise asks via the unsaved-exit dialog.</summary>
    public ICommand CancelItemEditCommand { get; }
    public ICommand OpenWorkspaceCommand { get; }
    public ICommand AddItemCommand { get; }
    /// <summary>Duplicate-add confirmation: visible while an add waits for the user to approve a second instance of the same target.</summary>
    public bool DuplicatePending => _pendingDup;
    private bool _pendingDup;
    /// <summary>Text shown next to the duplicate confirm/cancel buttons.</summary>
    public string DuplicateMessage { get => _dupMessage; private set => Set(ref _dupMessage, value); }
    private string _dupMessage = "";
    public ICommand ConfirmDuplicateCommand { get; }
    public ICommand CancelDuplicateCommand { get; }
    /// <summary>Set instead of opening when the workspace has no items (the spec's דיאלוג סביבת עבודה ריקה).</summary>
    public bool EmptyWorkspaceOpen { get => _emptyOpen; private set { Set(ref _emptyOpen, value); RaiseCommands(); } }
    private bool _emptyOpen;
    public ICommand CloseEmptyWorkspaceCommand { get; }
    /// <summary>The shared unsaved-changes exit dialog (דיאלוג יציאה עם שינויים שלא נשמרו).</summary>
    public bool UnsavedOpen { get => _unsavedOpen; private set { Set(ref _unsavedOpen, value); RaiseCommands(); } }
    private bool _unsavedOpen;
    private Action _exitAction = () => { };
    /// <summary>Window-close guard: false means the close was cancelled and the unsaved-changes dialog opened instead.</summary>
    public bool RequestAppClose()
    {
        if (CaptureDirty) { MarkUnsavedHost(false, false, false, false, true); _exitAction = () => AppCloseApproved?.Invoke(); UnsavedOpen = true; return false; }
        if (DetailsDirty) { MarkUnsavedHost(false, false, false); _exitAction = () => AppCloseApproved?.Invoke(); UnsavedOpen = true; return false; }
        if (CreateDirty) { MarkUnsavedHost(true, false, false); _exitAction = () => AppCloseApproved?.Invoke(); UnsavedOpen = true; return false; }
        if (ItemEditDirty) { MarkUnsavedHost(false, true, false); _exitAction = () => AppCloseApproved?.Invoke(); UnsavedOpen = true; return false; }
        if (AddressEditDirty) { MarkUnsavedHost(false, false, true); _exitAction = () => AppCloseApproved?.Invoke(); UnsavedOpen = true; return false; }
        if (DesignDirty) { MarkUnsavedHost(false, false, false, true); _exitAction = () => AppCloseApproved?.Invoke(); UnsavedOpen = true; return false; }
        return true;
    }
    private void SaveAndExit()
    {
        // Save EVERY open dirty form, innermost first, so "save and exit" never approves the close while
        // another pending edit is silently dropped (PR19 review: details + inline item edit together saved
        // only the details and lost the item change). A failed save keeps the dialog open for fixing.
        if (ItemEditDirty) { if (!SaveItemEdit()) return; }
        if (AddressEditDirty) { if (!SaveAddress()) return; }
        if (UnsavedForCapture && CaptureDirty) { if (!CaptureGuardSave()) return; }
        if (UnsavedForDesign && DesignDirty) { if (!SaveDesign()) return; }
        if (UnsavedForCreate && CreateDirty)
        {
            if (string.IsNullOrWhiteSpace(NewName)) { UnsavedOpen = false; _exitAction = () => { }; Status = "Not saved: workspace name is required"; return; }
            Create();
            if (CreateDirty) { UnsavedOpen = false; _exitAction = () => { }; return; } // creation failed; keep editing
        }
        if (DetailsDirty) { if (!SaveDetails()) return; }
        UnsavedOpen = false; var a = _exitAction; _exitAction = () => { }; a();
    }
    /// <summary>Fired when the user chose שמור וצא or צא בלי לשמור for a window close.</summary>
    public event Action? AppCloseApproved;
    public IReadOnlyList<ItemType> ItemTypes { get; } = new[] { ItemType.Link, ItemType.Program, ItemType.File, ItemType.Folder };

    public MainViewModel(IStoreRepository repo, IItemLauncher launcher, IItemPreflight? preflight = null, IOpenStateDetector? detector = null, IOpenItemCapturer? capturer = null)
    {
        _repo = repo; _launcher = launcher; _preflight = preflight; _detector = detector ?? new NoOpenStateDetection(); _capturer = capturer ?? new NoCapture();
        InitCaptureCommands();
        InitGlobalEnvCommands();
        InitAllItemsCommands();
        InitOpeningCommands();
        CreateWorkspaceCommand = Cmd(_ => Create(), _ => !ReadOnly && !string.IsNullOrWhiteSpace(NewName));
        // No workspace deletion: the user explicitly rejected it from the card actions (Nov 11); the menu is
        // הצג / שגר / הוסף אייטם / עצב כרטיס / ערוך קטגוריה only. Catalog.DeleteWorkspace stays unexposed.
        OpenDetailsCommand = Cmd(p => OpenDetails((Workspace)p!), p => p is Workspace);
        // owner decision 22.11 17:15: deploy is never enabled for an empty workspace, in any view.
        OpenWorkspaceForCommand = Cmd(p => { Selected = (Workspace)p!; Open(); }, p => p is Workspace w && w.Items.Count > 0 && !IsChecking && !IsLaunching && !SelectionPending && !OpenDecisionPending);
        AddItemToCommand = Cmd(p => { OpenDetails((Workspace)p!); FocusAddRequested?.Invoke(); }, p => p is Workspace);
        BackCommand = Cmd(_ => Back(), _ => InDetails);
        SaveDetailsCommand = Cmd(_ => SaveDetails(), _ => InDetails && DetailsDirty && !ReadOnly);
        SaveAndExitCommand = Cmd(_ => SaveAndExit(), _ => UnsavedOpen);
        ExitWithoutSavingCommand = Cmd(_ => { UnsavedOpen = false; if (UnsavedForCapture) { DiscardCaptureChanges(); } else if (UnsavedForItemEdit) { ItemEditOpen = false; } else if (UnsavedForAddress) { AddressEditOpen = false; } else if (UnsavedForCreate) { ResetCreateForm(); } else if (UnsavedForDesign) { DesignEditOpen = false; } else { _detailsDirtyBypass = true; LoadDetails(); _detailsDirtyBypass = false; } var a = _exitAction; _exitAction = () => { }; a(); }, _ => UnsavedOpen);
        CloseUnsavedCommand = Cmd(_ => { UnsavedOpen = false; _exitAction = () => { }; }, _ => UnsavedOpen);
        FocusAddCommand = Cmd(_ => FocusAddRequested?.Invoke(), _ => InDetails);
        OpenItemCommand = Cmd(p => OpenOne((ItemRow)p!), p => !ReadOnly && Selected != null && p is ItemRow && !IsChecking && !IsLaunching && !SelectionPending && !OpenDecisionPending);
        CopyAddressCommand = Cmd(p => { CopyAddressRequested?.Invoke(((ItemRow)p!).Target); Status = "Address copied"; }, p => p is ItemRow);
        EditItemCommand = Cmd(p => BeginItemEdit((ItemRow)p!), p => !ReadOnly && Selected != null && p is ItemRow && !ItemEditDirty && !AddressEditDirty);
        SaveItemEditCommand = Cmd(_ => SaveItemEdit(), _ => ItemEditOpen && ItemEditDirty && !ReadOnly);
        CancelItemEditCommand = Cmd(_ => CancelItemEdit(), _ => ItemEditOpen);
        EditAddressCommand = Cmd(p => BeginItemEdit((ItemRow)p!), p => !ReadOnly && Selected != null && p is ItemRow && !ItemEditDirty && !AddressEditDirty);
        SaveAddressCommand = Cmd(_ => SaveAddress(), _ => AddressEditOpen && AddressEditDirty && !ReadOnly);
        CopyFullAddressCommand = Cmd(_ => { CopyAddressRequested?.Invoke(EditAddress); Status = "Address copied"; }, _ => AddressEditOpen);
        CloseAddressEditCommand = Cmd(_ => CloseAddressEdit(), _ => AddressEditOpen);
        OpenWithCommand = Cmd(p => BeginOpenWith((ItemRow)p!), p => !ReadOnly && Selected != null && p is ItemRow);
        OpenWithGoCommand = Cmd(_ => OpenWithGo(), _ => OpenWithOpen && !ReadOnly && !IsChecking && !IsLaunching && !SelectionPending && !OpenDecisionPending);
        CloseOpenWithCommand = Cmd(_ => CloseOpenWith(), _ => OpenWithOpen);
        MoveItemUpCommand = Cmd(p => Apply(c => c.MoveInstance(Selected!.Id, ((ItemRow)p!).InstanceId, -1)), p => !ReadOnly && Selected != null && p is ItemRow);
        MoveItemDownCommand = Cmd(p => Apply(c => c.MoveInstance(Selected!.Id, ((ItemRow)p!).InstanceId, 1)), p => !ReadOnly && Selected != null && p is ItemRow);
        DesignCardCommand = Cmd(p => BeginDesign((Workspace)p!), p => p is Workspace && !ReadOnly);
        PickImageCommand = Cmd(_ => PickImageRequested?.Invoke(), _ => DesignEditOpen && !ReadOnly);
        PickCreateImageCommand = Cmd(_ => PickCreateImageRequested?.Invoke(), _ => !ReadOnly);
        RemoveCreateImageCommand = Cmd(_ => NewWorkspaceImagePath = "", _ => NewWorkspaceImagePath.Length > 0 && !ReadOnly);
        ApplyCreatePaletteColorCommand = Cmd(p => NewWorkspaceColor = (string)p!, p => p is string && !ReadOnly);
        RemoveImageCommand = Cmd(_ => DesignImagePath = "", _ => DesignEditOpen && DesignImagePath.Length > 0);
        ApplyPaletteColorCommand = Cmd(p => DesignColor = (string)p!, p => p is string && DesignEditOpen && !ReadOnly);
        SaveDesignCommand = Cmd(_ => SaveDesign(), _ => DesignEditOpen && DesignDirty && !ReadOnly);
        CloseDesignCommand = Cmd(_ => CloseDesign(), _ => DesignEditOpen);
        EditCategoryPopupCommand = Cmd(p => BeginCategoryEdit((Workspace)p!), p => p is Workspace && !ReadOnly);
        OpenPickerCommand = Cmd(_ => OpenPicker(), _ => InDetails && Selected != null && !ReadOnly);
        PickTypeCommand = Cmd(p => { PickerType = (ItemType)p!; RefreshPicker(); }, p => p is ItemType);
        PickResourceCommand = Cmd(p => PickResource((PickerRow)p!), p => p is PickerRow);
        ClosePickerCommand = Cmd(_ => PickerOpen = false, _ => PickerOpen);
        PickCategoryCommand = Cmd(p => PickCategory((CategoryRow)p!), p => p is CategoryRow && !ReadOnly);
        AddCategoryCommand = Cmd(_ => AddCategory(), _ => CategoryEditOpen && !ReadOnly && NewCategoryName.Length > 0);
        BeginRenameCategoryCommand = Cmd(p => { _renameOld = ((CategoryRow)p!).Name; RenameCategoryText = _renameOld; RenameCategoryOpen = true; }, p => p is CategoryRow { IsBuiltIn: false } && !ReadOnly);
        SaveRenameCategoryCommand = Cmd(_ => SaveRenameCategory(), _ => RenameCategoryOpen && !ReadOnly && RenameCategoryText.Length > 0);
        CancelRenameCategoryCommand = Cmd(_ => RenameCategoryOpen = false, _ => RenameCategoryOpen);
        DeleteCategoryCommand = Cmd(p => BeginDeleteCategory((CategoryRow)p!), p => p is CategoryRow { IsBuiltIn: false } && !ReadOnly);
        CloseCategoryPopupCommand = Cmd(_ => CloseCategoryPopup(), _ => CategoryEditOpen);
        ShowCategoryDeleteListCommand = Cmd(_ => CategoryDeleteListShown = true, _ => CategoryDeletePending && !CategoryDeleteListShown);
        ConfirmCategoryDeleteCommand = Cmd(_ => ConfirmCategoryDelete(), _ => CategoryDeletePending && !ReadOnly);
        CancelCategoryDeleteCommand = Cmd(_ => CancelCategoryDelete("Category deletion cancelled, nothing was changed"), _ => CategoryDeletePending);
        RemoveItemCommand = Cmd(p =>
        {
            var row = (ItemRow)p!;
            // Deleting the very row whose inline editor is open: a dirty editor wins - the delete is refused
            // with a clear status instead of stranding the edit on a missing instance (PR19 review). A clean
            // editor just closes before the delete.
            if (ItemEditOpen && row.InstanceId == _editItemId)
            {
                if (ItemEditDirty) { Status = "Not deleted: save or cancel the open edit of this item first"; return; }
                ItemEditOpen = false;
            }
            if (AddressEditOpen && row.InstanceId == _addressInstId)
            {
                if (AddressEditDirty) { Status = "Not deleted: save or cancel the open address edit of this item first"; return; }
                AddressEditOpen = false;
            }
            Apply(c => c.RemoveInstance(Selected!.Id, row.InstanceId));
        }, p => !ReadOnly && Selected != null && p is ItemRow);
        AddItemCommand = Cmd(_ => AddItem(), _ => !ReadOnly && Selected != null && NewTarget.Length > 0);
        ConfirmDuplicateCommand = Cmd(_ => { _pendingDup = false; DuplicateMessage = ""; AddItem(force: true); }, _ => _pendingDup);
        CancelDuplicateCommand = Cmd(_ => ClearDup("Add cancelled, nothing was added"), _ => _pendingDup);
        CloseEmptyWorkspaceCommand = Cmd(_ => EmptyWorkspaceOpen = false, _ => _emptyOpen);
        GoToDetailsCommand = Cmd(_ => { EmptyWorkspaceOpen = false; if (Selected != null) { LoadDetails(); InDetails = true; } }, _ => _emptyOpen && Selected != null);
        OpenWorkspaceCommand = Cmd(_ => Open(), _ => Selected is { Items.Count: > 0 } && !IsChecking && !IsLaunching && !SelectionPending && !OpenDecisionPending);
        ConfirmOpenAnywayCommand = Cmd(_ => ConfirmOpen(), _ => _pendingOpen != null);
        CancelOpenCommand = Cmd(_ => { ClearPending(); }, _ => SelectionPending || _pendingOpen != null || IsChecking || IsLaunching);
        Reload();
    }


    public Workspace? Selected { get => _selected; set { ClearPending(); ClearDup(); EmptyWorkspaceOpen = false; if (!ItemEditDirty) ItemEditOpen = false; if (!AddressEditDirty) AddressEditOpen = false; OpenWithOpen = false; Set(ref _selected, value); Raise(nameof(SelectedIsDefault)); Raise(nameof(DefaultToggleLabel)); Raise(nameof(EditingItemInstanceId)); if (InDetails && !DetailsDirty) LoadDetails(); RefreshItems(); Raise(nameof(ShowEmptyItemsMessage)); Raise(nameof(CreatedAtText)); RaiseCommands(); } }
    // An ordinary refresh (Apply -> Show re-selects the same workspace) must not silently drop an open
    // unsaved item/address edit (PR19 review: edits disappeared on unrelated mutations). A dirty form stays
    // open with its fields. Safety does not rest on the selection staying put: saves validate the
    // workspace+instance captured when the form opened, and dirty forms block workspace switches and row
    // deletion, so a preserved form can neither crash nor silently retarget.
    public string Status { get => _status; private set { Set(ref _status, value); PresentLegacyStatus(value); } }
    public bool ReadOnly { get => _readOnly; private set { Set(ref _readOnly, value); Raise(nameof(SaveBlocked)); RaiseCommands(); } }
    public ItemType NewItemType { get => _newType; set { Set(ref _newType, value); Raise(nameof(NewItemTypeIndex)); ClearDup(); } }
    public string NewTarget { get => _newTarget; set { Set(ref _newTarget, value);
        if (Uri.TryCreate(value, UriKind.Absolute, out var pasted) && (pasted.Scheme == Uri.UriSchemeHttp || pasted.Scheme == Uri.UriSchemeHttps)) NewItemType = ItemType.Link;
        ClearDup(); RaiseCommands(); } }
    public static IReadOnlyList<string> ItemTypeLabels { get; } = new[] { "אתר", "קובץ", "תיקייה", "תוכנה" };
    public int NewItemTypeIndex { get => (int)NewItemType; set => NewItemType = (ItemType)value; }
    private ItemType _newType = ItemType.File; private string _newTarget = "";
    /// <summary>Optional local name for the instance in this workspace. Empty means the original (derived) name.</summary>
    public string NewItemName { get => _newItemName; set { Set(ref _newItemName, value ?? ""); ClearDup(); } }
    private string _newItemName = "";
    /// <summary>Optional description typed in the add form. Empty means none.</summary>
    public string NewDescription { get => _newDescription; set { Set(ref _newDescription, value ?? ""); ClearDup(); } }
    private string _newDescription = "";

    private void ClearDup(string? status = null) { if (!_pendingDup) return; _pendingDup = false; DuplicateMessage = ""; if (status != null) Status = status; Raise(nameof(DuplicatePending)); RaiseCommands(); }
    public string NewName { get => _newName; set { Set(ref _newName, value); Raise(nameof(CreateDirty)); Raise(nameof(CreatePreview)); RaiseCommands(); } }
    /// <summary>Optional workspace description in the create form.</summary>
    public string NewWorkspaceDescription { get => _newWsDesc; set { Set(ref _newWsDesc, value ?? ""); Raise(nameof(CreateDirty)); } }
    private string _newWsDesc = "";
    /// <summary>Category chosen in the create form from the managed list (קטגוריה: כללי מסומנת כברירת מחדל, ואפשר לבחור אחרת).</summary>
    public string NewWorkspaceCategory { get => _newWsCat; set { Set(ref _newWsCat, value ?? Workspace.DefaultCategory); Raise(nameof(CreateDirty)); Raise(nameof(CreatePreview)); } }
    private string _newWsCat = Workspace.DefaultCategory;
    /// <summary>Optional card color chosen at creation (#RRGGBB, palette or typed; empty = the app's default look). The same
    /// design rule as עצב כרטיס applies from the first save: with an image the color is the frame, without one the background.</summary>
    public string NewWorkspaceColor { get => _newWsColor; set { Set(ref _newWsColor, value ?? ""); Raise(nameof(CreateDirty)); Raise(nameof(CreatePreview)); RaiseCommands(); } }
    private string _newWsColor = "";
    /// <summary>Optional card background image path chosen at creation; empty = none.</summary>
    public string NewWorkspaceImagePath { get => _newWsImage; set { Set(ref _newWsImage, value ?? ""); Raise(nameof(CreateDirty)); Raise(nameof(CreatePreview)); RaiseCommands(); } }
    private string _newWsImage = "";
    /// <summary>Clears every create-form field after a successful create or a discard (יציאה בלי לשמור).</summary>
    private void ResetCreateForm() { NewName = ""; NewWorkspaceDescription = ""; NewWorkspaceCategory = Workspace.DefaultCategory; NewWorkspaceColor = ""; NewWorkspaceImagePath = ""; }
    /// <summary>The create form holds unsaved input; closing the app then goes through the same unsaved-exit
    /// dialog (spec: דיאלוג יציאה עם שינויים שלא נשמרו applies to טופס יצירת סביבה too).</summary>
    public bool CreateDirty => NewName.Length > 0 || NewWorkspaceDescription.Length > 0 || NewWorkspaceCategory != Workspace.DefaultCategory || NewWorkspaceColor.Length > 0 || NewWorkspaceImagePath.Length > 0;
    /// <summary>True while the open unsaved-exit dialog guards the create form rather than the details screen.</summary>
    public bool UnsavedForCreate { get => _unsavedForCreate; private set => Set(ref _unsavedForCreate, value); }
    private bool _unsavedForCreate;
    /// <summary>True while the open unsaved-exit dialog guards the item edit form (טופס עריכת אייטם).</summary>
    public bool UnsavedForItemEdit { get => _unsavedForItemEdit; private set => Set(ref _unsavedForItemEdit, value); }
    private bool _unsavedForItemEdit;
    /// <summary>True while the open unsaved-exit dialog guards the address popup (פופאפ עריכת כתובת).</summary>
    public bool UnsavedForAddress { get => _unsavedForAddress; private set => Set(ref _unsavedForAddress, value); }
    private bool _unsavedForAddress;
    /// <summary>True while the open unsaved-exit dialog guards the card design popup (פופאפ עיצוב כרטיס).</summary>
    public bool UnsavedForDesign { get => _unsavedForDesign; private set => Set(ref _unsavedForDesign, value); }
    private bool _unsavedForDesign;
    public string UnsavedContextLabel => UnsavedForCreate ? "יצירת סביבה · יציאה" : UnsavedForItemEdit || UnsavedForAddress ? "עריכת אייטם · יציאה" : UnsavedForDesign ? "עיצוב כרטיס · יציאה" : UnsavedForCapture ? "לכידה · יציאה" : "פרטי הסביבה · יציאה";
    public string UnsavedContextMessage => UnsavedForCreate ? "בוצעו שינויים בסביבה החדשה שעדיין לא נשמרו." : UnsavedForItemEdit || UnsavedForAddress ? "בוצעו שינויים באייטם שעדיין לא נשמרו." : UnsavedForDesign ? "בוצעו שינויים בעיצוב הכרטיס שעדיין לא נשמרו." : UnsavedForCapture ? "בוצעו שינויים בלכידה שעדיין לא נשמרו." : "בוצעו שינויים בפרטי הסביבה שעדיין לא נשמרו.";
    private void MarkUnsavedHost(bool forCreate, bool forItemEdit, bool forAddress, bool forDesign = false, bool forCapture = false)
    {
        UnsavedForCapture = forCapture; UnsavedForCreate = forCreate; UnsavedForItemEdit = forItemEdit; UnsavedForAddress = forAddress; UnsavedForDesign = forDesign;
        Raise(nameof(UnsavedContextLabel)); Raise(nameof(UnsavedContextMessage));
    }

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

    // ---- open-with popup (פופאפ פתח באמצעות): browser for a site, program for a file; per-instance הגדר כברירת מחדל ----
    public bool OpenWithOpen { get => _openWithOpen; private set { Set(ref _openWithOpen, value); RaiseCommands(); } }
    private bool _openWithOpen;
    private ItemRow? _openWithRow;
    private StagedRow? _openWithStaged;
    /// <summary>Which program the popup asks for: a browser for a site, a program for a file/folder.</summary>
    public string OpenWithLabel => _openWithRow?.Type == nameof(ItemType.Link) || _openWithStaged?.Type == ItemType.Link ? "Browser (full path to its exe); empty = Windows default" : "Program (full path to its exe); empty = Windows default";
    public string OpenWithProgram { get => _openWithProgram; set { Set(ref _openWithProgram, value ?? ""); RaiseCommands(); } }
    private string _openWithProgram = "";
    /// <summary>הגדר כברירת מחדל: save the choice for THIS instance only (never a Windows default, never other workspaces).</summary>
    public bool OpenWithSetDefault { get => _openWithSetDefault; set => Set(ref _openWithSetDefault, value); }
    private bool _openWithSetDefault;
    private void BeginOpenWith(ItemRow row)
    {
        if (row.Type == nameof(ItemType.Program)) { Status = "Open with does not apply to a program item (it is the program)"; return; }
        var m = Selected!.Items.FirstOrDefault(x => (x.Id ?? x.ItemId) == row.InstanceId);
        _openWithRow = row; _openWithStaged = null;
        OpenWithProgram = m?.Launch?.Browser ?? m?.Launch?.OpenWith ?? "";
        OpenWithSetDefault = m?.Launch is not null;
        OpenWithOpen = true; Raise(nameof(OpenWithLabel));
    }
    private void OpenWithGo()
    {
        if (_openWithStaged is { } sg)
        {
            var prog2 = OpenWithProgram.Trim();
            LaunchConfig? cfg2 = prog2.Length == 0 ? null : sg.Type == ItemType.Link ? new LaunchConfig(Browser: prog2) : new LaunchConfig(OpenWith: prog2);
            if (OpenWithSetDefault) sg.OpenWith = prog2; // a staged default stays staged until שמור
            CloseOpenWith();
            var stagedItem = new Item("staged", sg.Type, sg.Name, sg.Target, string.IsNullOrWhiteSpace(sg.Description) ? null : sg.Description);
            OpenOneCore(sg.Name, stagedItem, cfg2);
            return;
        }
        if (_openWithRow is null) return;
        var row = _openWithRow; var prog = OpenWithProgram.Trim();
        LaunchConfig? cfg = prog.Length == 0 ? null : row.Type == nameof(ItemType.Link) ? new LaunchConfig(Browser: prog) : new LaunchConfig(OpenWith: prog);
        if (OpenWithSetDefault && !Apply(c => c.SetLaunch(Selected!.Id, row.InstanceId, cfg))) return;
        OpenWithOpen = false;
        var m = Selected!.Items.FirstOrDefault(x => (x.Id ?? x.ItemId) == row.InstanceId);
        if (m is null || !_current.Items.TryGetValue(m.ItemId, out var it)) { Status = row.Name + ": not opened (the saved item record is missing)"; return; }
        // This open uses the choice just made (empty = the Windows default for this open), whether or not it was saved.
        OpenOneCore(row.Name, it, cfg);
    }

    // ---- card design popup (פופאפ עיצוב כרטיס, settled Nov 11): per-workspace color and background image, with a live
    // preview in the view, a save button on any change, and the shared unsaved-exit dialog on X ----
    public bool DesignEditOpen { get => _designEditOpen; private set { Set(ref _designEditOpen, value); RaiseCommands(); } }
    private bool _designEditOpen;
    private string _designWsId = "";
    /// <summary>#RRGGBB, empty = the default look. The preview and the card show this value as the frame (with an image) or the background (without).</summary>
    public string DesignColor { get => _designColor; set { Set(ref _designColor, value ?? ""); Raise(nameof(DesignDirty)); RaiseCommands(); } }
    private string _designColor = "";
    /// <summary>Path of the uploaded background image; empty = none. Stored as a path reference (copying the file is an open storage decision).</summary>
    public string DesignImagePath { get => _designImagePath; set { Set(ref _designImagePath, value ?? ""); Raise(nameof(DesignDirty)); RaiseCommands(); } }
    private string _designImagePath = "";
    private string _origColor = "", _origImage = "";
    /// <summary>Any change in the design popup shows its Save button (same rule as the other edit screens).</summary>
    public bool DesignDirty => DesignEditOpen && (DesignColor != _origColor || DesignImagePath != _origImage);
    /// <summary>Provisional preset palette: the exact color set was never settled, so the popup offers these and a free #RRGGBB entry.</summary>
    public IReadOnlyList<string> DesignPalette { get; } = new[] { "#FFFFFF", "#FDE68A", "#BFDBFE", "#BBF7D0", "#FECACA", "#E9D5FF", "#E5E7EB" };
    private void BeginDesign(Workspace w)
    {
        var fresh = _current.Workspaces.FirstOrDefault(x => x.Id == w.Id) ?? w; // the caller may hold a pre-reload reference
        _designWsId = fresh.Id; _origColor = fresh.Color ?? ""; _origImage = fresh.ImagePath ?? "";
        DesignColor = _origColor; DesignImagePath = _origImage;
        DesignEditOpen = true;
    }
    private bool SaveDesign()
    {
        var color = DesignColor.Trim(); var image = DesignImagePath.Trim();
        if (color.Length > 0 && Catalog.ValidateColor(color) is { } ce) { FieldError("design", "הצבע אינו תקין. הזינו צבע בצורה #RRGGBB.", "Not saved: " + ce); return false; }
        var ok = Apply(c => c.SetCardDesign(_designWsId, color.Length == 0 ? null : color, image.Length == 0 ? null : image));
        if (ok) { Status = "Card design saved"; DesignEditOpen = false; }
        return ok;
    }
    private void CloseDesign()
    {
        if (DesignDirty) { MarkUnsavedHost(false, false, false, true); _exitAction = () => DesignEditOpen = false; UnsavedOpen = true; return; }
        DesignEditOpen = false;
    }

    // ---- category popup (פופאפ עריכת קטגוריה, settled Nov 11): assign from the managed list; custom add/rename/delete with
    // the settled deletion warning (one workspace shown directly; several behind a list button; confirm moves them to כללי) ----
    /// <summary>One row in the category popup: built-ins expose no edit/delete (their commands guard on IsBuiltIn).</summary>
    public sealed record CategoryRow(string Name, bool IsBuiltIn);
    public ObservableCollection<CategoryRow> CategoryRows { get; } = new();
    /// <summary>The flat list for the create form and the details screen: built-ins first, then custom.</summary>
    public ObservableCollection<string> CategoryChoices { get; } = new();
    public bool CategoryEditOpen { get => _categoryEditOpen; private set { Set(ref _categoryEditOpen, value); RaiseCommands(); } }
    private bool _categoryEditOpen;
    private string _categoryWsId = "";
    /// <summary>The category the workspace in the popup currently has (displayed as the selected one).</summary>
    public string CurrentCategoryName { get => _currentCategoryName; private set => Set(ref _currentCategoryName, value); }
    private string _currentCategoryName = Workspace.DefaultCategory;
    public string NewCategoryName { get => _newCategoryName; set { Set(ref _newCategoryName, value ?? ""); RaiseCommands(); } }
    private string _newCategoryName = "";
    public bool RenameCategoryOpen { get => _renameCategoryOpen; private set => Set(ref _renameCategoryOpen, value); }
    private bool _renameCategoryOpen;
    private string _renameOld = "";
    public string RenameCategoryText { get => _renameCategoryText; set { Set(ref _renameCategoryText, value ?? ""); RaiseCommands(); } }
    private string _renameCategoryText = "";
    /// <summary>True while the delete-category warning waits for the user's answer.</summary>
    public bool CategoryDeletePending { get => _categoryDeletePending; private set { Set(ref _categoryDeletePending, value); RaiseCommands(); } }
    private bool _categoryDeletePending;
    public string CategoryDeleteMessage { get => _categoryDeleteMessage; private set => Set(ref _categoryDeleteMessage, value); }
    private string _categoryDeleteMessage = "";
    /// <summary>Names of the workspaces assigned to the category being deleted.</summary>
    public ObservableCollection<string> CategoryDeleteWorkspaces { get; } = new();
    /// <summary>With one assigned workspace the warning shows it directly; with several, this becomes true via the list button.</summary>
    public bool CategoryDeleteListShown { get => _categoryDeleteListShown; private set { Set(ref _categoryDeleteListShown, value); RaiseCommands(); } }
    private bool _categoryDeleteListShown;
    private string _deleteCategoryName = "";
    private void BeginCategoryEdit(Workspace w)
    {
        _categoryWsId = w.Id; CategoryEditOpen = true; RenameCategoryOpen = false; CancelCategoryDelete();
        RefreshCategories();
    }
    private void RefreshCategories()
    {
        var cats = new Catalog(_current).AllCategories();
        CategoryRows.Clear(); foreach (var c in cats) CategoryRows.Add(new CategoryRow(c, Catalog.IsBuiltInCategory(c)));
        // Rebuilding CategoryChoices with Clear() raises Reset, which wipes each bound ComboBox selection, and the
        // TwoWay SelectedItem binding pushes null back into the view model; restoring while the list is momentarily
        // empty leaves the combo visibly blank (seen on the create form). Rebuild diff-wise instead so an unchanged
        // selection never gets a Reset, and still capture/restore for the removed-item (rename/delete) case.
        var keepNew = _newWsCat; var keepEdit = _editCategory;
        for (var i = CategoryChoices.Count - 1; i >= 0; i--) if (!cats.Contains(CategoryChoices[i])) CategoryChoices.RemoveAt(i);
        foreach (var c in cats) if (!CategoryChoices.Contains(c)) CategoryChoices.Add(c);
        _newWsCat = keepNew; Raise(nameof(NewWorkspaceCategory)); Raise(nameof(CreateDirty));
        _editCategory = keepEdit; Raise(nameof(EditCategory)); RaiseDirty();
        var w = _current.Workspaces.FirstOrDefault(x => x.Id == _categoryWsId);
        CurrentCategoryName = w?.EffectiveCategory ?? Workspace.DefaultCategory;
    }
    private void PickCategory(CategoryRow row)
    {
        if (Apply(c => c.SetCategory(_categoryWsId, row.Name))) Status = "Category saved";
    }
    private void AddCategory()
    {
        if (Catalog.ValidateCategoryName(NewCategoryName) is { } categoryError) { FieldError("category", "שם הקטגוריה אינו תקין. הזינו שם באורך עד 100 תווים ללא תווי בקרה.", "Not saved: " + categoryError); return; }
        if (Apply(c => c.AddCustomCategory(NewCategoryName))) { NewCategoryName = ""; Status = "Category added"; }
    }
    private void SaveRenameCategory()
    {
        if (Catalog.ValidateCategoryName(RenameCategoryText) is { } categoryError) { FieldError("category", "שם הקטגוריה אינו תקין. הזינו שם באורך עד 100 תווים ללא תווי בקרה.", "Not saved: " + categoryError); return; }
        if (Apply(c => c.RenameCustomCategory(_renameOld, RenameCategoryText))) { RenameCategoryOpen = false; Status = "Category renamed"; }
    }
    private void BeginDeleteCategory(CategoryRow row)
    {
        var used = new Catalog(_current).WorkspacesInCategory(row.Name);
        if (used.Count == 0)
        {
            // settled edge case: no assigned workspaces means no warning at all
            if (Apply(c => c.DeleteCustomCategory(row.Name))) Status = "Category deleted";
            return;
        }
        _deleteCategoryName = row.Name;
        CategoryDeleteWorkspaces.Clear(); foreach (var w in used) CategoryDeleteWorkspaces.Add(w.Name);
        CategoryDeleteMessage = used.Count == 1
            ? $"The workspace '{used[0].Name}' uses the category '{row.Name}'. Delete it and move the workspace to {Workspace.DefaultCategory}?"
            : $"{used.Count} workspaces use the category '{row.Name}'. Delete it and move them to {Workspace.DefaultCategory}?";
        CategoryDeleteListShown = used.Count == 1; // one workspace is shown directly; several sit behind the list button
        CategoryDeletePending = true;
    }
    private void ConfirmCategoryDelete()
    {
        if (Apply(c => c.DeleteCustomCategory(_deleteCategoryName))) Status = "Category deleted, its workspaces moved to " + Workspace.DefaultCategory;
        CancelCategoryDelete();
    }
    private void CancelCategoryDelete(string? status = null)
    {
        CategoryDeletePending = false; CategoryDeleteMessage = ""; CategoryDeleteWorkspaces.Clear(); CategoryDeleteListShown = false; _deleteCategoryName = "";
        if (status != null) Status = status;
    }
    private void CloseCategoryPopup()
    {
        if (CategoryDeletePending) { CancelCategoryDelete("Category deletion cancelled, nothing was changed"); return; }
        if (RenameCategoryOpen) { RenameCategoryOpen = false; return; }
        CategoryEditOpen = false;
    }

    // ---- existing-resource picker (בחר מסביבות קיימות, settled Nov 11): items only, four type buttons, the ORIGINAL
    // resource name (never an instance's local name), search, and sorting by the number of workspaces containing that
    // exact address. Choosing fills the add form's address; the local name/description stay for the user before adding. ----
    /// <summary>One resource in the picker. Name is the catalog item's original name; Usage is the number of workspaces containing the same address.</summary>
    public sealed record PickerRow(string ItemId, string Name, string Target, int Usage, ItemType Type);
    public ObservableCollection<PickerRow> PickerRows { get; } = new();
    public bool PickerOpen { get => _pickerOpen; private set { Set(ref _pickerOpen, value); RaiseCommands(); } }
    private bool _pickerOpen;
    /// <summary>The active type button in the picker; אתרים is the default.</summary>
    public ItemType PickerType { get => _pickerType; private set => Set(ref _pickerType, value); }
    private ItemType _pickerType = ItemType.Link;
    /// <summary>The four type buttons in the settled order: תוכנות, אתרים, קבצים, תיקיות.</summary>
    public IReadOnlyList<ItemType> PickerTypes { get; } = new[] { ItemType.Link, ItemType.Program, ItemType.File, ItemType.Folder };
    /// <summary>Search filter, applied inside each type list (name and address, case-insensitive).</summary>
    public string PickerSearch { get => _pickerSearch; set { Set(ref _pickerSearch, value ?? ""); RefreshPicker(); } }
    private string _pickerSearch = "";
    /// <summary>Workspaces containing this exact address (by item identity: type + exact target). Used for the sort.</summary>
    private int UsageOf(string itemId) => _current.Workspaces.Count(w => w.Items.Any(m => m.ItemId == itemId));
    private void RefreshPicker()
    {
        PickerRows.Clear(); if (!PickerOpen) return;
        var q = _pickerSearch;
        foreach (var it in _current.Items.Values
            .Where(i => i.Type == PickerType)
            .Where(i => q.Length == 0 || i.Name.Contains(q, StringComparison.OrdinalIgnoreCase) || i.Target.Contains(q, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(i => UsageOf(i.Id)).ThenBy(i => i.Name, StringComparer.OrdinalIgnoreCase))
            PickerRows.Add(new PickerRow(it.Id, it.Name, it.Target, UsageOf(it.Id), it.Type));
    }
    private void OpenPicker() { PickerType = ItemType.Link; PickerOpen = true; RefreshPicker(); }
    private void PickResource(PickerRow row)
    {
        NewItemType = row.Type; NewTarget = row.Target;
        PickerOpen = false; Status = "Address filled from an existing resource; set a local name and description before adding";
    }

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
    // ---- navigation and the details screen (פרטי סביבה) ----
    /// <summary>False = overview (workspace cards + create form); true = the details screen of <see cref="Selected"/>.</summary>
    public bool InDetails { get => _inDetails; private set { Set(ref _inDetails, value); Raise(nameof(InOverview)); Raise(nameof(ShowEmptyItemsMessage)); RaiseCommands(); } }
    private bool _inDetails;
    public bool InOverview => !_inDetails && !InCreate && !InAllItems;
    private bool _inCreate;
    public bool InCreate
    {
        get => _inCreate;
        private set { Set(ref _inCreate, value); Raise(nameof(InOverview)); RaiseCommands(); }
    }
    public Workspace CreatePreview => new()
    {
        Name = string.IsNullOrWhiteSpace(NewName) ? "פרויקט חדש" : NewName,
        Category = NewWorkspaceCategory,
        Color = string.IsNullOrWhiteSpace(NewWorkspaceColor) ? "#7189BB" : NewWorkspaceColor,
        ImagePath = NewWorkspaceImagePath
    };
    public void ShowCreateScreen()
    {
        if (ReadOnly || CaptureOpen || InSettings || InGlobalEnv || IsChecking || IsLaunching || UnsavedOpen || InCreate) return;
        void Show() { InAllItems = false; ItemEditOpen = false; AddressEditOpen = false; OpenWithOpen = false; InDetails = false; InCreate = true; }
        if (DetailsDirty || ItemEditDirty || AddressEditDirty || DesignDirty)
        {
            MarkUnsavedHost(false, ItemEditDirty, AddressEditDirty, DesignDirty);
            _exitAction = Show; UnsavedOpen = true; return;
        }
        Show();
    }
    public void LeaveCreateScreen()
    {
        if (!InCreate) return;
        if (CreateDirty)
        {
            MarkUnsavedHost(true, false, false);
            _exitAction = () => InCreate = false; UnsavedOpen = true; return;
        }
        InCreate = false;
    }
    /// <summary>True while the in-window settings screen (הגדרות) covers the content. Independent of
    /// overview/details: חזרה לסביבות returns to whatever screen was open below.</summary>
    public bool InSettings { get => _inSettings; private set { Set(ref _inSettings, value); RaiseCommands(); } }
    private bool _inSettings;
    public RelayCommand OpenSettingsCommand { get; private set; } = null!;
    public RelayCommand CloseSettingsCommand { get; private set; } = null!;
    /// <summary>Spec strings quoted in Hebrew on the cards, kept verbatim.</summary>
    public const string EmptyWorkspaceTitle = "סביבת עבודה ריקה";
    public const string EmptyItemsMessage = "עדיין אין אייטמים בסביבה";
    /// <summary>The empty state of the details screen: message plus an הוסף אייטם button.</summary>
    public bool ShowEmptyItemsMessage => InDetails && Selected != null && Selected.Items.Count == 0;

    /// <summary>Editable workspace name on the details screen (direct editing, no edit button).</summary>
    public string EditName { get => _editName; set { Set(ref _editName, value ?? ""); RaiseDirty(); } }
    private string _editName = "";
    /// <summary>Editable category; the built-in default is כללי and custom category management is a separate feature.</summary>
    public string EditCategory { get => _editCategory; set { Set(ref _editCategory, value ?? ""); RaiseDirty(); } }
    private string _editCategory = "";
    /// <summary>Editable workspace description.</summary>
    public string EditDescription { get => _editDescription; set { Set(ref _editDescription, value ?? ""); RaiseDirty(); } }
    private string _editDescription = "";
    private bool _detailsDirtyBypass;
    /// <summary>Any change on the details screen shows the Save button (spec: כל שינוי מציג כפתור שמירה). Scoped to an open details session: before the fields load (overview/startup) the blank edit boxes must not read as "changed" (PR19 review: a freshly started app with a saved workspace looked dirty and prompted on close).</summary>
    public bool DetailsDirty => !_detailsDirtyBypass && InDetails && Selected != null && (EditName != Selected.Name || EditCategory != Selected.EffectiveCategory || EditDescription != (Selected.Description ?? ""));
    public string CreatedAtText => Selected?.CreatedAt is { } t ? t.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "unknown";
    /// <summary>Card display: last time the workspace was really opened, or never.</summary>
    public static string LastOpenedTextOf(Workspace w) => w.LastOpenedAt is { } t ? t.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "never";
    private void RaiseDirty() { Raise(nameof(DetailsDirty)); RaiseCommands(); }
    private void LoadDetails()
    {
        if (Selected is null) return;
        _editName = Selected.Name; _editCategory = Selected.EffectiveCategory; _editDescription = Selected.Description ?? "";
        Raise(nameof(EditName)); Raise(nameof(EditCategory)); Raise(nameof(EditDescription)); RaiseDirty(); Raise(nameof(CreatedAtText));
    }
    private void OpenDetails(Workspace w)
    {
        ClearPending(); ClearDup(); EmptyWorkspaceOpen = false;
        // Resolve an open inline edit before switching workspace: a dirty one blocks the navigation with a
        // clear status (its edits stay bound to the workspace they started in - PR19 review); clean ones close.
        if (ItemEditDirty || AddressEditDirty) { Status = "Save or cancel the open item edit before switching workspaces"; return; }
        ItemEditOpen = false; AddressEditOpen = false; OpenWithOpen = false;
        Selected = w; LoadDetails(); InDetails = true;
    }
    private void Back()
    {
        ClearPending(); ClearDup(); EmptyWorkspaceOpen = false;
        if (DetailsDirty) { MarkUnsavedHost(false, false, false); _exitAction = () => { ItemEditOpen = false; AddressEditOpen = false; InDetails = false; }; UnsavedOpen = true; return; }
        if (ItemEditDirty) { MarkUnsavedHost(false, true, false); _exitAction = () => { ItemEditOpen = false; AddressEditOpen = false; InDetails = false; }; UnsavedOpen = true; return; }
        if (AddressEditDirty) { MarkUnsavedHost(false, false, true); _exitAction = () => { AddressEditOpen = false; InDetails = false; }; UnsavedOpen = true; return; }
        ItemEditOpen = false; AddressEditOpen = false; OpenWithOpen = false;
        InDetails = false;
    }
    private bool SaveDetails()
    {
        var id = Selected!.Id; var n = EditName; var d = EditDescription; var cat = EditCategory;
        if (string.IsNullOrWhiteSpace(n)) { FieldError("details", "חסר שם לסביבה. הזינו שם כדי לשמור.", "Not saved: workspace name is required"); return false; }
        if (Catalog.ValidateDescription(d) is { } detailError) { FieldError("details", "התיאור אינו תקין. הזינו עד 500 תווים ללא תווי בקרה.", "Not saved: " + detailError); return false; }
        var ok = Apply(c => c.UpdateWorkspace(id, n, d, cat == Workspace.DefaultCategory ? null : cat)); // the default stays the default, not an explicit value
        if (ok) { Status = "Workspace details saved"; RaiseDirty(); Raise(nameof(CreatedAtText)); }
        return ok;
    }
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
        RefreshCategories();
        Workspaces.Clear(); foreach (var w in s.Workspaces) Workspaces.Add(w);
        ApplyView();
        Raise(nameof(HasOtherWorkspaces));
        Selected = Workspaces.FirstOrDefault(w => w.Id == keep) ?? Workspaces.FirstOrDefault();
    }

    private static int GroupRank(ItemType t) => Catalog.DisplayGroup(t);
    public static string GroupOf(ItemType t) => t switch { ItemType.Link => "Sites", ItemType.Folder => "Folders", ItemType.File => "Files", _ => "Programs" };

    private void RefreshItems()
    {
        Items.Clear(); Raise(nameof(ShowEmptyItemsMessage)); if (Selected is null) return;
        foreach (var m in Selected.Items.Select((m, i) => (m, i)).OrderBy(x => _current.Items.TryGetValue(x.m.ItemId, out var g) ? GroupRank(g.Type) : 4).ThenBy(x => x.i))
            if (_current.Items.TryGetValue(m.m.ItemId, out var it))
                Items.Add(new ItemRow(m.m.Id ?? m.m.ItemId, it.Id, m.m.Name ?? it.Name, it.Target, it.Type.ToString(), GroupOf(it.Type), m.m.Description ?? it.Description, m.m.Launch?.Browser ?? m.m.Launch?.OpenWith));
    }

    /// <summary>Manual add. The target and name are used exactly as typed (never trimmed); invalid input is reported, not corrected.
    /// The description is stored on the instance, so it never changes another workspace or another instance. Adding a target that already
    /// exists in this workspace first asks for confirmation; confirming adds a second, independent instance.</summary>
    private void AddItem(bool force = false)
    {
        var type = NewItemType; var target = NewTarget;
        if (Uri.TryCreate(target, UriKind.Absolute, out var web) && (web.Scheme == Uri.UriSchemeHttp || web.Scheme == Uri.UriSchemeHttps)) type = ItemType.Link;
        var wsId = Selected!.Id; var desc = NewDescription; var name = NewItemName;
        var err = Catalog.ValidateInputTarget(type, target) ?? Catalog.ValidateDescription(desc) ?? Catalog.ValidateInstanceName(string.IsNullOrWhiteSpace(name) ? null : name);
        if (err != null) { FieldError("add", err == Catalog.WebsiteTargetError ? err : "פרטי האייטם אינם תקינים. בדקו את הכתובת בהתאם לסוג, את השם ואת התיאור.", "Not added: " + err); return; }
        if (!force && new Catalog(_current).HasTarget(wsId, type, target))
        {
            _pendingDup = true; Raise(nameof(DuplicatePending)); RaiseCommands();
            DuplicateMessage = "This target is already in the workspace. Add it again as a separate instance with its own name and settings?";
            return;
        }
        _pendingDup = false; DuplicateMessage = ""; Raise(nameof(DuplicatePending));
        if (Apply(c => { var it = c.GetOrAddItem(type, DisplayName(type, target), target); c.AddToWorkspace(wsId, it.Id, null, string.IsNullOrWhiteSpace(name) ? null : name, desc); }))
        {
            NewTarget = ""; NewDescription = ""; NewItemName = "";
        }
    }

    private static string DisplayName(ItemType type, string target)
    {
        if (type == ItemType.Link && Uri.TryCreate(target, UriKind.Absolute, out var u) && u.Host.Length > 0) return u.Host;
        var i = target.LastIndexOfAny(new[] { '\\', '/' });
        return i >= 0 && i < target.Length - 1 ? target[(i + 1)..] : target;
    }

    /// <summary>Creates the workspace from the create form (name required; כללי is the default category; description optional)
    /// and then opens its details screen, as the spec requires (אחרי היצירה נפתחים פרטי סביבה).</summary>
    private void Create()
    {
        var n = NewName; var d = NewWorkspaceDescription; var cat = NewWorkspaceCategory;
        if (string.IsNullOrWhiteSpace(n) || Catalog.ValidateDescription(d) is not null) { FieldError("create", "הזינו שם לסביבה ותיאור באורך עד 500 תווים.", "Not created: invalid workspace fields"); return; }
        var color = NewWorkspaceColor.Trim(); var image = NewWorkspaceImagePath.Trim();
        if (InCreate && color.Length == 0) color = "#7189BB";
        if (ValidateCreateDesign(color, image) is { } de) { FieldError("create", "בדקו את הצבע ואת נתיב התמונה. צבע יש להזין בצורה #RRGGBB ותמונה כנתיב מלא.", "Not created: " + de); return; }
        string? id = null;
        if (Apply(c => { id = c.CreateWorkspace(n, d).Id; c.SetCategory(id, cat); c.SetCardDesign(id, color.Length > 0 ? color : null, image.Length > 0 ? image : null); }))
        {
            ResetCreateForm();
            InCreate = false;
            Selected = Workspaces.FirstOrDefault(w => w.Id == id);
            LoadDetails(); InDetails = true;
        }
    }

    /// <summary>Optional design at creation (both create forms): the color rule is the same as עצב כרטיס, and a typed
    /// image path must be an explicit absolute Windows path. A well-formed path whose file is missing stays allowed:
    /// missing files are a settled tolerated state with honest warnings elsewhere in the product, and the card renders
    /// its fallback. A refusal changes nothing and keeps the typed values for fixing.</summary>
    private static string? ValidateCreateDesign(string color, string image)
    {
        if (color.Length > 0 && Catalog.ValidateColor(color) is { } ce) return ce;
        if (image.Length > 0 && (!Catalog.IsAbsoluteWindowsPath(image) || image.Any(char.IsControl))) return "card image must be an explicit absolute Windows path";
        return null;
    }

    private bool Apply(Action<Catalog> change)
    {
        try { Show(_repo.Mutate(change)); _saveFailed = false; Raise(nameof(SaveBlocked)); ClearFieldErrors(); Status = ""; return true; }
        catch (StoreLockedException e) { SaveFailure(e.Message, "הנתונים בשימוש. סגרו חלון נוסף של התוכנה ונסו לשמור שוב. השינויים לא נשמרו."); }
        catch (StoreCorruptException e) { ReadOnly = true; Status = "Data file is invalid and was left untouched: " + e.Message; }
        catch (StoreTooNewException e) { ReadOnly = true; Status = e.Message; }
        catch (Exception e) when (e is IOException or InvalidOperationException or ArgumentException or UnauthorizedAccessException) { SaveFailure(e.Message); _status = "Save failed, nothing was changed: " + e.Message; Raise(nameof(Status)); }
        return false;
    }


}
