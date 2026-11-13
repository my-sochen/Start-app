using System.Runtime.ExceptionServices;
using System.Collections.ObjectModel;
using System.Windows.Input;
using StartApp.Core;

namespace StartApp.Presentation;

/// <summary>One visible instance row. Name/Description are the instance's own values with fallback to the catalog item's original name /
/// no description. Group is the display category (decided Nov 12: sites, folders, files, programs - the earlier files+programs combined group was superseded); order inside a group is undecided, so insertion order is kept.</summary>
public sealed record ItemRow(string InstanceId, string ItemId, string Name, string Target, string Type, string Group, string? Description = null, string? Program = null);

/// <summary>UI state only. Persistence via IStoreRepository, OS actions via IItemLauncher. A store that failed to load puts the app in a
/// read-only state with a message; nothing is written until the user decides (no silent reset).</summary>
public sealed class MainViewModel : ObservableBase
{
    private readonly IStoreRepository _repo;
    private readonly IItemLauncher _launcher;
    private readonly IItemPreflight? _preflight;
    private readonly IOpenStateDetector _detector;
    private readonly IOpenItemCapturer _capturer;
    private List<(Item Item, LaunchConfig? Launch, PreflightFinding Finding, OpenState Dup)>? _pendingOpen;
    private Workspace? _selected; private string _status = ""; private bool _readOnly; private string _newName = "";

    public ObservableCollection<Workspace> Workspaces { get; } = new();
    public ObservableCollection<ItemRow> Items { get; } = new();
    private readonly List<RelayCommand> _commands = new();
    private RelayCommand Cmd(Action<object?> run, Func<object?, bool> can) { var c = new RelayCommand(run, can); _commands.Add(c); return c; }
    private void RaiseCommands() { foreach (var c in _commands) c.Raise(); Raise(nameof(OpenDecisionPending)); Raise(nameof(ShowOpenPanel)); Raise(nameof(ConfirmLabel)); Raise(nameof(DuplicatePending)); }
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
    public ICommand ConfirmOpenAnywayCommand { get; }
    public ICommand CancelOpenCommand { get; }
    /// <summary>Per-item problems found before opening; the user decides. Empty when nothing is pending.</summary>
    public ObservableCollection<string> OpenProblems { get; } = new();
    /// <summary>Per-item outcome of the last open (one line per item).</summary>
    public ObservableCollection<string> OpenReport { get; } = new();
    public bool OpenDecisionPending => _pendingOpen != null;
    /// <summary>Label of the confirm button. When every item is skipped by policy, confirming only shows the report, so it says so.</summary>
    public string ConfirmLabel => _pendingOpen != null && _pendingOpen.All(p => NeverLaunched(p.Finding.State)) ? "Show report" : "Open anyway";
    /// <summary>The open panel (with Cancel) is visible while a decision is pending AND while checking or launching, so the user can always cancel.</summary>
    public bool ShowOpenPanel => _pendingOpen != null || _isChecking || _isLaunching;
    public IReadOnlyList<ItemType> ItemTypes { get; } = Enum.GetValues<ItemType>();

    public MainViewModel(IStoreRepository repo, IItemLauncher launcher, IItemPreflight? preflight = null, IOpenStateDetector? detector = null, IOpenItemCapturer? capturer = null)
    {
        _repo = repo; _launcher = launcher; _preflight = preflight; _detector = detector ?? new NoOpenStateDetection(); _capturer = capturer ?? new NoCapture();
        InitCaptureCommands();
        CreateWorkspaceCommand = Cmd(_ => Create(), _ => !ReadOnly && NewName.Length > 0);
        // No workspace deletion: the user explicitly rejected it from the card actions (Nov 11); the menu is
        // הצג / שגר / הוסף אייטם / עצב כרטיס / ערוך קטגוריה only. Catalog.DeleteWorkspace stays unexposed.
        OpenDetailsCommand = Cmd(p => OpenDetails((Workspace)p!), p => p is Workspace);
        OpenWorkspaceForCommand = Cmd(p => { Selected = (Workspace)p!; Open(); }, p => p is Workspace && !IsChecking && !IsLaunching);
        AddItemToCommand = Cmd(p => { OpenDetails((Workspace)p!); FocusAddRequested?.Invoke(); }, p => p is Workspace);
        BackCommand = Cmd(_ => Back(), _ => InDetails);
        SaveDetailsCommand = Cmd(_ => SaveDetails(), _ => InDetails && DetailsDirty && !ReadOnly);
        SaveAndExitCommand = Cmd(_ => SaveAndExit(), _ => UnsavedOpen);
        ExitWithoutSavingCommand = Cmd(_ => { UnsavedOpen = false; if (UnsavedForCapture) { DiscardCaptureChanges(); } else if (UnsavedForItemEdit) { ItemEditOpen = false; } else if (UnsavedForAddress) { AddressEditOpen = false; } else if (UnsavedForCreate) { NewName = ""; NewWorkspaceDescription = ""; NewWorkspaceCategory = Workspace.DefaultCategory; } else if (UnsavedForDesign) { DesignEditOpen = false; } else { _detailsDirtyBypass = true; LoadDetails(); _detailsDirtyBypass = false; } var a = _exitAction; _exitAction = () => { }; a(); }, _ => UnsavedOpen);
        CloseUnsavedCommand = Cmd(_ => { UnsavedOpen = false; _exitAction = () => { }; }, _ => UnsavedOpen);
        FocusAddCommand = Cmd(_ => FocusAddRequested?.Invoke(), _ => InDetails);
        OpenItemCommand = Cmd(p => OpenOne((ItemRow)p!), p => !ReadOnly && Selected != null && p is ItemRow && !IsChecking && !IsLaunching);
        CopyAddressCommand = Cmd(p => { CopyAddressRequested?.Invoke(((ItemRow)p!).Target); Status = "Address copied"; }, p => p is ItemRow);
        EditItemCommand = Cmd(p => BeginItemEdit((ItemRow)p!), p => !ReadOnly && Selected != null && p is ItemRow && !ItemEditDirty && !AddressEditDirty);
        SaveItemEditCommand = Cmd(_ => SaveItemEdit(), _ => ItemEditOpen && ItemEditDirty && !ReadOnly);
        CancelItemEditCommand = Cmd(_ => CancelItemEdit(), _ => ItemEditOpen);
        EditAddressCommand = Cmd(p => BeginAddressEdit((ItemRow)p!), p => !ReadOnly && Selected != null && p is ItemRow && !ItemEditDirty && !AddressEditDirty);
        SaveAddressCommand = Cmd(_ => SaveAddress(), _ => AddressEditOpen && AddressEditDirty && !ReadOnly);
        CopyFullAddressCommand = Cmd(_ => { CopyAddressRequested?.Invoke(EditAddress); Status = "Address copied"; }, _ => AddressEditOpen);
        CloseAddressEditCommand = Cmd(_ => CloseAddressEdit(), _ => AddressEditOpen);
        OpenWithCommand = Cmd(p => BeginOpenWith((ItemRow)p!), p => !ReadOnly && Selected != null && p is ItemRow);
        OpenWithGoCommand = Cmd(_ => OpenWithGo(), _ => OpenWithOpen && !ReadOnly && !IsChecking && !IsLaunching);
        CloseOpenWithCommand = Cmd(_ => CloseOpenWith(), _ => OpenWithOpen);
        MoveItemUpCommand = Cmd(p => Apply(c => c.MoveInstance(Selected!.Id, ((ItemRow)p!).InstanceId, -1)), p => !ReadOnly && Selected != null && p is ItemRow);
        MoveItemDownCommand = Cmd(p => Apply(c => c.MoveInstance(Selected!.Id, ((ItemRow)p!).InstanceId, 1)), p => !ReadOnly && Selected != null && p is ItemRow);
        DesignCardCommand = Cmd(p => BeginDesign((Workspace)p!), p => p is Workspace && !ReadOnly);
        PickImageCommand = Cmd(_ => PickImageRequested?.Invoke(), _ => DesignEditOpen && !ReadOnly);
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
        OpenWorkspaceCommand = Cmd(_ => Open(), _ => Selected != null && !IsChecking && !IsLaunching);
        ConfirmOpenAnywayCommand = Cmd(_ => ConfirmOpen(), _ => _pendingOpen != null);
        CancelOpenCommand = Cmd(_ => { var launching = IsLaunching; ClearPending(); if (!launching) Status = "Open cancelled, nothing was opened"; }, _ => _pendingOpen != null || IsChecking || IsLaunching);
        Reload();
    }

