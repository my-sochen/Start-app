using StartApp.Core;
using System.Collections.ObjectModel;
using System.Windows.Input;

namespace StartApp.Presentation;

/// <summary>תצוגה menu state (approved 22.11 handoff): card size, sort, filter and template for the home
/// overview. Sorting orders, never hides; filters hide non-matching workspaces and show as removable chips
/// on the filter bar (several filters may run together, OR inside a dimension, AND across dimensions).
/// Color filter options are the custom colors currently in use on cards (the advanced-picker history store
/// does not exist yet); content-item filtering offers the actual items of one type across all workspaces.</summary>
public sealed partial class MainViewModel
{
    private readonly WorkspaceViewSession _workspaceView = new();

    /// <summary>The sorted and filtered projection of <see cref="Workspaces"/> shown on the home overview.</summary>
    public ObservableCollection<Workspace> DisplayWorkspaces { get; } = new();
    /// <summary>Active filters as chips, in a stable order: colors, categories, content item, contains.</summary>
    public ObservableCollection<FilterChip> FilterChips { get; } = new();
    /// <summary>Custom card colors currently in use, offered under "לפי צבע".</summary>
    public ObservableCollection<string> FilterColorOptions { get; } = new();

    public ViewSort CurrentSort { get => _workspaceView.Sort; set { SetViewCurrentSort(value); ApplyView(); } }
    public CardSizeOption CurrentCardSize { get => _workspaceView.CardSize; set => SetViewCurrentCardSize(value); }
    public ViewTemplate CurrentTemplate { get => _workspaceView.Template; set => SetViewCurrentTemplate(value); }
    private void SetViewCurrentSort(ViewSort value) { if (_workspaceView.Sort != value) { _workspaceView.Sort = value; Raise(nameof(CurrentSort)); } }
    private void SetViewCurrentCardSize(CardSizeOption value) { if (_workspaceView.CardSize != value) { _workspaceView.CardSize = value; Raise(nameof(CurrentCardSize)); } }
    private void SetViewCurrentTemplate(ViewTemplate value) { if (_workspaceView.Template != value) { _workspaceView.Template = value; Raise(nameof(CurrentTemplate)); } }
    private void SetViewFilterContainsText(string value) { if (_workspaceView.ContainsText != value) { _workspaceView.ContainsText = value; Raise(nameof(FilterContainsText)); } }
    /// <summary>סינון "מכיל...": free text; environments containing it stay (bonus item, approved as a good idea 02:34:10).</summary>
    public string FilterContainsText { get => _workspaceView.ContainsText; set { SetViewFilterContainsText(value); ApplyView(); } }

    public bool HasActiveFilters => _workspaceView.HasActiveFilters;
    /// <summary>Live counter on the filter bar, e.g. "מוצגת 2 מתוך 6".</summary>
    public string FilteredCountText => $"מוצגת {DisplayWorkspaces.Count} מתוך {Workspaces.Count}";
    /// <summary>Single-content-filter state (approved mock home-filter-program): the bar switches to the short form
    /// "מסונן: סביבות שמכילות X · N מתוך M" + "✕ ניקוי סינון" instead of chips.</summary>
    public bool IsSingleContentFilter => _workspaceView.IsSingleContentFilter;
    /// <summary>The pinned item's plain name for the short-form bar ("סביבות שמכילות Visual Studio Code").</summary>
    public string SingleContentFilterName => _workspaceView.ItemName;
    /// <summary>Short counter for the single-filter form, e.g. "2 מתוך 6".</summary>
    public string SingleFilterCountText => $"{DisplayWorkspaces.Count} מתוך {Workspaces.Count}";

    /// <summary>Card stats line (approved mocks 22.11): per-type counts in the fixed order אתרים · תוכנות · קבצים · תיקיות,
    /// e.g. "3 אתרים · 2 תוכנות · תיקייה"; zero-count types are omitted, a count of 1 shows the singular without a number
    /// ("תוכנה", "תיקייה"); empty: "עדיין אין אייטמים".</summary>
    public string StatsText(Workspace w)
    {
        if (w.Items.Count == 0) return "עדיין אין אייטמים";
        var counts = new Dictionary<ItemType, int>();
        foreach (var m in w.Items)
        {
            var t = _current.Items.TryGetValue(m.ItemId, out var it) ? it.Type : ItemType.Program;
            counts[t] = counts.TryGetValue(t, out var n) ? n + 1 : 1;
        }
        var parts = new List<string>();
        void Add(ItemType t, string singular, string plural)
        { if (counts.TryGetValue(t, out var n)) parts.Add(n == 1 ? singular : $"{n} {plural}"); }
        Add(ItemType.Link, "אתר", "אתרים"); Add(ItemType.Program, "תוכנה", "תוכנות");
        Add(ItemType.File, "קובץ", "קבצים"); Add(ItemType.Folder, "תיקייה", "תיקיות");
        return string.Join(" · ", parts);
    }

