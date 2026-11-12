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
