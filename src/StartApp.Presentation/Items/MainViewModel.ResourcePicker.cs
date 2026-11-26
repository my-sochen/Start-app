using System.Windows.Input;
using System.Collections.ObjectModel;
using StartApp.Core;

namespace StartApp.Presentation;

public sealed partial class MainViewModel
{
    /// <summary>בחר מסביבות קיימות in the add form: opens the existing-resource picker (settled Nov 11).</summary>
    public ICommand OpenPickerCommand { get; private set; } = null!;
    /// <summary>One of the four type buttons in the picker (תוכנות / אתרים / קבצים / תיקיות).</summary>
    public ICommand PickTypeCommand { get; private set; } = null!;
    /// <summary>Choosing a resource returns to the add form with its address filled; local name/description stay the user's to set before adding.</summary>
    public ICommand PickResourceCommand { get; private set; } = null!;
    public ICommand ClosePickerCommand { get; private set; } = null!;

    private void InitResourcePickerCommands()
    {
        OpenPickerCommand = Cmd(_ => OpenPicker(), _ => InDetails && Selected != null && !ReadOnly);
        PickTypeCommand = Cmd(p => { PickerType = (ItemType)p!; RefreshPicker(); }, p => p is ItemType);
        PickResourceCommand = Cmd(p => PickResource((PickerRow)p!), p => p is PickerRow);
        ClosePickerCommand = Cmd(_ => PickerOpen = false, _ => PickerOpen);
    }

    // ---- existing-resource picker (בחר מסביבות קיימות, settled Nov 11): items only, four type buttons, the ORIGINAL
    // resource name (never an instance's local name), search, and sorting by the number of workspaces containing that
    // exact address. Choosing fills the add form's address; the local name/description stay for the user before adding. ----
    /// <summary>One resource in the picker. Name is the catalog item's original name; Usage is the number of workspaces containing the same address.</summary>
    public sealed record PickerRow(string ItemId, string Name, string Target, int Usage, ItemType Type);
    private readonly ResourcePickerSession _resourcePicker = new();
    public ObservableCollection<PickerRow> PickerRows { get; } = new();
    public bool PickerOpen { get => _resourcePicker.IsOpen; private set { if (_resourcePicker.IsOpen != value) { _resourcePicker.IsOpen = value; Raise(nameof(PickerOpen)); } RaiseCommands(); } }
    /// <summary>The active type button in the picker; links are the default.</summary>
    public ItemType PickerType { get => _resourcePicker.Type; private set { if (_resourcePicker.Type != value) { _resourcePicker.Type = value; Raise(nameof(PickerType)); } } }
    public IReadOnlyList<ItemType> PickerTypes { get; } = new[] { ItemType.Link, ItemType.Program, ItemType.File, ItemType.Folder };
    public string PickerSearch { get => _resourcePicker.Search; set { var search = value ?? ""; if (_resourcePicker.Search != search) { _resourcePicker.Search = search; Raise(nameof(PickerSearch)); } RefreshPicker(); } }
    private void RefreshPicker()
    {
        PickerRows.Clear(); if (!PickerOpen) return;
        foreach (var row in _resourcePicker.Read(_current))
            PickerRows.Add(new PickerRow(row.ItemId, row.Name, row.Target, row.Usage, row.Type));
    }
    private void OpenPicker() { PickerType = ItemType.Link; PickerOpen = true; RefreshPicker(); }
    private void PickResource(PickerRow row)
    {
        NewItemType = row.Type; NewTarget = row.Target;
        PickerOpen = false; Status = "Address filled from an existing resource; set a local name and description before adding";
    }

}