    /// <summary>The סביבת ברירת מחדל currently marked (null = "ללא סביבת ברירת מחדל"), set from אופציות > הגדר סביבה
    /// כברירת מחדל (approved 22.11) or the card/details actions (paths 2+3 spec landing).</summary>
    public string? DefaultWorkspaceId => _current.DefaultWorkspaceId;

    /// <summary>Details-screen state (path 3, approved 22.11): the quiet button label and the h1 green dot.</summary>
    public bool SelectedIsDefault => Selected is { } s && IsDefaultWorkspace(s);
    /// <summary>The quiet details button reads "הסר הגדרה כברירת מחדל" on the default, "הגדר כברירת מחדל" otherwise (owner wording 22.11).</summary>
    public string DefaultToggleLabel => SelectedIsDefault ? "הסר הגדרה כברירת מחדל" : "הגדר כברירת מחדל";

    /// <summary>Paths 2+3 (approved 22.11): one command from the card context menu and the details button; the parameter is
    /// the workspace. Setting with no existing default is immediate; replacing one opens the confirmation dialog first
    /// (owner decision 22.11 11:16). Every removal route asks for confirmation. The menu row label comes from DefaultMenuHeaderConverter.</summary>
    public ICommand ToggleDefaultForCommand => _toggleDefaultFor ??= Cmd(p =>
    { if (p is Workspace w) SetDefaultWorkspace(IsDefaultWorkspace(w) ? null : w.Id); }, _ => !ReadOnly);
    private ICommand? _toggleDefaultFor;

    /// <summary>Set or clear the סביבת ברירת מחדל (null = "ללא סביבת ברירת מחדל"). Owner behavior matrix 22.11:
    /// setting with NO existing default is immediate; setting while another workspace holds it first opens the
    /// confirmation dialog (ConfirmDefaultReplaceCommand applies, CancelDefaultReplaceCommand / ✕ / outside change
    /// nothing); every removal route asks for confirmation.</summary>
    public void SetDefaultWorkspace(string? workspaceId)
    {
        if (_current.DefaultWorkspaceId == workspaceId) return;
        if (_current.DefaultWorkspaceId is { Length: > 0 } cur && cur != workspaceId)
        {
            _defaultWorkspace.PendingId = workspaceId;
            DefaultConfirmOpen = true;
            Raise(nameof(DefaultConfirmOpen)); Raise(nameof(DefaultConfirmBody)); Raise(nameof(DefaultConfirmNotice)); Raise(nameof(DefaultConfirmTitle)); Raise(nameof(DefaultConfirmAcceptLabel)); RaiseCommands();
            return;
        }
        ApplyDefaultWorkspace(workspaceId);
    }

    private readonly DefaultWorkspaceSession _defaultWorkspace = new();
    private string DefaultName(string? id) => id is { Length: > 0 } && _current.Workspaces.FirstOrDefault(w => w.Id == id) is { } w ? w.Name : "";

    /// <summary>Replace-confirm dialog state (approved dialog 22.11): open flag, dynamic body naming the current and
    /// candidate workspaces, and the blue notice naming the candidate.</summary>
    public bool DefaultConfirmOpen { get => _defaultWorkspace.IsOpen; private set => _defaultWorkspace.IsOpen = value; }
    public string DefaultConfirmTitle => _defaultWorkspace.Title;
    public string DefaultConfirmAcceptLabel => _defaultWorkspace.AcceptLabel;
    public string DefaultConfirmBody => _defaultWorkspace.PendingId is null
        ? $"הסביבה '{DefaultName(_current.DefaultWorkspaceId)}' מוגדרת כיום כברירת מחדל ונפתחת אוטומטית עם הפעלת המחשב.\nלהגדיר ללא ברירת מחדל?"
        : $"הסביבה '{DefaultName(_current.DefaultWorkspaceId)}' מוגדרת כיום כברירת מחדל ונפתחת אוטומטית עם הפעלת המחשב.\nלהגדיר במקומה את '{DefaultName(_defaultWorkspace.PendingId)}'?";
    public string DefaultConfirmNotice => _defaultWorkspace.PendingId is null
        ? "אחרי האישור הנקודה הירוקה תוסר ולא תיפתח אף סביבה עם הפעלת המחשב. אפשר להגדיר ברירת מחדל שוב בכל עת."
        : $"אחרי האישור הנקודה הירוקה תעבור אל '{DefaultName(_defaultWorkspace.PendingId)}', והיא זו שתיפתח עם הפעלת המחשב.";

