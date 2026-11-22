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
    private string? _filterItemId; private string _filterItemLabel = "";
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
        ApplyView();
    }

    /// <summary>Remove one active filter by its chip key; every other filter stays.</summary>
    public void RemoveFilterChip(string key)
    {
        if (key.StartsWith("color:", StringComparison.OrdinalIgnoreCase)) _filterColors.Remove(key["color:".Length..]);
        else if (key.StartsWith("cat:", StringComparison.Ordinal)) _filterCategories.Remove(key["cat:".Length..]);
        else if (key == "item") { _filterItemId = null; _filterItemLabel = ""; }
        else if (key == "contains") _filterContainsText = "";
        else return;
        Raise(nameof(FilterContainsText));
        ApplyView();
    }

    /// <summary>✕ ניקוי הכל: clears every active filter at once.</summary>
    public void ClearFilters()
    {
        _filterCategories.Clear(); _filterColors.Clear(); _filterItemId = null; _filterItemLabel = ""; _filterContainsText = "";
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
    }
}
