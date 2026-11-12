using StartApp.Core;
using StartApp.Presentation;
using StartApp.Storage.Json;
using Xunit;

public class MainViewModelTests
{
    private static string Tmp() => Path.Combine(Path.GetTempPath(), "sa-" + Guid.NewGuid().ToString("N"), "store.json");

    [Fact] public void CreateWorkspace_Persists_AndSelects()
    {
        var p = Tmp(); using var repo = new JsonStore(p);
        var vm = new MainViewModel(repo, new NotImplementedLauncher());
        vm.NewName = "work"; vm.CreateWorkspaceCommand.Execute(null);
        Assert.Equal("work", vm.Selected!.Name);
        Assert.Single(repo.Load().Workspaces);
    }
    [Fact] public void InvalidFile_GoesReadOnly_AndNothingIsWritten()
    {
        var p = Tmp(); Directory.CreateDirectory(Path.GetDirectoryName(p)!); File.WriteAllText(p, "{broken");
        using var repo = new JsonStore(p);
        var vm = new MainViewModel(repo, new NotImplementedLauncher());
        Assert.True(vm.ReadOnly); Assert.Contains("untouched", vm.Status);
        vm.NewName = "x"; Assert.False(vm.CreateWorkspaceCommand.CanExecute(null));
        Assert.Equal("{broken", File.ReadAllText(p));
    }
    [Fact] public void OpenWorkspace_WithNoAdapter_ReportsPartialNotSuccess()
    {
        var p = Tmp(); using var repo = new JsonStore(p);
        repo.Mutate(c => { var w = c.CreateWorkspace("w"); var i = c.GetOrAddItem(ItemType.File, "f", "C:\\a.txt"); c.AddToWorkspace(w.Id, i.Id); });
        var vm = new MainViewModel(repo, new NotImplementedLauncher());
        vm.OpenWorkspaceCommand.Execute(null); vm.OpenTask.Wait(); Assert.True(vm.OpenDecisionPending); vm.ConfirmOpenAnywayCommand.Execute(null); vm.OpenTask.Wait();
        Assert.Contains("0 of 1", vm.Status);
    }

