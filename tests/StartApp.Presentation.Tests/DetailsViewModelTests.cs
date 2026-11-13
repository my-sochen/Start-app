using StartApp.Core;
using StartApp.Presentation;
using StartApp.Storage.Json;
using Xunit;

public class DetailsViewModelTests
{
    private static string Tmp() => Path.Combine(Path.GetTempPath(), "sa-" + Guid.NewGuid().ToString("N"), "store.json");
    private static MainViewModel Vm(JsonStore repo) => new(repo, new NotImplementedLauncher());

    [Fact] public void Create_opens_the_details_screen_with_the_spec_defaults()
    {
        using var repo = new JsonStore(Tmp()); var vm = Vm(repo);
        Assert.True(vm.InOverview);
        vm.NewName = "my ws"; vm.NewWorkspaceDescription = "about it"; vm.CreateWorkspaceCommand.Execute(null);
        Assert.True(vm.InDetails); Assert.False(vm.InOverview);
        Assert.Equal("my ws", vm.EditName); Assert.Equal("about it", vm.EditDescription);
        Assert.Equal(Workspace.DefaultCategory, vm.EditCategory);
        Assert.NotEqual("unknown", vm.CreatedAtText);
        Assert.True(vm.ShowEmptyItemsMessage); Assert.Equal("עדיין אין אייטמים בסביבה", MainViewModel.EmptyItemsMessage);
        Assert.Equal("", vm.NewName); Assert.Equal("", vm.NewWorkspaceDescription);
        var saved = repo.Load().Workspaces.Single();
        Assert.Equal("about it", saved.Description); Assert.NotNull(saved.CreatedAt);
    }
    [Fact] public void Direct_edit_shows_save_and_save_persists_without_touching_other_fields()
    {
        using var repo = new JsonStore(Tmp()); var vm = Vm(repo);
        vm.NewName = "w"; vm.CreateWorkspaceCommand.Execute(null);
        Assert.False(vm.DetailsDirty);
        vm.EditDescription = "new desc"; Assert.True(vm.DetailsDirty);
        vm.SaveDetailsCommand.Execute(null);
        Assert.False(vm.DetailsDirty); Assert.Equal("Workspace details saved", vm.Status);
        var w = repo.Load().Workspaces.Single();
        Assert.Equal("new desc", w.Description); Assert.Equal("w", w.Name); Assert.Null(w.Category);
        vm.EditName = "w2"; vm.EditCategory = "music"; Assert.True(vm.DetailsDirty);
        vm.SaveDetailsCommand.Execute(null);
        w = repo.Load().Workspaces.Single();
        Assert.Equal("w2", w.Name); Assert.Equal("music", w.Category);
    }
    [Fact] public void Save_rejects_an_empty_name_and_keeps_editing()
    {
        using var repo = new JsonStore(Tmp()); var vm = Vm(repo);
        vm.NewName = "w"; vm.CreateWorkspaceCommand.Execute(null);
        vm.EditName = "   "; vm.SaveDetailsCommand.Execute(null);
        Assert.Contains("Not saved", vm.Status); Assert.True(vm.DetailsDirty); Assert.True(vm.InDetails);
        Assert.Equal("w", repo.Load().Workspaces.Single().Name);
    }
    [Fact] public void Back_with_unsaved_changes_opens_the_dialog_and_x_returns_to_editing()
    {
        using var repo = new JsonStore(Tmp()); var vm = Vm(repo);
        vm.NewName = "w"; vm.CreateWorkspaceCommand.Execute(null);
        vm.EditName = "changed";
        vm.BackCommand.Execute(null);
        Assert.True(vm.UnsavedOpen); Assert.True(vm.InDetails); // dialog, still in details
        vm.CloseUnsavedCommand.Execute(null); // X: back to editing, nothing lost
        Assert.False(vm.UnsavedOpen); Assert.True(vm.InDetails); Assert.Equal("changed", vm.EditName);
    }
    [Fact] public void Exit_without_saving_discards_and_leaves()
    {
        using var repo = new JsonStore(Tmp()); var vm = Vm(repo);
        vm.NewName = "w"; vm.CreateWorkspaceCommand.Execute(null);
        vm.EditName = "changed"; vm.BackCommand.Execute(null);
        vm.ExitWithoutSavingCommand.Execute(null);
        Assert.False(vm.UnsavedOpen); Assert.True(vm.InOverview);
        Assert.Equal("w", repo.Load().Workspaces.Single().Name);
        vm.OpenDetailsCommand.Execute(vm.Workspaces.Single()); // re-entering shows the saved values
        Assert.Equal("w", vm.EditName);
    }
    [Fact] public void Save_and_exit_saves_then_leaves()
    {
        using var repo = new JsonStore(Tmp()); var vm = Vm(repo);
        vm.NewName = "w"; vm.CreateWorkspaceCommand.Execute(null);
        vm.EditName = "changed"; vm.BackCommand.Execute(null);
        vm.SaveAndExitCommand.Execute(null);
        Assert.False(vm.UnsavedOpen); Assert.True(vm.InOverview);
        Assert.Equal("changed", repo.Load().Workspaces.Single().Name);
    }
    [Fact] public void Window_close_is_guarded_the_same_way()
    {
        using var repo = new JsonStore(Tmp()); var vm = Vm(repo);
        vm.NewName = "w"; vm.CreateWorkspaceCommand.Execute(null);
        Assert.True(vm.RequestAppClose()); // nothing unsaved: closes
        vm.EditName = "changed";
        Assert.False(vm.RequestAppClose()); Assert.True(vm.UnsavedOpen);
        var approved = false; vm.AppCloseApproved += () => approved = true;
        vm.ExitWithoutSavingCommand.Execute(null);
        Assert.True(approved);
    }
    [Fact] public void Empty_workspace_open_from_overview_shows_the_dialog_and_the_button_leads_to_details()
    {
        using var repo = new JsonStore(Tmp()); var vm = Vm(repo);
        vm.NewName = "e"; vm.CreateWorkspaceCommand.Execute(null);
        vm.BackCommand.Execute(null); Assert.True(vm.InOverview);
        vm.OpenWorkspaceForCommand.Execute(vm.Workspaces.Single());
        Assert.True(vm.EmptyWorkspaceOpen); Assert.Equal("סביבת עבודה ריקה", MainViewModel.EmptyWorkspaceTitle);
        Assert.True(vm.InOverview); // the dialog did not navigate by itself
        vm.GoToDetailsCommand.Execute(null);
        Assert.False(vm.EmptyWorkspaceOpen); Assert.True(vm.InDetails); Assert.True(vm.ShowEmptyItemsMessage);
    }
    [Fact] public void Adding_the_first_item_hides_the_empty_state_message()
    {
        using var repo = new JsonStore(Tmp()); var vm = Vm(repo);
        vm.NewName = "w"; vm.CreateWorkspaceCommand.Execute(null);
        Assert.True(vm.ShowEmptyItemsMessage);
        vm.NewItemType = ItemType.File; vm.NewTarget = "C:\\a.txt"; vm.AddItemCommand.Execute(null);
        Assert.False(vm.ShowEmptyItemsMessage);
    }
}
