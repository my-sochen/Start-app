using StartApp.Core;
using StartApp.Presentation;
using StartApp.Storage.Json;
using Xunit;

/// <summary>בחר מסביבות קיימות in the add form (settled Nov 11): items only (no workspaces), four type buttons
/// (תוכנות / אתרים / קבצים / תיקיות), the ORIGINAL resource name (never an instance's local name), a search filter inside
/// each list, sorting by the number of workspaces containing the exact address, and picking returns to the form with the
/// address filled while the local name/description stay for the user.</summary>
public class ResourcePickerTests
{
    private static string Tmp() => Path.Combine(Path.GetTempPath(), "sa-" + Guid.NewGuid().ToString("N"), "store.json");

    private static MainViewModel Vm(JsonStore repo)
    {
        var vm = new MainViewModel(repo, new NotImplementedLauncher());
        vm.NewName = "w"; vm.CreateWorkspaceCommand.Execute(null);
        return vm;
    }
    private static void AddLink(MainViewModel vm, string url, string name)
    { vm.NewItemType = ItemType.Link; vm.NewTarget = url; vm.NewItemName = name; vm.AddItemCommand.Execute(null); }

    [Fact] public void Picker_lists_items_only_with_original_names_not_instance_names()
    {
        using var repo = new JsonStore(Tmp());
        var vm = Vm(repo);
        AddLink(vm, "https://example.com/", "my local name");
        vm.OpenPickerCommand.Execute(null);
        Assert.True(vm.PickerOpen);
        var row = Assert.Single(vm.PickerRows);
        Assert.Equal("example.com", row.Name); // original resource name, not "my local name"
        Assert.Equal("https://example.com/", row.Target); // sites also show the address
        Assert.Equal(ItemType.Link, row.Type);
    }

    [Fact] public void Four_type_buttons_switch_the_list()
    {
        using var repo = new JsonStore(Tmp());
        var vm = Vm(repo);
        AddLink(vm, "https://example.com/", "");
        vm.NewItemType = ItemType.File; vm.NewTarget = "C:\\a.txt"; vm.AddItemCommand.Execute(null);
        vm.OpenPickerCommand.Execute(null);
        Assert.Equal(ItemType.Link, vm.PickerType); // אתרים default
        Assert.Single(vm.PickerRows);
        vm.PickTypeCommand.Execute(ItemType.File);
        Assert.Equal("a.txt", Assert.Single(vm.PickerRows).Name);
        vm.PickTypeCommand.Execute(ItemType.Program);
        Assert.Empty(vm.PickerRows);
        vm.PickTypeCommand.Execute(ItemType.Folder);
        Assert.Empty(vm.PickerRows);
        Assert.Equal(new[] { ItemType.Program, ItemType.Link, ItemType.File, ItemType.Folder }, vm.PickerTypes); // settled order
    }

    [Fact] public void Sort_is_by_workspace_count_of_the_exact_address_descending()
    {
        using var repo = new JsonStore(Tmp());
        var vm = Vm(repo);
        var first = vm.Selected!;
        AddLink(vm, "https://popular.example/", "");
        AddLink(vm, "https://rare.example/", "");
        vm.NewName = "w2"; vm.CreateWorkspaceCommand.Execute(null);
        AddLink(vm, "https://popular.example/", "");
        vm.Selected = vm.Workspaces.Single(w => w.Id == first.Id);
        vm.OpenPickerCommand.Execute(null);
        Assert.Equal(new[] { "popular.example", "rare.example" }, vm.PickerRows.Select(r => r.Name));
        Assert.Equal(2, vm.PickerRows[0].Usage); // the same address in two workspaces
        Assert.Equal(1, vm.PickerRows[1].Usage);
    }

    [Fact] public void Search_filters_inside_each_list()
    {
        using var repo = new JsonStore(Tmp());
        var vm = Vm(repo);
        AddLink(vm, "https://alpha.example/", "");
        AddLink(vm, "https://beta.example/", "");
        vm.OpenPickerCommand.Execute(null);
        Assert.Equal(2, vm.PickerRows.Count);
        vm.PickerSearch = "alpha";
        Assert.Equal("alpha.example", Assert.Single(vm.PickerRows).Name);
        vm.PickerSearch = "example";
        Assert.Equal(2, vm.PickerRows.Count); // matches the address too
        vm.PickerSearch = "";
    }