    /// <summary>Dialog confirm: move the green badge and fire the existing ~3s toast.</summary>
    public ICommand ConfirmDefaultReplaceCommand => _confirmDefaultReplace ??= Cmd(_ =>
    {
        if (!DefaultConfirmOpen || ReadOnly) return;
        var id = _defaultWorkspace.PendingId;
        CloseDefaultConfirm();
        ApplyDefaultWorkspace(id);
    }, _ => !ReadOnly);
    private ICommand? _confirmDefaultReplace;

    /// <summary>Dialog cancel (also ✕ and outside click): nothing changes.</summary>
    public ICommand CancelDefaultReplaceCommand => _cancelDefaultReplace ??= Cmd(_ => CloseDefaultConfirm(), _ => !ReadOnly);
    private ICommand? _cancelDefaultReplace;

    private void CloseDefaultConfirm()
    {
        _defaultWorkspace.Close();
        Raise(nameof(DefaultConfirmOpen)); RaiseCommands();
    }

    /// <summary>Persist the choice and refresh the cards so the green badge moves immediately. Exactly one workspace can
    /// hold it at a time. Confirms with the ~3s launch-style toast (approved wording 22.11).</summary>
    private void ApplyDefaultWorkspace(string? workspaceId)
    {
        if (!Apply(c => c.Store.DefaultWorkspaceId = workspaceId)) return;
        Raise(nameof(DefaultWorkspaceId)); Raise(nameof(SelectedIsDefault)); Raise(nameof(DefaultToggleLabel));
        if (workspaceId is { Length: > 0 } && _current.Workspaces.FirstOrDefault(w => w.Id == workspaceId) is { } w)
            ShowToast($"הסביבה '{w.Name}' הוגדרה כברירת מחדל · תיפתח עם הפעלת המחשב", false);
        else
            ShowToast("הוסרה ההגדרה כברירת מחדל", false);
    }

    /// <summary>True when this workspace is the one marked סביבת ברירת מחדל (Store.DefaultWorkspaceId): its card carries
    /// the 11px green-dot badge (approved 22.11 handoff). Exactly one card can match; none matches when no default is set.
    /// When filters hide that card the badge hides with it, because it renders on the card itself.</summary>
    public bool IsDefaultWorkspace(Workspace w) => _current.DefaultWorkspaceId is { Length: > 0 } id && w.Id == id;

    /// <summary>Card footer first line (approved mocks): "פתיחה אחרונה: היום, 09:40" / "אתמול, 16:00" / "18.11, 10:20";
    /// an empty workspace shows "סביבה ריקה · הוסף אייטם בפרטים" instead of a date.</summary>
    public string FooterLine1(Workspace w)
    {
        if (w.Items.Count == 0) return "סביבה ריקה · הוסף אייטם בפרטים";
        if (w.LastOpenedAt is not { } t) return "פתיחה אחרונה: עוד לא נפתחה";
        var l = t.ToLocalTime();
        var day = l.Date == DateTime.Today ? "היום" : l.Date == DateTime.Today.AddDays(-1) ? "אתמול" : l.ToString("dd.MM");
        return $"פתיחה אחרונה: {day}, {l:HH:mm}";
    }

    /// <summary>List-row last-opened text (approved 22.11 list mock): like FooterLine1 but an empty workspace shows
    /// plain "סביבה ריקה" - the card's " · הוסף אייטם בפרטים" hint is card-specific.</summary>
    public string FooterLine1List(Workspace w) => w.Items.Count == 0 ? "סביבה ריקה" : FooterLine1(w);

    /// <summary>Category choices for "לפי קטגוריה" (multi-select, OR): the built-ins and the user's custom categories.</summary>
    public IReadOnlyList<string> FilterCategoryOptions => Catalog.BuiltInCategories.Concat(_current.CustomCategories).ToList();