    private void ClearPending() { if (IsChecking) { _checkCts?.Cancel(); _generation++; IsChecking = false; } if (IsLaunching) _launchCts?.Cancel(); _pendingOpen = null; OpenProblems.Clear(); Raise(nameof(OpenDecisionPending)); RaiseCommands(); }

    public Workspace? Selected { get => _selected; set { ClearPending(); ClearDup(); EmptyWorkspaceOpen = false; if (!ItemEditDirty) ItemEditOpen = false; if (!AddressEditDirty) AddressEditOpen = false; OpenWithOpen = false; Set(ref _selected, value); if (InDetails && !DetailsDirty) LoadDetails(); RefreshItems(); Raise(nameof(ShowEmptyItemsMessage)); Raise(nameof(CreatedAtText)); RaiseCommands(); } }
    // An ordinary refresh (Apply -> Show re-selects the same workspace) must not silently drop an open
    // unsaved item/address edit (PR19 review: edits disappeared on unrelated mutations). A dirty form stays
    // open with its fields. Safety does not rest on the selection staying put: saves validate the
    // workspace+instance captured when the form opened, and dirty forms block workspace switches and row
    // deletion, so a preserved form can neither crash nor silently retarget.
    public string Status { get => _status; private set => Set(ref _status, value); }
    public bool ReadOnly { get => _readOnly; private set { Set(ref _readOnly, value); RaiseCommands(); } }
    public ItemType NewItemType { get => _newType; set { Set(ref _newType, value); ClearDup(); } }
    public string NewTarget { get => _newTarget; set { Set(ref _newTarget, value); ClearDup(); RaiseCommands(); } }
    private ItemType _newType = ItemType.File; private string _newTarget = "";
    /// <summary>Optional local name for the instance in this workspace. Empty means the original (derived) name.</summary>
    public string NewItemName { get => _newItemName; set { Set(ref _newItemName, value ?? ""); ClearDup(); } }
    private string _newItemName = "";
    /// <summary>Optional description typed in the add form. Empty means none.</summary>
    public string NewDescription { get => _newDescription; set { Set(ref _newDescription, value ?? ""); ClearDup(); } }
    private string _newDescription = "";

