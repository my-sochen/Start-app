using StartApp.Core;
using StartApp.Presentation;
using StartApp.Storage.Json;
using Xunit;

public class AdvancedColorTests
{
    static JsonStore Repo() => new(Path.Combine(Path.GetTempPath(), "color-" + Guid.NewGuid().ToString("N"), "store.json"));
    [Fact] public void Advanced_create_selection_changes_draft_not_saved_workspace()
    {
        using var repo = Repo(); var vm = new MainViewModel(repo, new NotImplementedLauncher());
        vm.ShowCreateScreen(); vm.NewName = "draft"; vm.NewWorkspaceDescription = "description";
        vm.ApplyAdvancedColorCommand.Execute(new AdvancedColorChoice("create", "#12ab34"));
        Assert.Equal("#12AB34", vm.NewWorkspaceColor); Assert.Equal("draft", vm.NewName);
        Assert.Equal("description", vm.NewWorkspaceDescription); Assert.Empty(repo.Load().Workspaces);
        vm.CreateWorkspaceCommand.Execute(null); Assert.Equal("#12AB34", repo.Load().Workspaces.Single().Color);
    }
    [Fact] public void Advanced_design_selection_preserves_image_and_cancel_does_not_change_workspace()
    {
        using var repo = Repo(); var saved = repo.Mutate(c => c.CreateWorkspace("original"));
        var vm = new MainViewModel(repo, new NotImplementedLauncher()); vm.DesignCardCommand.Execute(vm.Workspaces.Single());
        vm.DesignImagePath = @"C:\example.png";
        vm.ApplyAdvancedColorCommand.Execute(new AdvancedColorChoice("design", "#123456"));
        Assert.Equal(@"C:\example.png", vm.DesignImagePath); Assert.True(vm.DesignDirty);
        vm.CloseDesignCommand.Execute(null); Assert.True(vm.UnsavedOpen); vm.ExitWithoutSavingCommand.Execute(null);
        Assert.Null(repo.Load().Workspaces.Single().Color); Assert.Null(repo.Load().Workspaces.Single().ImagePath);
    }
    [Theory] [InlineData("#xyzxyz")] [InlineData("")] [InlineData("#123")] [InlineData("#00112233")]
    public void Invalid_selection_cannot_apply(string color)
    {
        using var repo = Repo(); var vm = new MainViewModel(repo, new NotImplementedLauncher());
        var choice = new AdvancedColorChoice("create", color); Assert.False(vm.ApplyAdvancedColorCommand.CanExecute(choice));
        vm.ApplyAdvancedColorCommand.Execute(choice); Assert.Equal("", vm.NewWorkspaceColor);
    }
    [Fact] public void Closed_design_and_unknown_targets_cannot_apply()
    {
        using var repo = Repo(); var vm = new MainViewModel(repo, new NotImplementedLauncher());
        Assert.False(vm.ApplyAdvancedColorCommand.CanExecute(new AdvancedColorChoice("design", "#123456")));
        Assert.False(vm.ApplyAdvancedColorCommand.CanExecute(new AdvancedColorChoice("filter", "#123456")));
    }
}