    [Fact] public void AddItem_Persists_ReusesSharedItem_AndRejectsInvalidWithoutTrimming()
    {
        var p = Tmp(); using var repo = new JsonStore(p);
        var vm = new MainViewModel(repo, new NotImplementedLauncher());
        vm.NewName = "a"; vm.CreateWorkspaceCommand.Execute(null);
        vm.NewItemType = ItemType.Link; vm.NewTarget = "https://example.com/x"; vm.AddItemCommand.Execute(null);
        Assert.Equal("example.com", vm.Items.Single().Name); Assert.Equal("", vm.NewTarget);
        vm.NewName = "b"; vm.CreateWorkspaceCommand.Execute(null);
        vm.NewItemType = ItemType.Link; vm.NewTarget = "https://example.com/x"; vm.AddItemCommand.Execute(null);
        Assert.Single(repo.Load().Items); Assert.Equal(2, repo.Load().Workspaces.Count);
        vm.NewTarget = " https://example.com/y"; vm.AddItemCommand.Execute(null);
        Assert.StartsWith("Not added", vm.Status); Assert.Single(repo.Load().Items);
    }
    [Fact] public void Duplicate_add_in_the_same_workspace_asks_first_and_confirm_adds_a_second_instance()
    {
        var p = Tmp(); using var repo = new JsonStore(p);
        var vm = new MainViewModel(repo, new NotImplementedLauncher());
        vm.NewName = "a"; vm.CreateWorkspaceCommand.Execute(null);
        vm.NewItemType = ItemType.Link; vm.NewTarget = "https://example.com/x"; vm.AddItemCommand.Execute(null);
        Assert.Single(vm.Items); Assert.False(vm.DuplicatePending);
        vm.NewItemType = ItemType.Link; vm.NewTarget = "https://example.com/x"; vm.NewItemName = "again"; vm.AddItemCommand.Execute(null);
        Assert.True(vm.DuplicatePending); Assert.Single(vm.Items); Assert.Contains("already in the workspace", vm.DuplicateMessage);
        vm.ConfirmDuplicateCommand.Execute(null);
        Assert.False(vm.DuplicatePending); Assert.Equal(2, vm.Items.Count);
        Assert.Equal("again", vm.Items[1].Name); Assert.NotEqual(vm.Items[0].InstanceId, vm.Items[1].InstanceId);
        Assert.Single(repo.Load().Items);
        vm.RemoveItemCommand.Execute(vm.Items[1]); Assert.Single(vm.Items); // removing one instance keeps the other
    }
    [Fact] public void Duplicate_add_cancel_adds_nothing_and_editing_the_target_cancels_the_prompt()
    {
        var p = Tmp(); using var repo = new JsonStore(p);
        var vm = new MainViewModel(repo, new NotImplementedLauncher());
        vm.NewName = "a"; vm.CreateWorkspaceCommand.Execute(null);
        vm.NewItemType = ItemType.File; vm.NewTarget = "C:\\a.txt"; vm.AddItemCommand.Execute(null);
        vm.NewTarget = "C:\\a.txt"; vm.AddItemCommand.Execute(null);
        Assert.True(vm.DuplicatePending);
        vm.NewTarget = "C:\\b.txt"; Assert.False(vm.DuplicatePending); vm.AddItemCommand.Execute(null); // different target: no prompt
        Assert.Equal(2, vm.Items.Count);
        vm.NewTarget = "C:\\b.txt"; vm.AddItemCommand.Execute(null); Assert.True(vm.DuplicatePending);
        vm.CancelDuplicateCommand.Execute(null); Assert.False(vm.DuplicatePending); Assert.Equal(2, vm.Items.Count);
    }
    [Fact] public void Instance_name_is_stored_exactly_and_empty_falls_back_to_the_derived_name()
    {
        var p = Tmp(); using var repo = new JsonStore(p);
        var vm = new MainViewModel(repo, new NotImplementedLauncher());
        vm.NewName = "a"; vm.CreateWorkspaceCommand.Execute(null);
        vm.NewItemType = ItemType.File; vm.NewTarget = "C:\\docs\\spec.docx"; vm.NewItemName = "My spec"; vm.AddItemCommand.Execute(null);
        Assert.Equal("My spec", vm.Items.Single().Name); Assert.Equal("", vm.NewItemName);
        Assert.Equal("My spec", repo.Load().Workspaces.Single().Items.Single().Name);
        vm.NewTarget = "C:\\docs\\other.docx"; vm.AddItemCommand.Execute(null);
        Assert.Equal("other.docx", vm.Items[1].Name); Assert.Null(repo.Load().Workspaces.Single().Items[1].Name);
    }
    [Fact] public void Open_empty_workspace_shows_the_empty_workspace_state_and_opens_nothing()
    {
        var p = Tmp(); using var repo = new JsonStore(p);
        var vm = new MainViewModel(repo, new NotImplementedLauncher());
        vm.NewName = "empty"; vm.CreateWorkspaceCommand.Execute(null);
        vm.OpenWorkspaceCommand.Execute(null);
        Assert.True(vm.EmptyWorkspaceOpen); Assert.Equal("", vm.Status);
        vm.CloseEmptyWorkspaceCommand.Execute(null); Assert.False(vm.EmptyWorkspaceOpen);
    }
    [Fact] public void New_workspace_has_the_default_category()
    {
        var p = Tmp(); using var repo = new JsonStore(p);
        var vm = new MainViewModel(repo, new NotImplementedLauncher());
        vm.NewName = "plain"; vm.CreateWorkspaceCommand.Execute(null);
        Assert.Equal(Workspace.DefaultCategory, vm.Selected!.EffectiveCategory);
        Assert.Null(repo.Load().Workspaces.Single().Category);
    }
}