    [Fact] public void Pick_returns_to_the_form_with_the_address_filled_and_add_keeps_the_local_name()
    {
        using var repo = new JsonStore(Tmp());
        var vm = Vm(repo);
        var first = vm.Selected!;
        AddLink(vm, "https://example.com/", "in w");
        vm.NewName = "w2"; vm.CreateWorkspaceCommand.Execute(null); // fresh form in w2
        vm.OpenPickerCommand.Execute(null);
        vm.PickResourceCommand.Execute(Assert.Single(vm.PickerRows));
        Assert.False(vm.PickerOpen);
        Assert.Equal(ItemType.Link, vm.NewItemType);
        Assert.Equal("https://example.com/", vm.NewTarget);
        Assert.Equal("", vm.NewItemName); // local name stays for the user
        vm.NewItemName = "local to w2";
        vm.AddItemCommand.Execute(null);
        Assert.Equal("local to w2", vm.Items.Single().Name);
        // and the instance in the first workspace kept its own name
        Assert.Equal("in w", repo.Load().Workspaces.Single(w => w.Id == first.Id).Items.Single().Name);
    }

    [Fact] public void Address_existing_only_in_another_workspace_adds_without_a_duplicate_warning()
    {
        using var repo = new JsonStore(Tmp());
        var vm = Vm(repo);
        AddLink(vm, "https://example.com/", "");
        vm.NewName = "w2"; vm.CreateWorkspaceCommand.Execute(null);
        vm.NewItemType = ItemType.Link; vm.NewTarget = "https://example.com/"; vm.AddItemCommand.Execute(null);
        Assert.False(vm.DuplicatePending); // the warning is only for the current workspace
        Assert.Single(vm.Items);
    }
}

/// <summary>לכידה (settled Nov 12 07:14-07:45): the list is a snapshot of what the scan reported, read-only; adds to a new workspace (checked by default)
/// or to an existing one side by side, staged until שמור; duplicates warn per drag or once for a group; X resets, חזור keeps the snapshot.</summary>
public class CaptureViewModelTests
{
    private static string Tmp() => Path.Combine(Path.GetTempPath(), "sa-" + Guid.NewGuid().ToString("N"), "store.json");
    private sealed class FakeCapturer : IOpenItemCapturer
    {
        public List<CapturedItem> Items = new()
        {
            new(ItemType.Program, "Notepad", "C:\\Windows\\notepad.exe"),
            new(ItemType.Folder, "proj", "D:\\proj"),
            new(ItemType.Link, "example.com", "https://example.com/"),
        };
        public int Scans;
        public CaptureScan Scan() { Scans++; return new(Items.ToList(), new[] { "Files: not detected" }); }
    }
    private static (MainViewModel vm, FakeCapturer cap, JsonStore repo) Make(Action<MainViewModel>? setup = null)
    {
        var repo = new JsonStore(Tmp()); var cap = new FakeCapturer();
        var vm = new MainViewModel(repo, new NotImplementedLauncher(), capturer: cap);
        setup?.Invoke(vm);
        return (vm, cap, repo);
    }
    private static void Ws(MainViewModel vm, string name) { vm.NewName = name; vm.CreateWorkspaceCommand.Execute(null); vm.BackCommand.Execute(null); }
    private static Workspace Find(MainViewModel vm, string name) => vm.Workspaces.Single(w => w.Name == name);
    private static int Saved(JsonStore repo, string ws) => repo.Load().Workspaces.Single(w => w.Name == ws).Items.Count;

    [Fact] public void New_workspace_capture_scans_shows_rows_all_checked_and_creates_only_checked_items()
    {
        var (vm, cap, repo) = Make(); using var _ = repo;
        vm.CaptureNewCommand.Execute(null);
        Assert.True(vm.IsCaptureNew); Assert.Equal(1, cap.Scans);
        Assert.Equal(3, vm.CaptureRows.Count); Assert.All(vm.CaptureRows, r => Assert.True(r.IsChecked));
        Assert.Contains("Files: not detected", vm.CaptureNotes);
        vm.CaptureRows.Single(r => r.Name == "proj").IsChecked = false;
        vm.NewName = "from capture";
        vm.CaptureCreateCommand.Execute(null);
        Assert.False(vm.CaptureOpen); Assert.True(vm.InDetails);
        var w = repo.Load(); var ws = w.Workspaces.Single();
        Assert.Equal(2, ws.Items.Count);
        Assert.Equal(new[] { "C:\\Windows\\notepad.exe", "https://example.com/" }.OrderBy(x => x, StringComparer.Ordinal), ws.Items.Select(m => w.Items[m.ItemId].Target).OrderBy(x => x, StringComparer.Ordinal));
        Assert.Empty(vm.CaptureRows);
    }

