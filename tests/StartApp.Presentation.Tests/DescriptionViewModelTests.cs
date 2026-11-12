using StartApp.Core;
using StartApp.Presentation;
using StartApp.Storage.Json;
using Xunit;

public class DescriptionViewModelTests
{
    private static string Tmp() => Path.Combine(Path.GetTempPath(), "sa-" + Guid.NewGuid().ToString("N"), "store.json");
    private static MainViewModel Vm(JsonStore repo) { var vm = new MainViewModel(repo, new NotImplementedLauncher()); vm.NewName = "w"; vm.CreateWorkspaceCommand.Execute(null); return vm; }

    [Fact] public void Add_with_description_saves_shows_and_clears_the_field()
    {
        using var repo = new JsonStore(Tmp()); var vm = Vm(repo);
        vm.NewItemType = ItemType.File; vm.NewTarget = "C:\\a.txt"; vm.NewDescription = "my notes"; vm.AddItemCommand.Execute(null);
        Assert.Equal("my notes", vm.Items.Single().Description); Assert.Equal("", vm.NewDescription); Assert.Equal("", vm.NewTarget);
        Assert.Equal("my notes", repo.Load().Workspaces.Single().Items.Single().Description); // per instance, not on the shared item
        Assert.Null(repo.Load().Items.Values.Single().Description);
    }
    [Fact] public void Add_without_description_is_allowed_and_has_none()
    {
        using var repo = new JsonStore(Tmp()); var vm = Vm(repo);
        vm.NewItemType = ItemType.File; vm.NewTarget = "C:\\a.txt"; vm.AddItemCommand.Execute(null);
        Assert.Null(vm.Items.Single().Description); Assert.Null(repo.Load().Items.Values.Single().Description);
    }
    [Fact] public void Too_long_description_is_rejected_and_nothing_is_added_and_the_text_is_kept()
    {
        using var repo = new JsonStore(Tmp()); var vm = Vm(repo); var big = new string('x', 501);
        vm.NewItemType = ItemType.File; vm.NewTarget = "C:\\a.txt"; vm.NewDescription = big; vm.AddItemCommand.Execute(null);
        Assert.StartsWith("Not added", vm.Status); Assert.Empty(vm.Items); Assert.Empty(repo.Load().Items); Assert.Equal(big, vm.NewDescription); Assert.Equal("C:\\a.txt", vm.NewTarget);
    }
    [Fact] public void Descriptions_are_per_workspace_instance_and_never_leak_across()
    {
        using var repo = new JsonStore(Tmp()); var vm = Vm(repo);
        vm.NewItemType = ItemType.File; vm.NewTarget = "C:\\a.txt"; vm.NewDescription = "first"; vm.AddItemCommand.Execute(null);
        vm.NewName = "w2"; vm.CreateWorkspaceCommand.Execute(null);
        vm.Selected = vm.Workspaces.Single(w => w.Name == "w2");
        vm.NewItemType = ItemType.File; vm.NewTarget = "C:\\a.txt"; vm.NewDescription = "second"; vm.AddItemCommand.Execute(null);
        Assert.Equal("second", vm.Items.Single().Description); Assert.Single(repo.Load().Items); // one shared catalog item
        var w1 = repo.Load().Workspaces.Single(w => w.Name == "w"); var w2 = repo.Load().Workspaces.Single(w => w.Name == "w2");
        Assert.Equal("first", w1.Items.Single().Description); Assert.Equal("second", w2.Items.Single().Description);
        Assert.NotEqual(w1.Items.Single().Id, w2.Items.Single().Id);
    }
    [Fact] public void Row_of_a_pre_description_item_has_null_description()
    {
        using var repo = new JsonStore(Tmp()); repo.Mutate(c => { var w = c.CreateWorkspace("w"); c.AddToWorkspace(w.Id, c.GetOrAddItem(ItemType.File, "a", "C:\\a.txt").Id); });
        var vm = new MainViewModel(repo, new NotImplementedLauncher()); vm.Selected = vm.Workspaces.Single();
        Assert.Null(vm.Items.Single().Description);
    }
    [Fact] public void Whitespace_only_description_is_saved_as_none()
    {
        using var repo = new JsonStore(Tmp()); var vm = Vm(repo);
        vm.NewItemType = ItemType.File; vm.NewTarget = "C:\\a.txt"; vm.NewDescription = "   "; vm.AddItemCommand.Execute(null);
        Assert.Null(vm.Items.Single().Description); Assert.Null(repo.Load().Workspaces.Single().Items.Single().Description);
    }
}
