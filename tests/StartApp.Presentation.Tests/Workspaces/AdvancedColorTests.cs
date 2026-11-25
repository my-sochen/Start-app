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
    [Fact] public void Filter_options_include_history_without_live_cards_and_merge_legacy_colors_without_duplicates()
    {
        using var repo = Repo();
        repo.Mutate(c => { WorkspaceColors.Remember(c.Store, "#123456"); WorkspaceColors.Remember(c.Store, "#AABBCC"); var w = c.CreateWorkspace("legacy"); c.SetCardDesign(w.Id, "#aabbcc", null); });
        var vm = new MainViewModel(repo, new NotImplementedLauncher());
        Assert.Equal(new[] { "#123456", "#AABBCC" }, vm.FilterColorOptions);
        vm.ToggleColorFilter("#123456"); Assert.Empty(vm.DisplayWorkspaces); Assert.True(vm.HasActiveFilters);
        vm.ToggleColorFilter("#123456"); Assert.Single(vm.DisplayWorkspaces);
    }
    [Fact] public void Read_only_store_disables_advanced_color_and_cannot_mutate_draft()
    {
        using var repo = new ReadOnlyRepo(); var vm = new MainViewModel(repo, new NotImplementedLauncher());
        Assert.True(vm.ReadOnly); Assert.False(vm.CanChooseWorkspaceColor);
        var choice = new AdvancedColorChoice("create", "#123456");
        Assert.False(vm.ApplyAdvancedColorCommand.CanExecute(choice)); vm.ApplyAdvancedColorCommand.Execute(choice);
        Assert.Equal("", vm.NewWorkspaceColor);
    }
    sealed class ReadOnlyRepo : IStoreRepository
    {
        public Store Load() => throw new StoreTooNewException(999, 6);
        public Store Mutate(Action<Catalog> change) => throw new Exception("must not write");
        public void Dispose() { }
    }
    [Fact] public void Nine_muted_base_colors_are_shared_and_old_saved_colors_remain_selectable_separately()
    {
        using var repo = Repo(); repo.Mutate(c => { var w = c.CreateWorkspace("old"); c.SetCardDesign(w.Id, "#BFDBFE", null); });
        var vm = new MainViewModel(repo, new NotImplementedLauncher());
        Assert.Equal(9, vm.DesignPalette.Count); Assert.Equal(9, vm.DesignPalette.Distinct().Count());
        Assert.Equal(WorkspaceColors.BasePalette, vm.DesignPalette); Assert.Contains("#7189BB", vm.DesignPalette);
        Assert.Contains("#BFDBFE", vm.AdvancedColorOptions); Assert.True(vm.HasAdvancedColorOptions);
        Assert.DoesNotContain(vm.AdvancedColorOptions, c => vm.DesignPalette.Contains(c));
        Assert.Equal("#BFDBFE", repo.Load().Workspaces.Single().Color);
    }
}