    [Fact] public void Create_needs_a_name_and_a_typed_name_then_X_asks_before_discarding()
    {
        var (vm, _, repo) = Make(); using var _ = repo;
        vm.CaptureNewCommand.Execute(null);
        Assert.False(vm.CaptureCreateCommand.CanExecute(null));
        vm.NewName = "typed";
        vm.CaptureCloseCommand.Execute(null);
        Assert.True(vm.UnsavedOpen); Assert.True(vm.UnsavedForCapture); Assert.True(vm.CaptureOpen);
        vm.CloseUnsavedCommand.Execute(null);               // the dialog's X: back to the capture screen, nothing lost
        Assert.True(vm.CaptureOpen); Assert.Equal("typed", vm.NewName); Assert.Equal(3, vm.CaptureRows.Count);
        vm.CaptureCloseCommand.Execute(null); vm.ExitWithoutSavingCommand.Execute(null);
        Assert.False(vm.CaptureOpen); Assert.Empty(repo.Load().Workspaces); Assert.Equal("", vm.NewName);
    }

    [Fact] public void Existing_mode_staging_is_not_saved_until_Save_and_the_capture_list_stays_open()
    {
        var (vm, _, repo) = Make(v => { Ws(v, "A"); }); using var _ = repo;
        vm.CaptureExistingCommand.Execute(null);
        Assert.True(vm.IsCapturePick); Assert.All(vm.CaptureRows, r => Assert.False(r.IsChecked));
        vm.CapturePickWorkspaceCommand.Execute(Find(vm, "A"));
        Assert.True(vm.IsCaptureSide);
        Assert.Equal("הוסף הכל", vm.CaptureAddLabel);
        vm.CaptureAddCommand.Execute(null);
        Assert.Equal(3, vm.StagedRows.Count); Assert.Equal(0, Saved(repo, "A")); Assert.True(vm.CaptureDirty);
        vm.CaptureSaveCommand.Execute(null);
        Assert.Equal(3, Saved(repo, "A")); Assert.Empty(vm.StagedRows);
        Assert.Equal(3, vm.CaptureRows.Count); Assert.True(vm.IsCaptureSide); Assert.Equal(3, vm.CaptureWorkspaceItems.Count);
        Assert.False(vm.CaptureDirty);
    }

    [Fact] public void Add_button_becomes_add_selected_and_adds_only_the_ticked_rows()
    {
        var (vm, _, repo) = Make(v => Ws(v, "A")); using var _ = repo;
        vm.CaptureExistingCommand.Execute(null); vm.CapturePickWorkspaceCommand.Execute(Find(vm, "A"));
        vm.CaptureRows[0].IsChecked = true; vm.CaptureRows[2].IsChecked = true;
        Assert.Equal("הוסף נבחרים", vm.CaptureAddLabel);
        vm.CaptureAddCommand.Execute(null);
        Assert.Equal(new[] { "Notepad", "example.com" }, vm.StagedRows.Select(s => s.OriginalName).ToArray());
        vm.CaptureRows[0].IsChecked = false; vm.CaptureRows[2].IsChecked = false;
        Assert.Equal("הוסף הכל", vm.CaptureAddLabel);
    }

    [Fact] public void Dragging_a_duplicate_warns_for_that_drag_and_confirm_adds_a_second_instance()
    {
        var (vm, _, repo) = Make(v => { Ws(v, "A"); v.OpenDetailsCommand.Execute(Find(v, "A")); v.NewItemType = ItemType.Folder; v.NewTarget = "D:\\proj"; v.AddItemCommand.Execute(null); v.BackCommand.Execute(null); }); using var _ = repo;
        vm.CaptureExistingCommand.Execute(null); vm.CapturePickWorkspaceCommand.Execute(Find(vm, "A"));
        vm.CaptureDropCommand.Execute(vm.CaptureRows.Single(r => r.Name == "Notepad"));
        Assert.Single(vm.StagedRows); Assert.False(vm.CaptureDupSinglePending);          // new address: no warning
        vm.CaptureDropCommand.Execute(vm.CaptureRows.Single(r => r.Name == "proj"));
        Assert.True(vm.CaptureDupSinglePending); Assert.Single(vm.StagedRows);           // already saved in the workspace: warning, not added yet
        vm.CancelCaptureDupSingleCommand.Execute(null);
        Assert.False(vm.CaptureDupSinglePending); Assert.Single(vm.StagedRows);
        vm.CaptureDropCommand.Execute(vm.CaptureRows.Single(r => r.Name == "proj"));
        vm.ConfirmCaptureDupSingleCommand.Execute(null);
        Assert.Equal(2, vm.StagedRows.Count);
        vm.CaptureDropCommand.Execute(vm.CaptureRows.Single(r => r.Name == "Notepad"));  // already staged counts as a duplicate too
        Assert.True(vm.CaptureDupSinglePending);
    }