    private void ClearDup(string? status = null) { if (!_pendingDup) return; _pendingDup = false; DuplicateMessage = ""; if (status != null) Status = status; Raise(nameof(DuplicatePending)); RaiseCommands(); }
    public string NewName { get => _newName; set { Set(ref _newName, value); Raise(nameof(CreateDirty)); RaiseCommands(); } }
    /// <summary>Optional workspace description in the create form.</summary>
    public string NewWorkspaceDescription { get => _newWsDesc; set { Set(ref _newWsDesc, value ?? ""); Raise(nameof(CreateDirty)); } }
    private string _newWsDesc = "";
    /// <summary>Category chosen in the create form from the managed list (קטגוריה: כללי מסומנת כברירת מחדל, ואפשר לבחור אחרת).</summary>
    public string NewWorkspaceCategory { get => _newWsCat; set { Set(ref _newWsCat, value ?? Workspace.DefaultCategory); Raise(nameof(CreateDirty)); } }
    private string _newWsCat = Workspace.DefaultCategory;
    /// <summary>The create form holds unsaved input; closing the app then goes through the same unsaved-exit
    /// dialog (spec: דיאלוג יציאה עם שינויים שלא נשמרו applies to טופס יצירת סביבה too).</summary>
    public bool CreateDirty => NewName.Length > 0 || NewWorkspaceDescription.Length > 0 || NewWorkspaceCategory != Workspace.DefaultCategory;
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
    private void MarkUnsavedHost(bool forCreate, bool forItemEdit, bool forAddress, bool forDesign = false, bool forCapture = false) { UnsavedForCapture = forCapture; UnsavedForCreate = forCreate; UnsavedForItemEdit = forItemEdit; UnsavedForAddress = forAddress; UnsavedForDesign = forDesign; }

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
            var errS = Catalog.ValidateTarget(sa.Type, EditAddress);
            if (errS != null) { Status = "Not saved: " + errS; return false; }
            sa.Target = EditAddress; Status = "Address updated on the staged item (saved to the workspace on שמור)"; AddressEditOpen = false;
            return true;
        }
        // Same binding rule as the item editor: validate the workspace+instance captured when the popup
        // opened, never the current selection; a missing record keeps the typed address in the form.
        var wsA = _current.Workspaces.FirstOrDefault(w => w.Id == _addressWsId);
        var m = wsA?.Items.FirstOrDefault(x => (x.Id ?? x.ItemId) == _addressInstId);
        if (m is null || !_current.Items.TryGetValue(m.ItemId, out var it)) { Status = "Not saved: the saved item record is missing; your address is still in the form"; return false; }
        var err = Catalog.ValidateTarget(it.Type, EditAddress);
        if (err != null) { Status = "Not saved: " + err; return false; }
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
        if (color.Length > 0 && Catalog.ValidateColor(color) is { } ce) { Status = "Not saved: " + ce; return false; }
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
        if (Apply(c => c.AddCustomCategory(NewCategoryName))) { NewCategoryName = ""; Status = "Category added"; }
    }
    private void SaveRenameCategory()
    {
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
    public IReadOnlyList<ItemType> PickerTypes { get; } = new[] { ItemType.Program, ItemType.Link, ItemType.File, ItemType.Folder };
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
    public bool ItemEditOpen { get => _itemEditOpen; private set { Set(ref _itemEditOpen, value); RaiseCommands(); } }
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
    public bool ItemEditDirty => ItemEditOpen && (EditItemName != _origItemName || EditItemDescription != _origItemDesc || EditItemTarget != _origItemTarget);
    private void BeginItemEdit(ItemRow row)
    {
        if (!TryOpenEditorForm()) return;
        _editItemWsId = Selected?.Id ?? ""; _editItemId = row.InstanceId; _origItemName = row.Name; _origItemDesc = row.Description ?? ""; _origItemTarget = row.Target;
        EditItemName = _origItemName; EditItemDescription = _origItemDesc; EditItemTarget = _origItemTarget;
        ItemEditOpen = true;
    }
    private bool SaveItemEdit()
    {
        if (string.IsNullOrWhiteSpace(EditItemName)) { Status = "Not saved: item name is required"; return false; }
        // Validate against the workspace+instance captured when the editor opened, never the current
        // selection (PR19 review: the row can be deleted or the view switched while the editor is open).
        // A missing target never throws and never silently retargets: the typed changes stay in the form
        // so the user decides (cancel, or re-add the item).
        var ws = _current.Workspaces.FirstOrDefault(w => w.Id == _editItemWsId);
        if (ws is null || !ws.Items.Any(x => (x.Id ?? x.ItemId) == _editItemId))
        { Status = "Not saved: this item was removed; your changes are still in the form - cancel the edit or add the item again"; return false; }
        var targetChanged = EditItemTarget != _origItemTarget;
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
        });
        if (ok) { Status = "Item saved"; ItemEditOpen = false; }
        return ok;
    }
    private void CancelItemEdit()
    {
        if (ItemEditDirty) { MarkUnsavedHost(false, true, false); _exitAction = () => ItemEditOpen = false; UnsavedOpen = true; return; }
        ItemEditOpen = false;
    }
    /// <summary>לחיצה על שם האייטם: opens that one item after the same honest preflight the full open uses; the report line
    /// never claims more than a sent request. A real request also stamps the card's last-opened time.</summary>
    private void OpenOne(ItemRow row)
    {
        var m = Selected!.Items.FirstOrDefault(x => (x.Id ?? x.ItemId) == row.InstanceId);
        if (m is null || !_current.Items.TryGetValue(m.ItemId, out var it)) { Status = row.Name + ": not opened (the saved item record is missing)"; return; }
        // After הגדר כברירת מחדל a plain name click opens with the saved choice directly, without reopening the popup.
        OpenOneCore(row.Name, it, m.Launch);
    }
    private void OpenOneCore(string name, Item it, LaunchConfig? launch)
    {
        var f = PreflightRules.CheckForm(it, launch) ?? _preflight?.Check(it, launch) ?? new PreflightFinding(PreflightState.Ready);
        if (NeverLaunched(f.State)) { Status = name + ": skipped, not opened (" + f.Detail + ")"; return; }
        var gen = ++_generation;
        OpenTask = Guard(gen, OpenOneAsync(it, launch, name, gen));
    }
    private async Task OpenOneAsync(Item it, LaunchConfig? launch, string name, int gen)
    {
        var r = await Task.Run(() => _launcher.Launch(it, launch)).ConfigureAwait(true);
        if (gen != _generation) return;
        // No TouchLastOpened here: the settled card field "פתיחה אחרונה" is the workspace's last launch (שגר);
        // no source defines a single-item open as stamping it, so it does not.
        Status = r.Status == LaunchStatus.Started ? name + ": open request sent (not proof it loaded)" : name + ": failed (" + (r.Detail ?? r.Status.ToString()) + ")";
    }

    // ---- navigation and the details screen (פרטי סביבה) ----
    /// <summary>False = overview (workspace cards + create form); true = the details screen of <see cref="Selected"/>.</summary>
    public bool InDetails { get => _inDetails; private set { Set(ref _inDetails, value); Raise(nameof(InOverview)); Raise(nameof(ShowEmptyItemsMessage)); RaiseCommands(); } }
    private bool _inDetails;
    public bool InOverview => !_inDetails;
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
        if (string.IsNullOrWhiteSpace(n)) { Status = "Not saved: workspace name is required"; return false; }
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
        var type = NewItemType; var target = NewTarget; var wsId = Selected!.Id; var desc = NewDescription; var name = NewItemName;
        var err = Catalog.ValidateTarget(type, target) ?? Catalog.ValidateDescription(desc) ?? Catalog.ValidateInstanceName(string.IsNullOrWhiteSpace(name) ? null : name);
        if (err != null) { Status = "Not added: " + err; return; }
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
        var n = NewName; var d = NewWorkspaceDescription; var cat = NewWorkspaceCategory; string? id = null;
        if (Apply(c => { id = c.CreateWorkspace(n, d).Id; c.SetCategory(id, cat); }))
        {
            NewName = ""; NewWorkspaceDescription = ""; NewWorkspaceCategory = Workspace.DefaultCategory;
            Selected = Workspaces.FirstOrDefault(w => w.Id == id);
            LoadDetails(); InDetails = true;
        }
    }

    private bool Apply(Action<Catalog> change)
    {
        try { Show(_repo.Mutate(change)); Status = ""; return true; }
        catch (StoreLockedException e) { Status = e.Message; }
        catch (StoreCorruptException e) { ReadOnly = true; Status = "Data file is invalid and was left untouched: " + e.Message; }
        catch (StoreTooNewException e) { ReadOnly = true; Status = e.Message; }
        catch (Exception e) when (e is IOException or InvalidOperationException or ArgumentException or UnauthorizedAccessException) { Status = "Save failed, nothing was changed: " + e.Message; }
        return false;
    }

    /// <summary>Every item is checked first (environment and already-open state). Anything that is not clean is listed with its reason and nothing starts until the
    /// user chooses open anyway or cancel. Already-open is Unknown unless a detector has reliable exact-target evidence: Unknown is reported as "cannot tell whether
    /// this is already open, opening may create a duplicate", never as a duplicate and never as safe. After "anyway", Unsupported items (alias, non-http(s) link,
    /// bad config) are never launched; the rest are attempted exactly as saved, with no substitute app. Nothing is closed. "Started" only means the open request was
    /// sent successfully, not that the document loaded.</summary>
    /// <summary>Overall time allowed for the pre-open checks. Items still unchecked after this are reported as "could not be checked", never as fine.</summary>
    public TimeSpan OpenCheckTimeout { get; set; } = TimeSpan.FromSeconds(10);
    private const int CheckWorkers = 4;
    private int _generation; private CancellationTokenSource? _checkCts; private bool _isChecking;
    private CancellationTokenSource? _launchCts; private bool _isLaunching;
    public bool IsLaunching { get => _isLaunching; private set { Set(ref _isLaunching, value); RaiseCommands(); } }
    public bool IsChecking { get => _isChecking; private set { Set(ref _isChecking, value); RaiseCommands(); } }
    /// <summary>The running (or last) check-and-open operation. UI code never waits on it; tests await it.</summary>
    public Task OpenTask { get; private set; } = Task.CompletedTask;

    private string _openWsId = "";
    private void Open()
    {
        ClearPending(); OpenReport.Clear();
        if (Selected!.Items.Count == 0) { EmptyWorkspaceOpen = true; return; }
        _openWsId = Selected!.Id;
        // Snapshot semantics: what is checked and opened is the saved state at the moment Open was pressed; the store file is not watched.
        var snapshot = Selected!.Items.Select(m => (m, Item: _current.Items.TryGetValue(m.ItemId, out var it) ? it : null)).ToList();
        _checkCts?.Cancel(); _checkCts = new CancellationTokenSource(); var gen = ++_generation;
        IsChecking = true; Status = "Checking items before opening...";
        OpenTask = Guard(gen, CheckThenOpenAsync(snapshot, gen, _checkCts.Token));
    }

    private (Item Item, LaunchConfig? Launch, PreflightFinding Finding, OpenState Dup) Evaluate(Membership m, Item? it)
    {
        if (it is null) return (new Item(m.ItemId, ItemType.File, "(missing item " + m.ItemId + ")", ""), m.Launch, new(PreflightState.Unsupported, "the saved item record is missing from the catalog"), OpenState.NotOpen);
        var f = PreflightRules.CheckForm(it, m.Launch) ?? _preflight?.Check(it, m.Launch) ?? new(PreflightState.Ready);
        var dup = f.State == PreflightState.Unsupported ? OpenState.NotOpen : _detector.Detect(it, m.Launch);
        return (it, m.Launch, f, dup);
    }

    /// <summary>Checks run on at most <see cref="CheckWorkers"/> background workers, never on the caller's thread, with one overall deadline. A newer Open, a
    /// selection change or Cancel makes this run stale: its result is dropped. Probes already blocked in the OS cannot be cancelled but are bounded (see FileSystemProbe).</summary>
    private async Task CheckThenOpenAsync(List<(Membership M, Item? Item)> snapshot, int gen, CancellationToken token)
    {
        var stop = CancellationTokenSource.CreateLinkedTokenSource(token); // deliberately not disposed: a late worker may still call stop.Cancel(), and the parent token source is per operation, so nothing outlives the operation // the deadline stops workers from taking NEW items; a call already running finishes by itself
        var results = new (Item Item, LaunchConfig? Launch, PreflightFinding Finding, OpenState Dup)?[snapshot.Count];
        var next = -1; Exception? fault = null; var gate = new object(); var published = false; var reported = 0; // results[] is written by workers and read once at the deadline: both under gate
        Task Worker() => Task.Factory.StartNew(() => // dedicated threads: these calls block, and must not starve the thread pool (or the deadline timer)
        {
            while (!stop.Token.IsCancellationRequested)
            {
                var i = Interlocked.Increment(ref next); if (i >= snapshot.Count) return;
                try { var r = Evaluate(snapshot[i].M, snapshot[i].Item); lock (gate) { if (!published) results[i] = r; } }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
                { var r = (snapshot[i].Item ?? new Item(snapshot[i].M.ItemId, ItemType.File, snapshot[i].M.ItemId, ""), snapshot[i].M.Launch, new PreflightFinding(PreflightState.Unavailable, e.GetType().Name), OpenState.Unknown); lock (gate) { if (!published) results[i] = r; } }
                catch (Exception e) { Interlocked.CompareExchange(ref fault, e, null); stop.Cancel(); bool late; lock (gate) late = published; if (late && Interlocked.Exchange(ref reported, 1) == 0) ReportLate(e); return; } // after the deadline nobody awaits this worker: report now, not when all workers finish // a programming bug is not "unavailable": stop and surface it
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        var all = Task.WhenAll(Enumerable.Range(0, Math.Min(CheckWorkers, snapshot.Count)).Select(_ => Worker()));
        await Task.WhenAny(all, Task.Delay(OpenCheckTimeout, token)).ConfigureAwait(true);
        // Workers still running at the deadline are not awaited, but a fault they hit is still observed exactly once: either thrown here or reported through ReportLate.
        _ = all.ContinueWith(t => { var late = Volatile.Read(ref fault) ?? t.Exception?.GetBaseException(); if (late != null && Interlocked.Exchange(ref reported, 1) == 0) ReportLate(late); }, TaskScheduler.Default);
        (Item Item, LaunchConfig? Launch, PreflightFinding Finding, OpenState Dup)?[] done;
        lock (gate) { published = true; done = ((Item Item, LaunchConfig? Launch, PreflightFinding Finding, OpenState Dup)?[])results.Clone(); }
        var f0 = Volatile.Read(ref fault);
        if (f0 != null)
        {
            if (Interlocked.Exchange(ref reported, 1) == 0) ExceptionDispatchInfo.Capture(f0).Throw();
            if (gen == _generation) { IsChecking = false; Status = "Open stopped by an unexpected error: " + f0.GetType().Name; } // already reported through ReportLate
            return;
        }
        if (gen != _generation || token.IsCancellationRequested) return;
        stop.Cancel();
        IsChecking = false;
        var plan = new List<(Item Item, LaunchConfig? Launch, PreflightFinding Finding, OpenState Dup)>();
        for (int i = 0; i < snapshot.Count; i++)
        {
            var r = done[i];
            if (r is { } ok) plan.Add(ok);
            else if (snapshot[i].Item is { } it) plan.Add((it, snapshot[i].M.Launch, new(PreflightState.Unavailable, "check did not finish in time"), OpenState.Unknown));
        }
        var bad = plan.Where(p => p.Finding.State != PreflightState.Ready || p.Dup != OpenState.NotOpen).ToList();
        if (bad.Count == 0) { await LaunchAsync(plan); return; }
        _pendingOpen = plan;
        foreach (var b in bad) OpenProblems.Add($"{b.Item.Name}: {Describe(b.Finding, b.Dup)}");
        var skipped = bad.Count(b => NeverLaunched(b.Finding.State)); var confirmable = bad.Count - skipped;
        var onlyUnknown = bad.Count(b => !NeverLaunched(b.Finding.State) && b.Finding.State == PreflightState.Ready && b.Dup == OpenState.Unknown);
        var msg = new List<string>();
        if (skipped > 0) msg.Add($"{skipped} {(skipped == 1 ? "item" : "items")} will be skipped.");
        if (confirmable > 0) msg.Add($"{confirmable} {(confirmable == 1 ? "item" : "items")} can be opened after your confirmation" + (onlyUnknown > 0 ? $" (for {onlyUnknown} we only cannot tell whether {(onlyUnknown == 1 ? "it is" : "they are")} already open)." : "."));
        msg.Add("Nothing has been opened yet.");
        Status = string.Join(" ", msg);
        Raise(nameof(OpenDecisionPending)); RaiseCommands();
    }

    private static string Describe(PreflightFinding f, OpenState dup)
    {
        var parts = new List<string>();
        if (f.State == PreflightState.Missing) parts.Add("not found" + (f.Detail is null ? "" : " (" + f.Detail + ")"));
        else if (f.State == PreflightState.Unsupported) return "will not be opened (" + f.Detail + ")";
        else if (f.State == PreflightState.NeedsDecision) return "will be skipped, not opened (" + f.Detail + ")";
        else if (f.State == PreflightState.Unavailable) parts.Add("could not be checked" + (f.Detail is null ? "" : " (" + f.Detail + ")"));
        if (dup == OpenState.Open) parts.Add("already open (confirmed), opening again will create a duplicate");
        else if (dup == OpenState.Unknown) parts.Add("cannot tell whether it is already open, opening may create a duplicate");
        return string.Join("; ", parts);
    }

    private void ConfirmOpen() { var plan = _pendingOpen!; ClearPending(); var gen = ++_generation; OpenTask = Guard(gen, LaunchAsync(plan)); }

    /// <summary>The last unexpected error that arrived after its operation was already over (late worker fault) and had no <see cref="UnexpectedError"/> subscriber.</summary>
    public Exception? LateError { get; private set; }
    private void ReportLate(Exception e) { var h = UnexpectedError; if (h is null) LateError = e; else h(e); }

    /// <summary>Raised for an unexpected (programming) error in an open operation, after the view model has reset its busy state and published what it knows.
    /// The host decides how to surface it (the App rethrows it on the UI dispatcher so it is not silent). With no subscriber the error is rethrown into <see cref="OpenTask"/>.</summary>
    public event Action<Exception>? UnexpectedError;

    /// <summary>A fire-and-forget command must not leave a silently faulted task. Resets busy flags (only if this operation is still the current one),
    /// shows a status, then hands the error to the host; it is never swallowed.</summary>
    private async Task Guard(int gen, Task t)
    {
        try { await t; }
        catch (Exception e)
        {
            if (gen == _generation) { IsChecking = false; IsLaunching = false; Status = "Open stopped by an unexpected error: " + e.GetType().Name + ". See the report for what was already sent."; }
            var h = UnexpectedError;
            if (h is null) throw;
            h(e);
        }
    }

    private static bool NeverLaunched(PreflightState s) => s is PreflightState.Unsupported or PreflightState.NeedsDecision;

    /// <summary>Starts items one after another on a background worker (Process.Start can block). Cancel stops FURTHER items only: requests already sent cannot be undone
    /// and are reported. A start call that is blocked in the OS cannot be interrupted; only the single launch worker is ever held by it.</summary>
    private async Task LaunchAsync(List<(Item Item, LaunchConfig? Launch, PreflightFinding Finding, OpenState Dup)> plan)
    {
        OpenReport.Clear(); _launchCts = new CancellationTokenSource(); var cts = _launchCts; IsLaunching = true; Status = "Opening items...";
        var res = new List<(string Line, int Ok, int Refused, int NotTried)>(); // filled by the worker; read only after it finished
        Exception? fault = null;
        await Task.Run(() =>
        {
            try { foreach (var p in plan)
            {
                if (cts.IsCancellationRequested) { res.Add(($"{p.Item.Name}: not attempted (cancelled before this item)", 0, 0, 1)); continue; }
                if (NeverLaunched(p.Finding.State)) { res.Add(($"{p.Item.Name}: skipped, not opened ({p.Finding.Detail})", 0, 1, 0)); continue; }
                var r = _launcher.Launch(p.Item, p.Launch);
                if (r.Status == LaunchStatus.Started)
                    res.Add(($"{p.Item.Name}: open request sent (not proof it loaded)" + (p.Dup == OpenState.Open ? "; was already open (confirmed), so this may be a duplicate" : p.Dup == OpenState.Unknown ? "; could not tell if it was already open, so it may be a duplicate" : ""), 1, 0, 0));
                else res.Add(($"{p.Item.Name}: failed ({r.Detail ?? r.Status.ToString()})", 0, 0, 0));
            } }
            catch (Exception e) { fault = e; } // keep the partial results, then surface the bug below
        }).ConfigureAwait(true);
        if (fault != null) foreach (var p in plan.Skip(res.Count)) res.Add(($"{p.Item.Name}: not attempted (stopped by an unexpected error)", 0, 0, 1));
        var lines = res;
        int ok = lines.Sum(l => l.Ok), refused = lines.Sum(l => l.Refused), notTried = lines.Sum(l => l.NotTried);
        foreach (var l in lines) OpenReport.Add(l.Line);
        if (ok > 0) Apply(c => c.TouchLastOpened(_openWsId)); // the card's "last opened" is set only after a real open request went out, on the workspace that was opened even if the selection moved since
        IsLaunching = false;
        if (fault != null) ExceptionDispatchInfo.Capture(fault).Throw();
        var failed = plan.Count - ok - refused - notTried;
        var parts = new List<string>();
        if (refused > 0) parts.Add($"{refused} skipped by policy");
        if (failed > 0) parts.Add($"{failed} could not be started");
        if (notTried > 0) parts.Add($"{notTried} not attempted because you cancelled; requests already sent cannot be undone");
        Status = parts.Count == 0 ? $"Open requests sent for {ok} {(ok == 1 ? "item" : "items")}"
            : $"Open requests sent for {ok} of {plan.Count} items ({string.Join(", ", parts)})";
    }
    // ---- capture (לכידה, settled Nov 12 07:14-07:45): scan what is open now, show it read-only (checkbox, original name, address under it),
    // add to a NEW workspace (create form, items checked by default) or to an EXISTING one side by side. Staged additions are NOT saved until
    // שמור; the snapshot survives workspace changes and saves; X resets it. Duplicates: one warning per drag, one grouped dialog for add-all/selected. ----
    public enum CaptureStage { None, NewForm, PickWorkspace, SideBySide }
    /// <summary>One line of the capture list. Shows default details only: no actions menu, no editing (settled).</summary>
    public sealed class CaptureRow : ObservableBase
    {
        private readonly Action _changed; private bool _checked;
        public CaptureRow(CapturedItem item, bool isChecked, Action changed) { Item = item; _checked = isChecked; _changed = changed; }
        public CapturedItem Item { get; }
        public ItemType Type => Item.Type;
        public string Name => Item.Name;
        public string Target => Item.Target;
        public string Group => GroupOf(Item.Type);
        public string Label => "Select " + Item.Name;
        public bool IsChecked { get => _checked; set { if (_checked == value) return; _checked = value; Raise(nameof(IsChecked)); _changed(); } }
    }
    /// <summary>An item moved to the workspace side but not saved yet. Editable here (name, description, address, open-with); the capture side is never changed by these edits.</summary>
    public sealed class StagedRow : ObservableBase
    {
        private string _name, _description = "", _target, _openWith = "";
        public StagedRow(CapturedItem i) { Type = i.Type; OriginalName = i.Name; _name = i.Name; _target = i.Target; }
        public ItemType Type { get; }
        public string OriginalName { get; }
        public string Group => GroupOf(Type);
        public string Label => "Added: " + OriginalName;
        public string Name { get => _name; set => Set(ref _name, value ?? ""); }
        public string Description { get => _description; set => Set(ref _description, value ?? ""); }
        public string Target { get => _target; set => Set(ref _target, value ?? ""); }
        public string OpenWith { get => _openWith; set => Set(ref _openWith, value ?? ""); }
        /// <summary>פתח באמצעות does not apply to a program item (it is the program) - the menu entry hides.</summary>
        public bool IsProgram => Type == ItemType.Program;
    }
    /// <summary>One item already saved in the workspace on the side-by-side screen (read-only here).</summary>
    public sealed record CaptureExistingRow(string Name, string Target, string Group);
    /// <summary>One duplicate in the grouped dialog; the user ticks which ones to add anyway (none ticked by default).</summary>
    public sealed class DupChoice : ObservableBase
    {
        private bool _checked;
        public DupChoice(CaptureRow row) { Row = row; }
        public CaptureRow Row { get; }
        public string Label => "Add again: " + Row.Name;
        public bool IsChecked { get => _checked; set => Set(ref _checked, value); }
    }

    public ObservableCollection<CaptureRow> CaptureRows { get; } = new();
    public ObservableCollection<string> CaptureNotes { get; } = new();
    public ObservableCollection<Workspace> CaptureWorkspaceResults { get; } = new();
    public ObservableCollection<CaptureExistingRow> CaptureWorkspaceItems { get; } = new();
    public ObservableCollection<StagedRow> StagedRows { get; } = new();
    public ObservableCollection<DupChoice> CaptureDupChoices { get; } = new();
    private CaptureStage _captureStage = CaptureStage.None; private string _capWsId = "";
    public CaptureStage CaptureStageNow { get => _captureStage; private set { Set(ref _captureStage, value); Raise(nameof(CaptureOpen)); Raise(nameof(IsCaptureNew)); Raise(nameof(IsCapturePick)); Raise(nameof(IsCaptureSide)); Raise(nameof(CaptureDirty)); RaiseCommands(); } }
    public bool CaptureOpen => _captureStage != CaptureStage.None;
    public bool IsCaptureNew => _captureStage == CaptureStage.NewForm;
    public bool IsCapturePick => _captureStage == CaptureStage.PickWorkspace;
    public bool IsCaptureSide => _captureStage == CaptureStage.SideBySide;
    public bool CaptureEmpty => CaptureOpen && !IsScanning && CaptureRows.Count == 0;
    /// <summary>True while a scan runs in the background; the capture screen is already up and shows a scanning state.</summary>
    public bool IsScanning { get => _isScanning; private set { Set(ref _isScanning, value); Raise(nameof(CaptureEmpty)); RaiseCommands(); } }
    private bool _isScanning;
    /// <summary>The in-flight background scan; tests await it. Null until the first capture entry.</summary>
    public Task? CurrentScan { get; private set; }
    /// <summary>Hard bound for one scan: a source stuck in COM/UI Automation is abandoned after this, never waited on forever.</summary>
    public TimeSpan ScanTimeout { get; set; } = TimeSpan.FromSeconds(10);
    /// <summary>Anti-flicker floor so the scanning state is actually visible (and steady) on fast scans.</summary>
    public TimeSpan ScanMinVisible { get; set; } = TimeSpan.FromMilliseconds(700);
    private int _scanGen;
    private CancellationTokenSource? _scanCts;
    /// <summary>Background Scan() calls that have not returned yet (hung or just slow). A stuck COM/UIA call cannot be aborted, only
    /// abandoned - so abandonment is bounded: while <see cref="MaxOpenScans"/> scans have not come back, no new one starts.</summary>
    private int _openScans;
    private const int MaxOpenScans = 2;
    /// <summary>Without ticked rows the button adds everything; with ticked rows it becomes הוסף נבחרים (settled).</summary>
    public string CaptureAddLabel => CaptureRows.Any(r => r.IsChecked) ? "הוסף נבחרים" : "הוסף הכל";
    public string CaptureWorkspaceName => _current.Workspaces.FirstOrDefault(w => w.Id == _capWsId)?.Name ?? "";
    public string CaptureSearch { get => _captureSearch; set { Set(ref _captureSearch, value ?? ""); RefreshCaptureResults(); } }
    private string _captureSearch = "";
    /// <summary>Unsaved capture work: staged additions on the side-by-side screen, or a filled-in new-workspace form.</summary>
    public bool CaptureDirty => (_captureStage == CaptureStage.SideBySide && StagedRows.Count > 0) || (_captureStage == CaptureStage.NewForm && CreateDirty);
    public bool UnsavedForCapture { get => _unsavedForCapture; private set => Set(ref _unsavedForCapture, value); }
    private bool _unsavedForCapture;
    public bool CaptureDupSinglePending { get => _dupSingle != null; }
    public string CaptureDupSingleMessage { get => _dupSingleMessage; private set => Set(ref _dupSingleMessage, value); }
    private string _dupSingleMessage = ""; private CaptureRow? _dupSingle;
    public bool CaptureDupGroupPending { get => _dupGroup; private set { Set(ref _dupGroup, value); RaiseCommands(); } }
    private bool _dupGroup;
    public ICommand CaptureNewCommand { get; private set; } = null!;
    public ICommand CaptureExistingCommand { get; private set; } = null!;
    public ICommand AddFromOpenCommand { get; private set; } = null!;
    public ICommand CapturePickWorkspaceCommand { get; private set; } = null!;
    public ICommand CaptureAddCommand { get; private set; } = null!;
    public ICommand CaptureDropCommand { get; private set; } = null!;
    public ICommand CaptureSaveCommand { get; private set; } = null!;
    public ICommand CaptureBackCommand { get; private set; } = null!;
    public ICommand CaptureCloseCommand { get; private set; } = null!;
    public ICommand CaptureCreateCommand { get; private set; } = null!;
    public ICommand RemoveStagedCommand { get; private set; } = null!;
    public ICommand StagedCopyAddressCommand { get; private set; } = null!;
    public ICommand StagedMoveUpCommand { get; private set; } = null!;
    public ICommand StagedMoveDownCommand { get; private set; } = null!;
    public ICommand StagedOpenWithCommand { get; private set; } = null!;
    public ICommand StagedEditAddressCommand { get; private set; } = null!;
    public ICommand ConfirmCaptureDupSingleCommand { get; private set; } = null!;
    public ICommand CancelCaptureDupSingleCommand { get; private set; } = null!;
    public ICommand ConfirmCaptureDupGroupCommand { get; private set; } = null!;
    public ICommand CancelCaptureDupGroupCommand { get; private set; } = null!;
    private bool CaptureIdle => !_dupGroup && _dupSingle == null;
    private void InitCaptureCommands()
    {
        CaptureNewCommand = Cmd(_ => StartCapture(CaptureStage.NewForm, true), _ => !ReadOnly && !CaptureOpen && InOverview);
        CaptureExistingCommand = Cmd(_ => StartCapture(CaptureStage.PickWorkspace, false), _ => !ReadOnly && !CaptureOpen && InOverview);
        AddFromOpenCommand = Cmd(_ => StartCapture(CaptureStage.SideBySide, false, Selected!.Id), _ => !ReadOnly && !CaptureOpen && InDetails && Selected != null);
        CapturePickWorkspaceCommand = Cmd(p => EnterSideBySide(((Workspace)p!).Id), p => p is Workspace && _captureStage == CaptureStage.PickWorkspace);
        CaptureAddCommand = Cmd(_ => AddCaptured(), _ => IsCaptureSide && CaptureRows.Count > 0 && CaptureIdle && !ReadOnly);
        CaptureDropCommand = Cmd(p => DropCaptured((CaptureRow)p!), p => p is CaptureRow && IsCaptureSide && CaptureIdle && !ReadOnly);
        CaptureSaveCommand = Cmd(_ => SaveStaged(), _ => IsCaptureSide && StagedRows.Count > 0 && CaptureIdle && !ReadOnly);
        CaptureBackCommand = Cmd(_ => CaptureBack(), _ => IsCaptureSide && CaptureIdle);
        CaptureCloseCommand = Cmd(_ => CaptureClose(), _ => CaptureOpen && CaptureIdle);
        CaptureCreateCommand = Cmd(_ => CreateFromCapture(), _ => IsCaptureNew && NewName.Length > 0 && !ReadOnly && !IsScanning);
        RemoveStagedCommand = Cmd(p => { StagedRows.Remove((StagedRow)p!); Raise(nameof(CaptureDirty)); RaiseCommands(); }, p => p is StagedRow && IsCaptureSide);
        // Settled capture rule: a staged item offers the FULL item action set (מחיקה, העתק כתובת, עריכת כתובת, פתח באמצעות,
        // הזז למעלה/למטה; שיתוף לסביבה אחרת stays visible-disabled while that action is undefined) with staged semantics:
        // edits and a chosen default stay staged until שמור, and the source capture list is never changed.
        StagedCopyAddressCommand = Cmd(p => { CopyAddressRequested?.Invoke(((StagedRow)p!).Target); Status = "Address copied"; }, p => p is StagedRow && IsCaptureSide);
        // Enabled at the group edge exactly like the details screen's MoveItemUp/DownCommand: the four-group boundary is
        // enforced inside MoveStaged as a no-op, not by disabling the entry.
        StagedMoveUpCommand = Cmd(p => MoveStaged((StagedRow)p!, -1), p => p is StagedRow && IsCaptureSide && !ReadOnly);
        StagedMoveDownCommand = Cmd(p => MoveStaged((StagedRow)p!, 1), p => p is StagedRow && IsCaptureSide && !ReadOnly);
        StagedEditAddressCommand = Cmd(p => BeginAddressEditStaged((StagedRow)p!), p => p is StagedRow && IsCaptureSide && !ReadOnly && !ItemEditDirty && !AddressEditDirty);
        StagedOpenWithCommand = Cmd(p => BeginOpenWithStaged((StagedRow)p!), p => p is StagedRow && IsCaptureSide && CaptureIdle && !ReadOnly);
        ConfirmCaptureDupSingleCommand = Cmd(_ => { var r = _dupSingle!; _dupSingle = null; Raise(nameof(CaptureDupSinglePending)); Stage(r); }, _ => _dupSingle != null);
        CancelCaptureDupSingleCommand = Cmd(_ => { _dupSingle = null; Raise(nameof(CaptureDupSinglePending)); Status = "Not added, nothing was changed"; RaiseCommands(); }, _ => _dupSingle != null);
        ConfirmCaptureDupGroupCommand = Cmd(_ => { foreach (var c in CaptureDupChoices.Where(c => c.IsChecked).ToList()) Stage(c.Row); CaptureDupChoices.Clear(); CaptureDupGroupPending = false; }, _ => _dupGroup);
        CancelCaptureDupGroupCommand = Cmd(_ => { CaptureDupChoices.Clear(); CaptureDupGroupPending = false; Status = "Duplicates not added"; }, _ => _dupGroup);
    }
    private void OnCaptureCheckChanged() => Raise(nameof(CaptureAddLabel));
    private void StartCapture(CaptureStage stage, bool checkedByDefault, string? wsId = null)
    {
        CaptureRows.Clear(); CaptureNotes.Clear();
        StagedRows.Clear(); CaptureDupChoices.Clear(); _dupSingle = null; CaptureDupGroupPending = false; CaptureSearch = "";
        Raise(nameof(CaptureAddLabel)); Raise(nameof(CaptureEmpty)); Raise(nameof(CaptureDupSinglePending));
        // The screen opens right away; the scan itself runs off the UI thread, so a stuck COM/UIA call can never freeze the window.
        if (wsId != null) EnterSideBySide(wsId);
        else { RefreshCaptureResults(); CaptureStageNow = stage; }
        CurrentScan = RunScanAsync(checkedByDefault);
    }
    /// <summary>Background scan: bounded by <see cref="ScanTimeout"/>, cancelled by X or superseded by a newer scan (late results are
    /// discarded, never applied), with a visible scanning state. Unexpected faults are not swallowed: they reach <see cref="UnexpectedError"/>.
    /// Unlike the open flow there is no Guard generation here; <see cref="_scanGen"/> plays that role for scans.</summary>
    private async Task RunScanAsync(bool checkedByDefault)
    {
        var gen = ++_scanGen;
        try { _scanCts?.Cancel(); } catch (ObjectDisposedException) { }
        _scanCts?.Dispose(); var cts = new CancellationTokenSource(); _scanCts = cts;
        IsScanning = true; Status = "Scanning open items...";
        CaptureScan scan;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        if (Volatile.Read(ref _openScans) >= MaxOpenScans)
        {
            scan = new CaptureScan(Array.Empty<CapturedItem>(), new[] { "Earlier scans never came back (a stuck call cannot be aborted), so no new scan was started; items can still be added by hand" });
        }
        else
        {
            Interlocked.Increment(ref _openScans);
            try { scan = await CaptureScanning.BoundedAsync(_capturer, ScanTimeout, cts.Token, () => Interlocked.Decrement(ref _openScans)); }
            catch (OperationCanceledException) { return; } // X or a newer scan: nothing to apply
            catch (Exception e) when (e is IOException or InvalidOperationException or UnauthorizedAccessException or NotSupportedException)
            { scan = new CaptureScan(Array.Empty<CapturedItem>(), new[] { "Scan failed: " + e.GetType().Name }); }
            catch (Exception e)
            {
                if (gen == _scanGen) { IsScanning = false; Status = "Scan stopped by an unexpected error: " + e.GetType().Name; }
                var h = UnexpectedError; if (h is null) throw; h(e); return;
            }
        }
        if (gen != _scanGen || cts.IsCancellationRequested) return;
        var floor = ScanMinVisible - sw.Elapsed;
        if (floor > TimeSpan.Zero)
        {
            try { await Task.Delay(floor, cts.Token); } catch (OperationCanceledException) { return; }
            if (gen != _scanGen) return;
        }
        foreach (var i in scan.Items) CaptureRows.Add(new CaptureRow(i, checkedByDefault, OnCaptureCheckChanged));
        foreach (var n in scan.Notes) CaptureNotes.Add(n);
        IsScanning = false;
        Raise(nameof(CaptureAddLabel)); RaiseCommands();
        Status = scan.Items.Count == 0 ? "Nothing open was detected" : $"{scan.Items.Count} open {(scan.Items.Count == 1 ? "item" : "items")} detected";
    }
    private void RefreshCaptureResults()
    {
        CaptureWorkspaceResults.Clear();
        foreach (var w in Workspaces.Where(w => _captureSearch.Length == 0 || w.Name.Contains(_captureSearch, StringComparison.OrdinalIgnoreCase) || w.EffectiveCategory.Contains(_captureSearch, StringComparison.OrdinalIgnoreCase)))
            CaptureWorkspaceResults.Add(w);
    }
    private void EnterSideBySide(string wsId)
    {
        _capWsId = wsId; StagedRows.Clear(); RefreshCaptureWorkspaceItems(); Raise(nameof(CaptureWorkspaceName)); CaptureStageNow = CaptureStage.SideBySide;
    }
    private void RefreshCaptureWorkspaceItems()
    {
        CaptureWorkspaceItems.Clear();
        var w = _current.Workspaces.FirstOrDefault(x => x.Id == _capWsId); if (w is null) return;
        foreach (var m in w.Items.Select((m, i) => (m, i)).OrderBy(x => _current.Items.TryGetValue(x.m.ItemId, out var g) ? GroupRank(g.Type) : 4).ThenBy(x => x.i))
            if (_current.Items.TryGetValue(m.m.ItemId, out var it)) CaptureWorkspaceItems.Add(new CaptureExistingRow(m.m.Name ?? it.Name, it.Target, GroupOf(it.Type)));
    }
    private bool IsCaptureDuplicate(CaptureRow r) =>
        new Catalog(_current).HasTarget(_capWsId, r.Type, r.Target) || StagedRows.Any(s => s.Type == r.Type && s.Target == r.Target);
    private void Stage(CaptureRow r) { StagedRows.Add(new StagedRow(r.Item)); Raise(nameof(CaptureDirty)); RaiseCommands(); }
    /// <summary>One item dragged (or dropped by the view) onto the workspace side: a duplicate gets its own warning for that one drag.</summary>
    public void DropCaptured(CaptureRow r)
    {
        if (!IsCaptureSide || !CaptureIdle) return;
        if (IsCaptureDuplicate(r))
        {
            _dupSingle = r; CaptureDupSingleMessage = $"'{r.Name}' is already in this workspace (or already added here). Add it again as a separate instance?";
            Raise(nameof(CaptureDupSinglePending)); RaiseCommands(); return;
        }
        Stage(r);
    }
    /// <summary>הוסף הכל / הוסף נבחרים: new addresses are staged at once; every duplicate goes into ONE grouped dialog where the user picks which to add.</summary>
    private void AddCaptured()
    {
        var ticked = CaptureRows.Where(r => r.IsChecked).ToList();
        var rows = ticked.Count > 0 ? ticked : CaptureRows.ToList();
        var dups = rows.Where(IsCaptureDuplicate).ToList();
        foreach (var r in rows.Except(dups)) Stage(r);
        if (dups.Count > 0) { CaptureDupChoices.Clear(); foreach (var d in dups) CaptureDupChoices.Add(new DupChoice(d)); CaptureDupGroupPending = true; }
        Status = $"{rows.Count - dups.Count} added on screen, not saved yet" + (dups.Count > 0 ? $"; {dups.Count} already in the workspace need your choice" : "");
    }
    /// <summary>Staged reorder mirrors the details screen: הזז swaps with the next staged row IN THE SAME display
    /// group; at the group's edge it does nothing (same boundary rule as Catalog.MoveInstance).</summary>
    private void MoveStaged(StagedRow row, int delta)
    {
        var i = StagedRows.IndexOf(row); if (i < 0 || delta == 0) return;
        var g = row.Group; var step = Math.Sign(delta); var j = i + step;
        while (j >= 0 && j < StagedRows.Count && StagedRows[j].Group != g) j += step;
        if (j < 0 || j >= StagedRows.Count) return;
        StagedRows.Move(i, j); RaiseCommands();
    }
    /// <summary>פתח באמצעות on a staged row: the same popup a saved item gets, but a chosen default lands on the staged
    /// row's open-with field (written to the store only on שמור) and Go opens the item straight from the staged values.</summary>
    private void BeginOpenWithStaged(StagedRow row)
    {
        _openWithRow = null; _openWithStaged = row;
        OpenWithProgram = row.OpenWith;
        OpenWithSetDefault = row.OpenWith.Trim().Length > 0;
        OpenWithOpen = true; Raise(nameof(OpenWithLabel));
    }
    private void CloseOpenWith() { OpenWithOpen = false; _openWithRow = null; _openWithStaged = null; }
    private bool SaveStaged()
    {
        var wsId = _capWsId; var staged = StagedRows.ToList();
        foreach (var s in staged)
        {
            var ow = s.OpenWith.Trim(); LaunchConfig? launch = ow.Length == 0 ? null : s.Type == ItemType.Link ? new LaunchConfig(Browser: ow) : new LaunchConfig(OpenWith: ow);
            var err = Catalog.ValidateTarget(s.Type, s.Target) ?? Catalog.ValidateDescription(s.Description) ?? Catalog.ValidateLaunch(s.Type, launch);
            if (err != null) { Status = $"Not saved ({s.OriginalName}): " + err; return false; }
        }
        var ok = Apply(c =>
        {
            foreach (var s in staged)
            {
                var ow = s.OpenWith.Trim(); LaunchConfig? launch = ow.Length == 0 ? null : s.Type == ItemType.Link ? new LaunchConfig(Browser: ow) : new LaunchConfig(OpenWith: ow);
                var it = c.GetOrAddItem(s.Type, s.OriginalName, s.Target);
                var nm = string.IsNullOrWhiteSpace(s.Name) || s.Name == it.Name ? null : s.Name;
                c.AddToWorkspace(wsId, it.Id, launch, nm, s.Description);
            }
        });
        if (!ok) return false;
        StagedRows.Clear(); RefreshCaptureWorkspaceItems(); Raise(nameof(CaptureDirty)); RaiseCommands();
        Status = $"Saved {staged.Count} {(staged.Count == 1 ? "item" : "items")} to the workspace; the capture list stays open";
        return true;
    }
    private bool CreateFromCapture()
    {
        var n = NewName; var d = NewWorkspaceDescription; var cat = NewWorkspaceCategory; string? id = null;
        var chosen = CaptureRows.Where(r => r.IsChecked).Select(r => r.Item).ToList();
        if (string.IsNullOrWhiteSpace(n)) { Status = "Not created: workspace name is required"; return false; }
        var ok = Apply(c =>
        {
            id = c.CreateWorkspace(n, d).Id; c.SetCategory(id, cat);
            foreach (var i in chosen) { var it = c.GetOrAddItem(i.Type, i.Name, i.Target); c.AddToWorkspace(id, it.Id); }
        });
        if (!ok) return false;
        NewName = ""; NewWorkspaceDescription = ""; NewWorkspaceCategory = Workspace.DefaultCategory;
        ResetCapture();
        Selected = Workspaces.FirstOrDefault(w => w.Id == id);
        LoadDetails(); InDetails = true;
        Status = $"Workspace created with {chosen.Count} captured {(chosen.Count == 1 ? "item" : "items")}";
        return true;
    }
    private bool CaptureGuardSave()
    {
        if (IsCaptureNew)
        {
            if (string.IsNullOrWhiteSpace(NewName)) { UnsavedOpen = false; _exitAction = () => { }; Status = "Not saved: workspace name is required"; return false; }
            return CreateFromCapture();
        }
        return SaveStaged();
    }
    private void DiscardCaptureChanges()
    {
        StagedRows.Clear();
        if (IsCaptureNew) { NewName = ""; NewWorkspaceDescription = ""; NewWorkspaceCategory = Workspace.DefaultCategory; }
        Raise(nameof(CaptureDirty));
    }
    private void CaptureBack()
    {
        if (StagedRows.Count > 0) { MarkUnsavedHost(false, false, false, false, true); _exitAction = BackToPick; UnsavedOpen = true; return; }
        BackToPick();
    }
    private void BackToPick() { StagedRows.Clear(); _capWsId = ""; CaptureSearch = ""; RefreshCaptureResults(); CaptureStageNow = CaptureStage.PickWorkspace; }
    private void CaptureClose()
    {
        if (CaptureDirty) { MarkUnsavedHost(false, false, false, false, true); _exitAction = ResetCapture; UnsavedOpen = true; return; }
        ResetCapture();
    }
    /// <summary>X: the capture list is dropped; opening capture again scans what is open at that moment. What was already saved stays saved.</summary>
    private void ResetCapture()
    {
        _scanGen++; try { _scanCts?.Cancel(); } catch (ObjectDisposedException) { } // a scan still in flight is discarded, never applied
        IsScanning = false;
        CaptureRows.Clear(); CaptureNotes.Clear(); StagedRows.Clear(); CaptureDupChoices.Clear(); CaptureWorkspaceItems.Clear(); _dupSingle = null; _capWsId = "";
        CaptureDupGroupPending = false; CaptureSearch = "";
        Raise(nameof(CaptureDupSinglePending)); Raise(nameof(CaptureEmpty)); Raise(nameof(CaptureAddLabel));
        CaptureStageNow = CaptureStage.None;
    }

}
