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
    [Fact] public void History_commits_only_final_advanced_color_with_workspace_and_survives_restart()
    {
        using var repo = Repo(); var vm = new MainViewModel(repo, new NotImplementedLauncher()); vm.ShowCreateScreen(); vm.NewName = "draft";
        vm.ApplyAdvancedColorCommand.Execute(new AdvancedColorChoice("create", "#112233"));
        vm.ApplyAdvancedColorCommand.Execute(new AdvancedColorChoice("create", "#445566")); Assert.Empty(repo.Load().AdvancedColorHistory);
        vm.CreateWorkspaceCommand.Execute(null);
        Assert.Equal(new[] { "#445566" }, repo.Load().AdvancedColorHistory);
        var reopened = new MainViewModel(repo, new NotImplementedLauncher()); Assert.Contains("#445566", reopened.FilterColorOptions);
    }
    [Fact] public void Discarding_advanced_create_does_not_leak_history_to_next_create()
    {
        using var repo = Repo(); var vm = new MainViewModel(repo, new NotImplementedLauncher()); vm.ShowCreateScreen(); vm.NewName = "discard";
        vm.ApplyAdvancedColorCommand.Execute(new AdvancedColorChoice("create", "#112233")); vm.LeaveCreateScreen();
        Assert.True(vm.UnsavedOpen); vm.ExitWithoutSavingCommand.Execute(null); Assert.Empty(repo.Load().AdvancedColorHistory);
        vm.ShowCreateScreen(); vm.NewName = "new"; vm.CreateWorkspaceCommand.Execute(null); Assert.Empty(repo.Load().AdvancedColorHistory);
    }
    [Fact] public void Preset_replacing_advanced_choice_does_not_remember_abandoned_color()
    {
        using var repo = Repo(); var vm = new MainViewModel(repo, new NotImplementedLauncher()); vm.ShowCreateScreen(); vm.NewName = "new";
        vm.ApplyAdvancedColorCommand.Execute(new AdvancedColorChoice("create", "#112233")); vm.ApplyCreatePaletteColorCommand.Execute(vm.DesignPalette[1]);
        vm.CreateWorkspaceCommand.Execute(null); Assert.Empty(repo.Load().AdvancedColorHistory); Assert.Equal(vm.DesignPalette[1], repo.Load().Workspaces.Single().Color);
    }
    [Fact] public void Design_history_commits_on_save_but_not_on_discard_and_is_deduplicated()
    {
        using var repo = Repo(); repo.Mutate(c => c.CreateWorkspace("old")); var vm = new MainViewModel(repo, new NotImplementedLauncher());
        vm.DesignCardCommand.Execute(vm.Workspaces.Single()); vm.ApplyAdvancedColorCommand.Execute(new AdvancedColorChoice("design", "#112233"));
        vm.CloseDesignCommand.Execute(null); vm.ExitWithoutSavingCommand.Execute(null); Assert.Empty(repo.Load().AdvancedColorHistory);
        vm.DesignCardCommand.Execute(vm.Workspaces.Single()); vm.ApplyAdvancedColorCommand.Execute(new AdvancedColorChoice("design", "#445566"));
        vm.SaveDesignCommand.Execute(null); Assert.Equal(new[] { "#445566" }, repo.Load().AdvancedColorHistory);
        vm.DesignCardCommand.Execute(vm.Workspaces.Single()); vm.ApplyAdvancedColorCommand.Execute(new AdvancedColorChoice("design", "#445566")); vm.DesignImagePath = @"C:\new.png";
        vm.SaveDesignCommand.Execute(null); Assert.Single(repo.Load().AdvancedColorHistory);
    }
    [Fact] public void Failed_save_retains_pending_advanced_selection_for_atomic_retry()
    {
        using var repo = Repo(); var failing = new RetryRepo(repo); var vm = new MainViewModel(failing, new NotImplementedLauncher());
        vm.ShowCreateScreen(); vm.NewName = "retry"; vm.ApplyAdvancedColorCommand.Execute(new AdvancedColorChoice("create", "#112233"));
        vm.CreateWorkspaceCommand.Execute(null); Assert.Empty(repo.Load().AdvancedColorHistory); Assert.Empty(repo.Load().Workspaces); Assert.Equal("#112233", vm.NewWorkspaceColor);
        failing.Fail = false; vm.CreateWorkspaceCommand.Execute(null); Assert.Single(repo.Load().Workspaces); Assert.Equal(new[] { "#112233" }, repo.Load().AdvancedColorHistory);
    }
    sealed class RetryRepo(IStoreRepository inner) : IStoreRepository
    {
        public bool Fail = true;
        public Store Load() => inner.Load();
        public Store Mutate(Action<Catalog> change) => Fail ? throw new IOException("simulated failure") : inner.Mutate(change);
        public void Dispose() { }
    }
    [Fact] public async Task Capture_history_is_draft_until_capture_save_succeeds()
    {
        using var repo = Repo(); var vm = new MainViewModel(repo, new NotImplementedLauncher(), capturer: new EmptyCapturer()) { ScanMinVisible = TimeSpan.Zero };
        vm.CaptureNewCommand.Execute(null); await vm.CurrentScan!; vm.NewName = "capture";
        vm.ApplyAdvancedColorCommand.Execute(new AdvancedColorChoice("create", "#112233")); Assert.Empty(repo.Load().AdvancedColorHistory);
        vm.CaptureCreateCommand.Execute(null); Assert.Equal(new[] { "#112233" }, repo.Load().AdvancedColorHistory); Assert.Single(repo.Load().Workspaces);
    }
    [Fact] public async Task Capture_without_selection_does_not_add_advanced_history()
    {
        using var repo = Repo(); var vm = new MainViewModel(repo, new NotImplementedLauncher(), capturer: new EmptyCapturer()) { ScanMinVisible = TimeSpan.Zero };
        vm.CaptureNewCommand.Execute(null); await vm.CurrentScan!; vm.NewName = "no selection";
        Assert.Equal("", vm.NewWorkspaceColor);
        vm.CaptureCreateCommand.Execute(null);
        Assert.Single(repo.Load().Workspaces); Assert.Empty(repo.Load().AdvancedColorHistory);
    }
    [Fact] public async Task Capture_preset_click_must_save_non_default_color_without_history()
    {
        using var repo = Repo(); var vm = new MainViewModel(repo, new NotImplementedLauncher(), capturer: new EmptyCapturer()) { ScanMinVisible = TimeSpan.Zero };
        vm.CaptureNewCommand.Execute(null); await vm.CurrentScan!; vm.NewName = "green preset";
        vm.ApplyCreatePaletteColorCommand.Execute("#8BAA9D"); Assert.Equal("#8BAA9D", vm.NewWorkspaceColor);
        vm.CaptureCreateCommand.Execute(null); Assert.Equal("#8BAA9D", repo.Load().Workspaces.Single().Color); Assert.Empty(repo.Load().AdvancedColorHistory);
    }
    sealed class EmptyCapturer : IOpenItemCapturer { public CaptureScan Scan() => new(Array.Empty<CapturedItem>(), Array.Empty<string>()); }
    [Fact] public void Category_catalog_save_does_not_commit_or_lose_pending_color_history()
    {
        using var repo = Repo(); var vm = new MainViewModel(repo, new NotImplementedLauncher()); vm.ShowCreateScreen(); vm.NewName = "draft";
        vm.ApplyAdvancedColorCommand.Execute(new AdvancedColorChoice("create", "#112233"));
        vm.ManageCategoriesCommand.Execute("create"); vm.NewCategoryName = "personal"; vm.AddCategoryCommand.Execute(null);
        Assert.Empty(repo.Load().AdvancedColorHistory); Assert.Empty(repo.Load().Workspaces); Assert.Equal("#112233", vm.NewWorkspaceColor);
        vm.CloseCategoryPopupCommand.Execute(null); vm.CreateWorkspaceCommand.Execute(null); Assert.Equal(new[] { "#112233" }, repo.Load().AdvancedColorHistory);
    }
    [Fact] public void Design_failure_retains_draft_and_history_for_retry()
    {
        using var repo = Repo(); repo.Mutate(c => c.CreateWorkspace("old")); var failing = new RetryRepo(repo);
        var vm = new MainViewModel(failing, new NotImplementedLauncher()); vm.DesignCardCommand.Execute(vm.Workspaces.Single());
        vm.ApplyAdvancedColorCommand.Execute(new AdvancedColorChoice("design", "#112233")); vm.SaveDesignCommand.Execute(null);
        Assert.True(vm.DesignEditOpen); Assert.Equal("#112233", vm.DesignColor); Assert.Single(repo.Load().Workspaces); Assert.Empty(repo.Load().AdvancedColorHistory);
        failing.Fail = false; vm.SaveDesignCommand.Execute(null); Assert.False(vm.DesignEditOpen); Assert.Equal("#112233", repo.Load().Workspaces.Single().Color); Assert.Equal(new[] { "#112233" }, repo.Load().AdvancedColorHistory);
    }
    [Fact] public async Task Capture_failure_retains_draft_and_history_for_retry()
    {
        using var repo = Repo(); var failing = new RetryRepo(repo);
        var vm = new MainViewModel(failing, new NotImplementedLauncher(), capturer: new EmptyCapturer()) { ScanMinVisible = TimeSpan.Zero };
        vm.CaptureNewCommand.Execute(null); await vm.CurrentScan!; vm.NewName = "retry capture";
        vm.ApplyAdvancedColorCommand.Execute(new AdvancedColorChoice("create", "#112233")); vm.CaptureCreateCommand.Execute(null);
        Assert.True(vm.CaptureOpen); Assert.Equal("#112233", vm.NewWorkspaceColor); Assert.Empty(repo.Load().Workspaces); Assert.Empty(repo.Load().AdvancedColorHistory);
        failing.Fail = false; vm.CaptureCreateCommand.Execute(null); Assert.Single(repo.Load().Workspaces); Assert.Equal(new[] { "#112233" }, repo.Load().AdvancedColorHistory);
    }
}