    [Fact] public void Adding_several_shows_one_grouped_duplicate_dialog_and_adds_only_the_ticked_duplicates()
    {
        var (vm, _, repo) = Make(v => { Ws(v, "A"); v.OpenDetailsCommand.Execute(Find(v, "A"));
            v.NewItemType = ItemType.Folder; v.NewTarget = "D:\\proj"; v.AddItemCommand.Execute(null);
            v.NewItemType = ItemType.Link; v.NewTarget = "https://example.com/"; v.AddItemCommand.Execute(null); v.BackCommand.Execute(null); }); using var _ = repo;
        vm.CaptureExistingCommand.Execute(null); vm.CapturePickWorkspaceCommand.Execute(Find(vm, "A"));
        vm.CaptureAddCommand.Execute(null);
        Assert.Single(vm.StagedRows); Assert.Equal("Notepad", vm.StagedRows[0].OriginalName);      // the non-duplicate is staged at once
        Assert.True(vm.CaptureDupGroupPending); Assert.Equal(2, vm.CaptureDupChoices.Count);          // ONE dialog listing both duplicates
        Assert.All(vm.CaptureDupChoices, c => Assert.False(c.IsChecked));
        vm.CaptureDupChoices.Single(c => c.Row.Name == "proj").IsChecked = true;
        vm.ConfirmCaptureDupGroupCommand.Execute(null);
        Assert.False(vm.CaptureDupGroupPending);
        Assert.Equal(new[] { "Notepad", "proj" }, vm.StagedRows.Select(s => s.OriginalName).ToArray());
    }

    [Fact] public void Back_keeps_the_snapshot_and_lets_you_add_to_another_workspace_after_the_unsaved_dialog()
    {
        var (vm, cap, repo) = Make(v => { Ws(v, "A"); Ws(v, "B"); }); using var _ = repo;
        vm.CaptureExistingCommand.Execute(null); vm.CapturePickWorkspaceCommand.Execute(Find(vm, "A"));
        vm.CaptureRows[0].IsChecked = true; vm.CaptureAddCommand.Execute(null);
        vm.CaptureBackCommand.Execute(null);
        Assert.True(vm.UnsavedOpen); Assert.True(vm.IsCaptureSide);
        vm.SaveAndExitCommand.Execute(null);                                   // שמור וצא: saves A then returns to the workspace search
        Assert.Equal(1, Saved(repo, "A")); Assert.True(vm.IsCapturePick);
        Assert.Equal(3, vm.CaptureRows.Count); Assert.Equal(1, cap.Scans);     // same snapshot, no rescan
        vm.CapturePickWorkspaceCommand.Execute(Find(vm, "B"));
        vm.CaptureRows[0].IsChecked = true; vm.CaptureAddCommand.Execute(null); vm.CaptureSaveCommand.Execute(null);
        Assert.Equal(1, Saved(repo, "B"));                                      // the same capture fed several workspaces
        var s = repo.Load(); Assert.NotEqual(s.Workspaces[0].Items[0].Id, s.Workspaces[1].Items[0].Id);  // independent instances
    }

    [Fact] public void Back_without_saving_discards_staging_but_keeps_earlier_saves()
    {
        var (vm, _, repo) = Make(v => { Ws(v, "A"); }); using var _ = repo;
        vm.CaptureExistingCommand.Execute(null); vm.CapturePickWorkspaceCommand.Execute(Find(vm, "A"));
        vm.CaptureRows[0].IsChecked = true; vm.CaptureAddCommand.Execute(null); vm.CaptureSaveCommand.Execute(null);
        vm.CaptureRows[0].IsChecked = false; vm.CaptureRows[1].IsChecked = true; vm.CaptureAddCommand.Execute(null);
        vm.CaptureBackCommand.Execute(null); vm.ExitWithoutSavingCommand.Execute(null);
        Assert.True(vm.IsCapturePick); Assert.Equal(1, Saved(repo, "A")); Assert.Empty(vm.StagedRows);
    }

