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
    private Workspace? _selected; private string _status = ""; private bool _readOnly;

    public ObservableCollection<Workspace> Workspaces { get; } = new();
    public ObservableCollection<ItemRow> Items { get; } = new();
    private readonly CommandRegistry _commands = new();
    private RelayCommand Cmd(Action<object?> run, Func<object?, bool> can) => _commands.Add(run, can);
    private void RaiseCommands() { _commands.RaiseAll(); Raise(nameof(OpenDecisionPending)); Raise(nameof(ShowOpenPanel)); Raise(nameof(ConfirmLabel)); Raise(nameof(DuplicatePending)); Raise(nameof(CanBackToOpeningSelection)); }
    /// <summary>Opens the details screen for a workspace (from its card in the overview).</summary>
    public ICommand OpenDetailsCommand { get; }
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
    public event Action? FocusAddRequested;
    /// <summary>The empty-workspace dialog's button that leads to the details screen (to add items).</summary>
    public ICommand GoToDetailsCommand { get; }
    public event Action<string>? CopyAddressRequested;
    public event Action? PickImageRequested;
    public event Action? PickCreateImageRequested;
    /// <summary>שיתוף לסביבה אחרת is shown in the actions menu only when another workspace exists (the settled display condition);
    /// its popup is still undefined (הפרטים טרם הוגדרו), so the entry stays disabled.</summary>
    public bool HasOtherWorkspaces => Workspaces.Count > 1;
    /// <summary>Duplicate-add confirmation: visible while an add waits for the user to approve a second instance of the same target.</summary>
    public bool DuplicatePending => _manualAdd.DuplicatePending;
    private readonly ManualAddSession _manualAdd = new();
    /// <summary>Text shown next to the duplicate confirm/cancel buttons.</summary>
    public string DuplicateMessage { get => _manualAdd.DuplicateMessage; private set { if (_manualAdd.DuplicateMessage != value) { _manualAdd.DuplicateMessage = value; Raise(nameof(DuplicateMessage)); } } }

    /// <summary>Set instead of opening when the workspace has no items (the spec's דיאלוג סביבת עבודה ריקה).</summary>
    public bool EmptyWorkspaceOpen { get => _emptyOpen; private set { Set(ref _emptyOpen, value); RaiseCommands(); } }
    private bool _emptyOpen;
    public ICommand CloseEmptyWorkspaceCommand { get; }
    /// <summary>The shared unsaved-changes exit dialog (דיאלוג יציאה עם שינויים שלא נשמרו).</summary>
    private readonly UnsavedExitSession _unsavedExit = new();
    public bool UnsavedOpen { get => _unsavedExit.IsOpen; private set { if (_unsavedExit.IsOpen != value) { _unsavedExit.IsOpen = value; Raise(nameof(UnsavedOpen)); } RaiseCommands(); } }
    /// <summary>Window-close guard: false means the close was cancelled and the unsaved-changes dialog opened instead.</summary>
    public bool RequestAppClose()
    {
        if (CaptureDirty) { MarkUnsavedHost(false, false, false, false, true); _unsavedExit.Continuation = () => AppCloseApproved?.Invoke(); UnsavedOpen = true; return false; }
        if (DetailsDirty) { MarkUnsavedHost(false, false, false); _unsavedExit.Continuation = () => AppCloseApproved?.Invoke(); UnsavedOpen = true; return false; }
        if (CreateDirty) { MarkUnsavedHost(true, false, false); _unsavedExit.Continuation = () => AppCloseApproved?.Invoke(); UnsavedOpen = true; return false; }
        if (ItemEditDirty) { MarkUnsavedHost(false, true, false); _unsavedExit.Continuation = () => AppCloseApproved?.Invoke(); UnsavedOpen = true; return false; }
        if (AddressEditDirty) { MarkUnsavedHost(false, false, true); _unsavedExit.Continuation = () => AppCloseApproved?.Invoke(); UnsavedOpen = true; return false; }
        if (DesignDirty) { MarkUnsavedHost(false, false, false, true); _unsavedExit.Continuation = () => AppCloseApproved?.Invoke(); UnsavedOpen = true; return false; }
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
            if (string.IsNullOrWhiteSpace(NewName)) { UnsavedOpen = false; _unsavedExit.ClearContinuation(); Status = "Not saved: workspace name is required"; return; }
            Create();
            if (CreateDirty) { UnsavedOpen = false; _unsavedExit.ClearContinuation(); return; } // creation failed; keep editing
        }
        if (DetailsDirty) { if (!SaveDetails()) return; }
        UnsavedOpen = false; _unsavedExit.Continue();
    }
    /// <summary>Fired when the user chose שמור וצא or צא בלי לשמור for a window close.</summary>
    public event Action? AppCloseApproved;
    public IReadOnlyList<ItemType> ItemTypes { get; } = new[] { ItemType.Link, ItemType.Program, ItemType.File, ItemType.Folder };

    public MainViewModel(IStoreRepository repo, IItemLauncher launcher, IItemPreflight? preflight = null, IOpenStateDetector? detector = null, IOpenItemCapturer? capturer = null, IFileRevealer? fileRevealer = null)
    {
        _fileRevealer = fileRevealer;
        _repo = repo; _launcher = launcher; _preflight = preflight; _detector = detector ?? new NoOpenStateDetection(); _capturer = capturer ?? new NoCapture();
        _categoryEdit.PropertyChanged += OnCategorySessionChanged;
        InitCaptureCommands();
        InitGlobalEnvCommands();
        InitAllItemsCommands();
        InitOpeningCommands();
        // Feature initializers stay interleaved to preserve the existing command notification order.
        InitWorkspaceCreateCommands();
        // Workspace deletion remains unexposed; this is navigation only.
        OpenDetailsCommand = Cmd(p => OpenDetails((Workspace)p!), p => p is Workspace);
        InitWorkspaceCardOpeningCommand();
        AddItemToCommand = Cmd(p => { OpenDetails((Workspace)p!); FocusAddRequested?.Invoke(); }, p => p is Workspace);
        BackCommand = Cmd(_ => Back(), _ => InDetails);
        SaveDetailsCommand = Cmd(_ => SaveDetails(), _ => InDetails && DetailsDirty && !ReadOnly);
        SaveAndExitCommand = Cmd(_ => SaveAndExit(), _ => UnsavedOpen);
        ExitWithoutSavingCommand = Cmd(_ => { UnsavedOpen = false; if (UnsavedForCapture) { DiscardCaptureChanges(); } else if (UnsavedForItemEdit) { ItemEditOpen = false; } else if (UnsavedForAddress) { AddressEditOpen = false; } else if (UnsavedForCreate) { ResetCreateForm(); } else if (UnsavedForDesign) { DesignEditOpen = false; } else { _workspaceDetails.SuppressDirty = true; LoadDetails(); _workspaceDetails.SuppressDirty = false; } _unsavedExit.Continue(); }, _ => UnsavedOpen);
        CloseUnsavedCommand = Cmd(_ => { UnsavedOpen = false; _unsavedExit.ClearContinuation(); }, _ => UnsavedOpen);
        InitManualAddFocusCommand();
        InitSingleItemOpeningCommand();
        InitCopyAddressCommand();
        InitShowInFolderCommand();
        InitItemEditorCommands();
        InitAddressEditorCommands();
        InitOpenWithEditorCommands();
        InitItemReorderCommands();
        InitCardDesignEntryCommands();
        InitWorkspaceColorCommands();
        InitWorkspaceCreateDesignCommands();
        InitCardDesignEditCommands();
        InitCategoryEntryCommand();
        InitResourcePickerCommands();
        InitCategoryEditCommands();
        InitRemoveItemCommand();
        InitManualAddCommands();
        CloseEmptyWorkspaceCommand = Cmd(_ => EmptyWorkspaceOpen = false, _ => _emptyOpen);
        GoToDetailsCommand = Cmd(_ => { EmptyWorkspaceOpen = false; if (Selected != null) { LoadDetails(); InDetails = true; } }, _ => _emptyOpen && Selected != null);
        InitWorkspaceOpeningCommands();
        Reload();
    }


    public Workspace? Selected { get => _selected; set { ClearPending(); ClearDup(); EmptyWorkspaceOpen = false; if (!ItemEditDirty) ItemEditOpen = false; if (!AddressEditDirty) AddressEditOpen = false; OpenWithOpen = false; Set(ref _selected, value); Raise(nameof(SelectedIsDefault)); Raise(nameof(DefaultToggleLabel)); Raise(nameof(EditingItemInstanceId)); if (InDetails && !DetailsDirty) LoadDetails(); RefreshItems(); Raise(nameof(ShowEmptyItemsMessage)); Raise(nameof(CreatedAtText)); RaiseCommands(); } }
    // An ordinary refresh (Apply -> Show re-selects the same workspace) must not silently drop an open
    // unsaved item/address edit (PR19 review: edits disappeared on unrelated mutations). A dirty form stays
    // open with its fields. Safety does not rest on the selection staying put: saves validate the
    // workspace+instance captured when the form opened, and dirty forms block workspace switches and row
    // deletion, so a preserved form can neither crash nor silently retarget.
    public string Status { get => _status; private set { Set(ref _status, value); PresentLegacyStatus(value); } }
    public bool ReadOnly { get => _readOnly; private set { Set(ref _readOnly, value); Raise(nameof(SaveBlocked)); Raise(nameof(CanChooseWorkspaceColor)); RaiseCommands(); } }
    public ItemType NewItemType { get => _manualAdd.Type; set { if (_manualAdd.Type != value) { _manualAdd.Type = value; Raise(nameof(NewItemType)); } Raise(nameof(NewItemTypeIndex)); ClearDup(); } }
    public string NewTarget { get => _manualAdd.Target; set { if (_manualAdd.Target != value) { _manualAdd.Target = value; Raise(nameof(NewTarget)); }
        if (Uri.TryCreate(value, UriKind.Absolute, out var pasted) && (pasted.Scheme == Uri.UriSchemeHttp || pasted.Scheme == Uri.UriSchemeHttps)) NewItemType = ItemType.Link;
        ClearDup(); RaiseCommands(); } }
    public static IReadOnlyList<string> ItemTypeLabels { get; } = new[] { "אתר", "קובץ", "תיקייה", "תוכנה" };
    public int NewItemTypeIndex { get => (int)NewItemType; set => NewItemType = (ItemType)value; }
    /// <summary>Optional local name for the instance in this workspace. Empty means the original (derived) name.</summary>
    public string NewItemName { get => _manualAdd.Name; set { var text = value ?? ""; if (_manualAdd.Name != text) { _manualAdd.Name = text; Raise(nameof(NewItemName)); } ClearDup(); } }
    /// <summary>Optional description typed in the add form. Empty means none.</summary>
    public string NewDescription { get => _manualAdd.Description; set { var text = value ?? ""; if (_manualAdd.Description != text) { _manualAdd.Description = text; Raise(nameof(NewDescription)); } ClearDup(); } }

    private void ClearDup(string? status = null) { if (!_manualAdd.DuplicatePending) return; _manualAdd.DuplicatePending = false; DuplicateMessage = ""; if (status != null) Status = status; Raise(nameof(DuplicatePending)); RaiseCommands(); }
    private readonly WorkspaceCreateDraft _workspaceCreate = new();
    public string NewName { get => _workspaceCreate.Name; set { if (_workspaceCreate.Name != value) { _workspaceCreate.Name = value; Raise(nameof(NewName)); } Raise(nameof(CreateDirty)); Raise(nameof(CreatePreview)); RaiseCommands(); } }
    public string NewWorkspaceDescription { get => _workspaceCreate.Description; set { var text = value ?? ""; if (_workspaceCreate.Description != text) { _workspaceCreate.Description = text; Raise(nameof(NewWorkspaceDescription)); } Raise(nameof(CreateDirty)); } }
    public string NewWorkspaceCategory { get => _workspaceCreate.Category; set { var text = value ?? Workspace.DefaultCategory; if (_workspaceCreate.Category != text) { _workspaceCreate.Category = text; Raise(nameof(NewWorkspaceCategory)); } Raise(nameof(CreateDirty)); Raise(nameof(CreatePreview)); } }
    public string NewWorkspaceColor { get => _workspaceCreate.Color; set { var text = value ?? ""; if (_workspaceCreate.Color != text) { _workspaceCreate.Color = text; _workspaceCreate.AdvancedColorToRemember = null; Raise(nameof(NewWorkspaceColor)); } Raise(nameof(CreateDirty)); Raise(nameof(CreatePreview)); RaiseCommands(); } }
    public string NewWorkspaceImagePath { get => _workspaceCreate.ImagePath; set { var text = value ?? ""; if (_workspaceCreate.ImagePath != text) { _workspaceCreate.ImagePath = text; Raise(nameof(NewWorkspaceImagePath)); } Raise(nameof(CreateDirty)); Raise(nameof(CreatePreview)); RaiseCommands(); } }
    // Keep host setter order and notifications on successful create/discard.
    private void ResetCreateForm() { _workspaceCreate.AdvancedColorToRemember = null; NewName = ""; NewWorkspaceDescription = ""; NewWorkspaceCategory = Workspace.DefaultCategory; NewWorkspaceColor = ""; NewWorkspaceImagePath = ""; }
    public bool CreateDirty => _workspaceCreate.IsDirty;
    /// <summary>True while the open unsaved-exit dialog guards the create form rather than the details screen.</summary>
    public bool UnsavedForCreate { get => _unsavedExit.ForCreate; private set { if (_unsavedExit.ForCreate != value) { _unsavedExit.ForCreate = value; Raise(nameof(UnsavedForCreate)); } } }
    /// <summary>True while the open unsaved-exit dialog guards the item edit form (טופס עריכת אייטם).</summary>
    public bool UnsavedForItemEdit { get => _unsavedExit.ForItemEdit; private set { if (_unsavedExit.ForItemEdit != value) { _unsavedExit.ForItemEdit = value; Raise(nameof(UnsavedForItemEdit)); } } }
    /// <summary>True while the open unsaved-exit dialog guards the address popup (פופאפ עריכת כתובת).</summary>
    public bool UnsavedForAddress { get => _unsavedExit.ForAddress; private set { if (_unsavedExit.ForAddress != value) { _unsavedExit.ForAddress = value; Raise(nameof(UnsavedForAddress)); } } }
    /// <summary>True while the open unsaved-exit dialog guards the card design popup (פופאפ עיצוב כרטיס).</summary>
    public bool UnsavedForDesign { get => _unsavedExit.ForDesign; private set { if (_unsavedExit.ForDesign != value) { _unsavedExit.ForDesign = value; Raise(nameof(UnsavedForDesign)); } } }
    public string UnsavedContextLabel => UnsavedForCreate ? "יצירת סביבה · יציאה" : UnsavedForItemEdit || UnsavedForAddress ? "עריכת אייטם · יציאה" : UnsavedForDesign ? "עיצוב כרטיס · יציאה" : UnsavedForCapture ? "לכידה · יציאה" : "פרטי הסביבה · יציאה";
    public string UnsavedContextMessage => UnsavedForCreate ? "בוצעו שינויים בסביבה החדשה שעדיין לא נשמרו." : UnsavedForItemEdit || UnsavedForAddress ? "בוצעו שינויים באייטם שעדיין לא נשמרו." : UnsavedForDesign ? "בוצעו שינויים בעיצוב הכרטיס שעדיין לא נשמרו." : UnsavedForCapture ? "בוצעו שינויים בלכידה שעדיין לא נשמרו." : "בוצעו שינויים בפרטי הסביבה שעדיין לא נשמרו.";
    private void MarkUnsavedHost(bool forCreate, bool forItemEdit, bool forAddress, bool forDesign = false, bool forCapture = false)
    {
        UnsavedForCapture = forCapture; UnsavedForCreate = forCreate; UnsavedForItemEdit = forItemEdit; UnsavedForAddress = forAddress; UnsavedForDesign = forDesign;
        Raise(nameof(UnsavedContextLabel)); Raise(nameof(UnsavedContextMessage));
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
    public Workspace CreatePreview => _workspaceCreate.Preview;
    public void ShowCreateScreen()
    {
        if (ReadOnly || CaptureOpen || InSettings || InGlobalEnv || IsChecking || IsLaunching || UnsavedOpen || InCreate) return;
        void Show() { InAllItems = false; ItemEditOpen = false; AddressEditOpen = false; OpenWithOpen = false; InDetails = false; InCreate = true; }
        if (DetailsDirty || ItemEditDirty || AddressEditDirty || DesignDirty)
        {
            MarkUnsavedHost(false, ItemEditDirty, AddressEditDirty, DesignDirty);
            _unsavedExit.Continuation = Show; UnsavedOpen = true; return;
        }
        Show();
    }
    public void LeaveCreateScreen()
    {
        if (!InCreate) return;
        if (CreateDirty)
        {
            MarkUnsavedHost(true, false, false);
            _unsavedExit.Continuation = () => InCreate = false; UnsavedOpen = true; return;
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

    private readonly WorkspaceDetailsDraft _workspaceDetails = new();
    public string EditName { get => _workspaceDetails.Name; set { var text = value ?? ""; if (_workspaceDetails.Name != text) { _workspaceDetails.Name = text; Raise(nameof(EditName)); } RaiseDirty(); } }
    public string EditCategory { get => _workspaceDetails.Category; set { var text = value ?? ""; if (_workspaceDetails.Category != text) { _workspaceDetails.Category = text; Raise(nameof(EditCategory)); } RaiseDirty(); } }
    public string EditDescription { get => _workspaceDetails.Description; set { var text = value ?? ""; if (_workspaceDetails.Description != text) { _workspaceDetails.Description = text; Raise(nameof(EditDescription)); } RaiseDirty(); } }
    public bool DetailsDirty => _workspaceDetails.IsDirty(InDetails, Selected);
    public string CreatedAtText => Selected?.CreatedAt is { } t ? t.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "unknown";
    /// <summary>Card display: last time the workspace was really opened, or never.</summary>
    public static string LastOpenedTextOf(Workspace w) => w.LastOpenedAt is { } t ? t.ToLocalTime().ToString("yyyy-MM-dd HH:mm") : "never";
    private void RaiseDirty() { Raise(nameof(DetailsDirty)); RaiseCommands(); }
    private void LoadDetails()
    {
        if (Selected is null) return;
        _workspaceDetails.Load(Selected);
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
        if (DetailsDirty) { MarkUnsavedHost(false, false, false); _unsavedExit.Continuation = () => { ItemEditOpen = false; AddressEditOpen = false; InDetails = false; }; UnsavedOpen = true; return; }
        if (ItemEditDirty) { MarkUnsavedHost(false, true, false); _unsavedExit.Continuation = () => { ItemEditOpen = false; AddressEditOpen = false; InDetails = false; }; UnsavedOpen = true; return; }
        if (AddressEditDirty) { MarkUnsavedHost(false, false, true); _unsavedExit.Continuation = () => { AddressEditOpen = false; InDetails = false; }; UnsavedOpen = true; return; }
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
        var type = _manualAdd.EffectiveType; var target = NewTarget;
        var wsId = Selected!.Id; var desc = NewDescription; var name = NewItemName;
        var err = _manualAdd.ValidationError;
        if (err != null) { FieldError("add", err == Catalog.WebsiteTargetError ? err : "פרטי האייטם אינם תקינים. בדקו את הכתובת בהתאם לסוג, את השם ואת התיאור.", "Not added: " + err); return; }
        if (!force && new Catalog(_current).HasTarget(wsId, type, target))
        {
            _manualAdd.DuplicatePending = true; Raise(nameof(DuplicatePending)); RaiseCommands();
            DuplicateMessage = "This target is already in the workspace. Add it again as a separate instance with its own name and settings?";
            return;
        }
        _manualAdd.DuplicatePending = false; DuplicateMessage = ""; Raise(nameof(DuplicatePending));
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
        if (Apply(c => { id = c.CreateWorkspace(n, d).Id; c.SetCategory(id, cat); c.SetCardDesign(id, color.Length > 0 ? color : null, image.Length > 0 ? image : null); RememberAdvancedColor(c, _workspaceCreate.AdvancedColorToRemember, color); }))
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
