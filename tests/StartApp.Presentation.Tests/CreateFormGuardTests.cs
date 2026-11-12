using StartApp.Core;
using StartApp.Presentation;
using StartApp.Storage.Json;
using Xunit;

/// <summary>The unsaved-exit dialog (דיאלוג יציאה עם שינויים שלא נשמרו) also guards the create form
/// (spec: חל גם על טופס יצירת סביבה), plus the card actions menu shape.</summary>
public class CreateFormGuardTests
{
    private static string Tmp() => Path.Combine(Path.GetTempPath(), "sa-" + Guid.NewGuid().ToString("N"), "store.json");

    [Fact] public void Dirty_create_form_blocks_app_close_with_create_context()
    {
        using var repo = new JsonStore(Tmp()); var vm = new MainViewModel(repo, new NotImplementedLauncher());
        Assert.True(vm.RequestAppClose()); // nothing dirty: close allowed
        vm.NewWorkspaceDescription = "half-typed";
        Assert.True(vm.CreateDirty);
        Assert.False(vm.RequestAppClose()); // blocked
        Assert.True(vm.UnsavedOpen); Assert.True(vm.UnsavedForCreate);
    }

    [Fact] public void Save_and_exit_creates_the_workspace_and_approves_the_close()
    {
        using var repo = new JsonStore(Tmp()); var vm = new MainViewModel(repo, new NotImplementedLauncher());
        bool approved = false; vm.AppCloseApproved += () => approved = true;
        vm.NewName = "ws from dialog"; vm.NewWorkspaceDescription = "d";
        Assert.False(vm.RequestAppClose());
        vm.SaveAndExitCommand.Execute(null);
        Assert.True(approved); Assert.False(vm.UnsavedOpen); Assert.False(vm.CreateDirty);
        Assert.Equal("ws from dialog", repo.Load().Workspaces.Single().Name);
    }

    [Fact] public void Save_and_exit_with_an_empty_name_cancels_the_close_and_keeps_the_input()
    {
        using var repo = new JsonStore(Tmp()); var vm = new MainViewModel(repo, new NotImplementedLauncher());
        bool approved = false; vm.AppCloseApproved += () => approved = true;
        vm.NewWorkspaceDescription = "no name yet";
        Assert.False(vm.RequestAppClose());
        vm.SaveAndExitCommand.Execute(null);
        Assert.False(approved); Assert.False(vm.UnsavedOpen); Assert.True(vm.CreateDirty);
        Assert.Equal("Not saved: workspace name is required", vm.Status);
        Assert.Empty(repo.Load().Workspaces);
    }

    [Fact] public void Exit_without_saving_discards_the_form_and_approves_the_close()
    {
        using var repo = new JsonStore(Tmp()); var vm = new MainViewModel(repo, new NotImplementedLauncher());
        bool approved = false; vm.AppCloseApproved += () => approved = true;
        vm.NewName = "discard me";
        Assert.False(vm.RequestAppClose());
        vm.ExitWithoutSavingCommand.Execute(null);
        Assert.True(approved); Assert.Equal("", vm.NewName); Assert.False(vm.CreateDirty);
        Assert.Empty(repo.Load().Workspaces);
    }

    [Fact] public void Add_item_from_the_card_menu_opens_details_and_focuses_the_add_form()
    {
        using var repo = new JsonStore(Tmp()); var vm = new MainViewModel(repo, new NotImplementedLauncher());
        vm.NewName = "w"; vm.CreateWorkspaceCommand.Execute(null);
        vm.BackCommand.Execute(null); Assert.True(vm.InOverview);
        bool focused = false; vm.FocusAddRequested += () => focused = true;
        var w = vm.Workspaces.Single();
        Assert.True(vm.AddItemToCommand.CanExecute(w));
        vm.AddItemToCommand.Execute(w);
        Assert.True(vm.InDetails); Assert.True(focused);
    }
}