    [Fact] public void X_resets_the_snapshot_asks_when_unsaved_and_the_next_capture_scans_again()
    {
        var (vm, cap, repo) = Make(v => Ws(v, "A")); using var _ = repo;
        vm.CaptureExistingCommand.Execute(null); vm.CapturePickWorkspaceCommand.Execute(Find(vm, "A"));
        vm.CaptureAddCommand.Execute(null);
        vm.CaptureCloseCommand.Execute(null);
        Assert.True(vm.UnsavedOpen); Assert.True(vm.CaptureOpen);
        vm.ExitWithoutSavingCommand.Execute(null);
        Assert.False(vm.CaptureOpen); Assert.Empty(vm.CaptureRows); Assert.Equal(0, Saved(repo, "A"));
        cap.Items = new() { new(ItemType.Program, "Calc", "C:\\Windows\\calc.exe") };
        vm.CaptureExistingCommand.Execute(null);
        Assert.Equal(2, cap.Scans); Assert.Equal("Calc", vm.CaptureRows.Single().Name);
    }

    [Fact] public void Close_with_nothing_unsaved_closes_without_a_dialog()
    {
        var (vm, _, repo) = Make(v => Ws(v, "A")); using var _ = repo;
        vm.CaptureExistingCommand.Execute(null); vm.CaptureCloseCommand.Execute(null);
        Assert.False(vm.UnsavedOpen); Assert.False(vm.CaptureOpen);
    }

    [Fact] public void Staged_item_edits_apply_to_the_new_instance_only_and_never_to_the_capture_row()
    {
        var (vm, _, repo) = Make(v => Ws(v, "A")); using var _ = repo;
        vm.CaptureExistingCommand.Execute(null); vm.CapturePickWorkspaceCommand.Execute(Find(vm, "A"));
        vm.CaptureRows[1].IsChecked = true; vm.CaptureAddCommand.Execute(null);
        var st = vm.StagedRows.Single(); st.Name = "my project"; st.Description = "main folder";
        Assert.Equal("proj", vm.CaptureRows[1].Name);
        vm.CaptureSaveCommand.Execute(null);
        var s = repo.Load(); var m = s.Workspaces.Single().Items.Single();
        Assert.Equal("my project", m.Name); Assert.Equal("main folder", m.Description); Assert.Equal("proj", s.Items[m.ItemId].Name);
        vm.CaptureRows[1].IsChecked = true; vm.CaptureAddCommand.Execute(null);   // default details again, not the edited ones
        Assert.True(vm.CaptureDupGroupPending);
    }

    [Fact] public void Invalid_staged_values_are_reported_and_nothing_is_saved()
    {
        var (vm, _, repo) = Make(v => Ws(v, "A")); using var _ = repo;
        vm.CaptureExistingCommand.Execute(null); vm.CapturePickWorkspaceCommand.Execute(Find(vm, "A"));
        vm.CaptureRows[1].IsChecked = true; vm.CaptureAddCommand.Execute(null);
        vm.StagedRows.Single().OpenWith = "notepad";   // not an absolute path
        vm.CaptureSaveCommand.Execute(null);
        Assert.StartsWith("Not saved", vm.Status); Assert.Equal(0, Saved(repo, "A")); Assert.Single(vm.StagedRows);
    }

    [Fact] public void Add_from_open_in_details_goes_straight_to_the_current_workspace_and_Back_leads_to_the_search()
    {
        var (vm, _, repo) = Make(v => { Ws(v, "A"); v.OpenDetailsCommand.Execute(Find(v, "A")); }); using var _ = repo;
        Assert.True(vm.AddFromOpenCommand.CanExecute(null));
        vm.AddFromOpenCommand.Execute(null);
        Assert.True(vm.IsCaptureSide); Assert.Equal("A", vm.CaptureWorkspaceName);
        vm.CaptureBackCommand.Execute(null);
        Assert.True(vm.IsCapturePick); Assert.Single(vm.CaptureWorkspaceResults);
    }

    [Fact] public void Workspace_search_filters_by_name()
    {
        var (vm, _, repo) = Make(v => { Ws(v, "alpha"); Ws(v, "beta"); }); using var _ = repo;
        vm.CaptureExistingCommand.Execute(null);
        Assert.Equal(2, vm.CaptureWorkspaceResults.Count);
        vm.CaptureSearch = "bet"; Assert.Equal("beta", vm.CaptureWorkspaceResults.Single().Name);
    }

    [Fact] public void Capture_entries_are_not_available_while_read_only_or_already_open_and_without_a_detector_it_says_so()
    {
        var repo = new JsonStore(Tmp()); using var _ = repo;
        var vm = new MainViewModel(repo, new NotImplementedLauncher());
        vm.CaptureNewCommand.Execute(null);
        Assert.True(vm.CaptureEmpty); Assert.Equal("Nothing open was detected", vm.Status); Assert.Single(vm.CaptureNotes);
        Assert.False(vm.CaptureNewCommand.CanExecute(null));
    }
}
