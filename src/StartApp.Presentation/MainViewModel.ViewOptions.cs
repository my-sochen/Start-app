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
    private ViewSort _currentSort = ViewSort.RecentEntries;
    private CardSizeOption _cardSize = CardSizeOption.Medium;
    private ViewTemplate _template = ViewTemplate.Cards;
    private string _filterContainsText = "";
    private string? _filterItemId; private string _filterItemLabel = ""; private string _filterItemName = "";
    private readonly HashSet<string> _filterCategories = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _filterColors = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>The sorted and filtered projection of <see cref="Workspaces"/> shown on the home overview.</summary>
    public ObservableCollection<Workspace> DisplayWorkspaces { get; } = new();
    /// <summary>Active filters as chips, in a stable order: colors, categories, content item, contains.</summary>
    public ObservableCollection<FilterChip> FilterChips { get; } = new();
    /// <summary>Custom card colors currently in use, offered under "לפי צבע".</summary>
    public ObservableCollection<string> FilterColorOptions { get; } = new();

    public ViewSort CurrentSort { get => _currentSort; set { Set(ref _currentSort, value); ApplyView(); } }
    public CardSizeOption CurrentCardSize { get => _cardSize; set => Set(ref _cardSize, value); }
    public ViewTemplate CurrentTemplate { get => _template; set => Set(ref _template, value); }
    /// <summary>סינון "מכיל...": free text; environments containing it stay (bonus item, approved as a good idea 02:34:10).</summary>
    public string FilterContainsText { get => _filterContainsText; set { Set(ref _filterContainsText, value); ApplyView(); } }

    public bool HasActiveFilters => _filterCategories.Count > 0 || _filterColors.Count > 0 || _filterItemId != null || _filterContainsText.Trim().Length > 0;
    /// <summary>Live counter on the filter bar, e.g. "מוצגת 2 מתוך 6".</summary>
    public string FilteredCountText => $"מוצגת {DisplayWorkspaces.Count} מתוך {Workspaces.Count}";
    /// <summary>Single-content-filter state (approved mock home-filter-program): the bar switches to the short form
    /// "מסונן: סביבות שמכילות X · N מתוך M" + "✕ ניקוי סינון" instead of chips.</summary>
    public bool IsSingleContentFilter => _filterItemId != null && _filterCategories.Count == 0 && _filterColors.Count == 0 && _filterContainsText.Trim().Length == 0;
    /// <summary>The pinned item's plain name for the short-form bar ("סביבות שמכילות Visual Studio Code").</summary>
    public string SingleContentFilterName => _filterItemName;
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
            _pendingDefaultId = workspaceId;
            DefaultConfirmOpen = true;
            Raise(nameof(DefaultConfirmOpen)); Raise(nameof(DefaultConfirmBody)); Raise(nameof(DefaultConfirmNotice)); Raise(nameof(DefaultConfirmTitle)); Raise(nameof(DefaultConfirmAcceptLabel)); RaiseCommands();
            return;
        }
        ApplyDefaultWorkspace(workspaceId);
    }

    private string? _pendingDefaultId;
    private string DefaultName(string? id) => id is { Length: > 0 } && _current.Workspaces.FirstOrDefault(w => w.Id == id) is { } w ? w.Name : "";

    /// <summary>Replace-confirm dialog state (approved dialog 22.11): open flag, dynamic body naming the current and
    /// candidate workspaces, and the blue notice naming the candidate.</summary>
    public bool DefaultConfirmOpen { get; private set; }
    public string DefaultConfirmTitle => _pendingDefaultId is null ? "להסיר את ברירת המחדל?" : "כבר מוגדרת סביבת ברירת מחדל";
    public string DefaultConfirmAcceptLabel => _pendingDefaultId is null ? "כן, הסר ברירת מחדל" : "כן, הגדר כברירת מחדל";
    public string DefaultConfirmBody => _pendingDefaultId is null
        ? $"הסביבה '{DefaultName(_current.DefaultWorkspaceId)}' מוגדרת כיום כברירת מחדל ונפתחת אוטומטית עם הפעלת המחשב.\nלהגדיר ללא ברירת מחדל?"
        : $"הסביבה '{DefaultName(_current.DefaultWorkspaceId)}' מוגדרת כיום כברירת מחדל ונפתחת אוטומטית עם הפעלת המחשב.\nלהגדיר במקומה את '{DefaultName(_pendingDefaultId)}'?";
    public string DefaultConfirmNotice => _pendingDefaultId is null
        ? "אחרי האישור הנקודה הירוקה תוסר ולא תיפתח אף סביבה עם הפעלת המחשב. אפשר להגדיר ברירת מחדל שוב בכל עת."
        : $"אחרי האישור הנקודה הירוקה תעבור אל '{DefaultName(_pendingDefaultId)}', והיא זו שתיפתח עם הפעלת המחשב.";

    /// <summary>Dialog confirm: move the green badge and fire the existing ~3s toast.</summary>
    public ICommand ConfirmDefaultReplaceCommand => _confirmDefaultReplace ??= Cmd(_ =>
    {
        if (!DefaultConfirmOpen || ReadOnly) return;
        var id = _pendingDefaultId;
        CloseDefaultConfirm();
        ApplyDefaultWorkspace(id);
    }, _ => !ReadOnly);
    private ICommand? _confirmDefaultReplace;

    /// <summary>Dialog cancel (also ✕ and outside click): nothing changes.</summary>
    public ICommand CancelDefaultReplaceCommand => _cancelDefaultReplace ??= Cmd(_ => CloseDefaultConfirm(), _ => !ReadOnly);
    private ICommand? _cancelDefaultReplace;

    private void CloseDefaultConfirm()
    {
        _pendingDefaultId = null;
        DefaultConfirmOpen = false;
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
    public string? CurrentContentItemId => _filterItemId;

    private RelayCommand? _clearFilters;
    private RelayCommand? _removeChip;
    public ICommand ClearFiltersCommand => _clearFilters ??= Cmd(_ => ClearFilters(), _ => HasActiveFilters);
    public ICommand RemoveFilterChipCommand => _removeChip ??= Cmd(p => RemoveFilterChip((string)p!), p => p is string);

    public bool IsCategoryFiltered(string category) => _filterCategories.Contains(category);
    public bool IsColorFiltered(string color) => _filterColors.Contains(color);

    public void ToggleCategoryFilter(string category)
    {
        if (!_filterCategories.Remove(category)) _filterCategories.Add(category);
        ApplyView();
    }

    public void ToggleColorFilter(string color)
    {
        if (!_filterColors.Remove(color)) _filterColors.Add(color);
        ApplyView();
    }

    /// <summary>Filter to workspaces that contain this exact item (one item at a time, ✓, approved 02:37:27).</summary>
    public void SetContentItemFilter(Item? item)
    {
        _filterItemId = item?.Id;
        _filterItemLabel = item == null ? "" : ContentTypeLabel(item.Type) + ": " + item.Name;
        _filterItemName = item?.Name ?? "";
        ApplyView();
    }

    /// <summary>Remove one active filter by its chip key; every other filter stays.</summary>
    public void RemoveFilterChip(string key)
    {
        if (key.StartsWith("color:", StringComparison.OrdinalIgnoreCase)) _filterColors.Remove(key["color:".Length..]);
        else if (key.StartsWith("cat:", StringComparison.Ordinal)) _filterCategories.Remove(key["cat:".Length..]);
        else if (key == "item") { _filterItemId = null; _filterItemLabel = ""; _filterItemName = ""; }
        else if (key == "contains") _filterContainsText = "";
        else return;
        Raise(nameof(FilterContainsText));
        ApplyView();
    }

    /// <summary>✕ ניקוי הכל: clears every active filter at once.</summary>
    public void ClearFilters()
    {
        _filterCategories.Clear(); _filterColors.Clear(); _filterItemId = null; _filterItemLabel = ""; _filterItemName = ""; _filterContainsText = "";
        Raise(nameof(FilterContainsText));
        ApplyView();
    }

    private static bool WorkspaceContains(Workspace w, Store s, string text)
    {
        bool Has(string? v) => v != null && v.Contains(text, StringComparison.OrdinalIgnoreCase);
        if (Has(w.Name) || Has(w.Description)) return true;
        foreach (var m in w.Items)
            if (s.Items.TryGetValue(m.ItemId, out var it) && (Has(m.Name ?? it.Name) || Has(it.Target) || Has(m.Description ?? it.Description)))
                return true;
        return false;
    }

    /// <summary>Recompute DisplayWorkspaces (sort + active filters), the chips, the color options and the counter.
    /// Called from Show() after every load/save and from every view-option change.</summary>
    private void ApplyView()
    {
        IEnumerable<Workspace> q = Workspaces;
        if (_filterCategories.Count > 0) q = q.Where(w => _filterCategories.Contains(w.EffectiveCategory));
        if (_filterColors.Count > 0) q = q.Where(w => w.Color != null && _filterColors.Contains(w.Color));
        if (_filterItemId != null) q = q.Where(w => w.Items.Any(m => m.ItemId == _filterItemId));
        var text = _filterContainsText.Trim();
        if (text.Length > 0) q = q.Where(w => WorkspaceContains(w, _current, text));
        q = _currentSort switch
        {
            ViewSort.CreationDate => q.OrderByDescending(w => w.CreatedAt ?? DateTime.MinValue),
            ViewSort.ItemCount => q.OrderByDescending(w => w.Items.Count),
            ViewSort.LaunchCount => q.OrderByDescending(w => w.LaunchCount),
            ViewSort.Alphabetical => q.OrderBy(w => w.Name, StringComparer.CurrentCulture),
            _ => q.OrderByDescending(w => w.LastOpenedAt ?? DateTime.MinValue),
        };
        DisplayWorkspaces.Clear(); foreach (var w in q) DisplayWorkspaces.Add(w);

        FilterChips.Clear();
        foreach (var c in _filterColors.OrderBy(c => c, StringComparer.OrdinalIgnoreCase)) FilterChips.Add(new FilterChip("color:" + c, c, c));
        foreach (var c in _filterCategories.OrderBy(c => c, StringComparer.CurrentCulture)) FilterChips.Add(new FilterChip("cat:" + c, c));
        if (_filterItemId != null) FilterChips.Add(new FilterChip("item", _filterItemLabel));
        if (text.Length > 0) FilterChips.Add(new FilterChip("contains", "מכיל: " + text));

        FilterColorOptions.Clear();
        foreach (var c in Workspaces.Select(w => w.Color).Where(c => !string.IsNullOrEmpty(c)).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(c => c, StringComparer.OrdinalIgnoreCase))
            FilterColorOptions.Add(c!);

        Raise(nameof(HasActiveFilters)); Raise(nameof(FilteredCountText)); Raise(nameof(FilterCategoryOptions));
        Raise(nameof(IsSingleContentFilter)); Raise(nameof(SingleContentFilterName)); Raise(nameof(SingleFilterCountText));
    }
}
