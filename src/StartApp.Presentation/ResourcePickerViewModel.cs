using System.Collections.ObjectModel;
using StartApp.Core;

namespace StartApp.Presentation;

public sealed partial class MainViewModel
{
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

}