    /// <summary>The actual items of one type that appear in at least one workspace (לחיצה על סוג פותחת את הפריטים מאותו סוג מכל הסביבות).</summary>
    public IReadOnlyList<Item> ContentItemsOfType(ItemType type)
    {
        var used = new HashSet<string>(Workspaces.SelectMany(w => w.Items.Select(m => m.ItemId)));
        return _current.Items.Values.Where(i => i.Type == type && used.Contains(i.Id)).OrderBy(i => i.Name, StringComparer.CurrentCulture).ToList();
    }

    /// <summary>Hebrew dimension label for a content-type chip: תוכנות / אתרים / תיקיות / קבצים.</summary>
    public static string ContentTypeLabel(ItemType t) => t switch
    { ItemType.Link => "אתרים", ItemType.Folder => "תיקיות", ItemType.File => "קבצים", _ => "תוכנות" };

    /// <summary>The item the content filter is pinned to (null = no content filter), for the ✓ in the submenu.</summary>
    public string? CurrentContentItemId => _workspaceView.ItemId;

    private RelayCommand? _clearFilters;
    private RelayCommand? _removeChip;
    public ICommand ClearFiltersCommand => _clearFilters ??= Cmd(_ => ClearFilters(), _ => HasActiveFilters);
    public ICommand RemoveFilterChipCommand => _removeChip ??= Cmd(p => RemoveFilterChip((string)p!), p => p is string);

    public bool IsCategoryFiltered(string category) => _workspaceView.Categories.Contains(category);
    public bool IsColorFiltered(string color) => _workspaceView.Colors.Contains(color);

    public void ToggleCategoryFilter(string category)
    {
        if (!_workspaceView.Categories.Remove(category)) _workspaceView.Categories.Add(category);
        ApplyView();
    }

    public void ToggleColorFilter(string color)
    {
        if (!_workspaceView.Colors.Remove(color)) _workspaceView.Colors.Add(color);
        ApplyView();
    }

    /// <summary>Filter to workspaces that contain this exact item (one item at a time, ✓, approved 02:37:27).</summary>
    public void SetContentItemFilter(Item? item)
    {
        _workspaceView.ItemId = item?.Id;
        _workspaceView.ItemLabel = item == null ? "" : ContentTypeLabel(item.Type) + ": " + item.Name;
        _workspaceView.ItemName = item?.Name ?? "";
        ApplyView();
    }

    /// <summary>Remove one active filter by its chip key; every other filter stays.</summary>
    public void RemoveFilterChip(string key)
    {
        if (key.StartsWith("color:", StringComparison.OrdinalIgnoreCase)) _workspaceView.Colors.Remove(key["color:".Length..]);
        else if (key.StartsWith("cat:", StringComparison.Ordinal)) _workspaceView.Categories.Remove(key["cat:".Length..]);
        else if (key == "item") { _workspaceView.ItemId = null; _workspaceView.ItemLabel = ""; _workspaceView.ItemName = ""; }
        else if (key == "contains") _workspaceView.ContainsText = "";
        else return;
        Raise(nameof(FilterContainsText));
        ApplyView();
    }

    /// <summary>✕ ניקוי הכל: clears every active filter at once.</summary>
    public void ClearFilters()
    {
        _workspaceView.Categories.Clear(); _workspaceView.Colors.Clear(); _workspaceView.ItemId = null; _workspaceView.ItemLabel = ""; _workspaceView.ItemName = ""; _workspaceView.ContainsText = "";
        Raise(nameof(FilterContainsText));
        ApplyView();
    }

    /// <summary>Recompute DisplayWorkspaces (sort + active filters), the chips, the color options and the counter.
    /// Called from Show() after every load/save and from every view-option change.</summary>
    private void ApplyView()
    {
        var q = _workspaceView.Query(Workspaces, _current);
        var text = _workspaceView.ContainsText.Trim();
        DisplayWorkspaces.Clear(); foreach (var w in q) DisplayWorkspaces.Add(w);
        FilterChips.Clear(); foreach (var chip in _workspaceView.Chips(text)) FilterChips.Add(chip);

        FilterColorOptions.Clear();
        foreach (var c in Workspaces.Select(w => w.Color).Where(c => !string.IsNullOrEmpty(c)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(c => c, StringComparer.OrdinalIgnoreCase))
            FilterColorOptions.Add(c!);

        Raise(nameof(HasActiveFilters)); Raise(nameof(FilteredCountText)); Raise(nameof(FilterCategoryOptions));
        Raise(nameof(IsSingleContentFilter)); Raise(nameof(SingleContentFilterName)); Raise(nameof(SingleFilterCountText));
        _clearFilters?.Raise();
    }
}
