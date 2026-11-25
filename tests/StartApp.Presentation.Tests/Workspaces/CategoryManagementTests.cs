using StartApp.Core;
using StartApp.Presentation;
using StartApp.Storage.Json;
using Xunit;

public class CategoryManagementTests
{
    private static JsonStore Repo() => new(Path.Combine(Path.GetTempPath(), "categories-" + Guid.NewGuid().ToString("N"), "store.json"));

    [Fact] public void Create_selection_stays_in_draft_and_catalog_edits_preserve_all_draft_fields()
    {
        using var repo = Repo(); var vm = new MainViewModel(repo, new NotImplementedLauncher());
        vm.ShowCreateScreen(); vm.NewName = "טיוטה"; vm.NewWorkspaceDescription = "תיאור"; vm.NewWorkspaceColor = "#112233";
        vm.ManageCategoriesCommand.Execute("create"); vm.NewCategoryName = "קטגוריה אישית"; vm.AddCategoryCommand.Execute(null);
        vm.PickCategoryCommand.Execute(vm.CategoryRows.Single(x => x.Name == "קטגוריה אישית"));
        Assert.Equal("קטגוריה אישית", vm.NewWorkspaceCategory); Assert.Empty(repo.Load().Workspaces);
        vm.NewCategoryName = "קטגוריה נוספת"; vm.AddCategoryCommand.Execute(null);
        Assert.Equal("טיוטה", vm.NewName); Assert.Equal("תיאור", vm.NewWorkspaceDescription); Assert.Equal("#112233", vm.NewWorkspaceColor);
        Assert.Equal("קטגוריה אישית", vm.NewWorkspaceCategory); Assert.True(vm.CreateDirty);
    }

    [Fact] public void Details_selection_does_not_commit_workspace_and_rename_preserves_unrelated_changes()
    {
        using var repo = Repo(); repo.Mutate(c => c.CreateWorkspace("מקור"));
        var vm = new MainViewModel(repo, new NotImplementedLauncher()); vm.OpenDetailsCommand.Execute(vm.Workspaces.Single());
        vm.EditName = "שם בטיוטה"; vm.EditDescription = "תיאור בטיוטה";
        vm.ManageCategoriesCommand.Execute("details"); vm.NewCategoryName = "קטגוריה אישית"; vm.AddCategoryCommand.Execute(null);
        var row = vm.CategoryRows.Single(x => x.Name == "קטגוריה אישית"); vm.PickCategoryCommand.Execute(row);
        Assert.Equal(Workspace.DefaultCategory, repo.Load().Workspaces.Single().EffectiveCategory);
        vm.BeginRenameCategoryCommand.Execute(row); vm.RenameCategoryText = "שם קטגוריה חדש"; vm.SaveRenameCategoryCommand.Execute(null);
        Assert.Equal("שם בטיוטה", vm.EditName); Assert.Equal("תיאור בטיוטה", vm.EditDescription); Assert.Equal("שם קטגוריה חדש", vm.EditCategory);
        Assert.Equal("מקור", repo.Load().Workspaces.Single().Name); Assert.True(vm.DetailsDirty);
    }

    [Fact] public void Catalog_only_entry_does_not_assign_selected_workspace()
    {
        using var repo = Repo(); repo.Mutate(c => c.CreateWorkspace("מקור"));
        var vm = new MainViewModel(repo, new NotImplementedLauncher()); vm.ManageCategoriesCommand.Execute("catalog");
        Assert.False(vm.CategoryAssignmentAvailable); Assert.False(vm.PickCategoryCommand.CanExecute(vm.CategoryRows.First()));
        vm.NewCategoryName = "קטגוריה אישית"; vm.AddCategoryCommand.Execute(null);
        Assert.Equal(Workspace.DefaultCategory, repo.Load().Workspaces.Single().EffectiveCategory);
    }

    [Fact] public void Delete_draft_category_falls_back_to_general_without_saving_workspace()
    {
        using var repo = Repo(); var vm = new MainViewModel(repo, new NotImplementedLauncher()); vm.ShowCreateScreen(); vm.NewName = "טיוטה";
        vm.ManageCategoriesCommand.Execute("create"); vm.NewCategoryName = "קטגוריה אישית"; vm.AddCategoryCommand.Execute(null);
        var row = vm.CategoryRows.Single(x => x.Name == "קטגוריה אישית"); vm.PickCategoryCommand.Execute(row); vm.DeleteCategoryCommand.Execute(row);
        Assert.Equal(Workspace.DefaultCategory, vm.NewWorkspaceCategory); Assert.Equal("טיוטה", vm.NewName); Assert.Empty(repo.Load().Workspaces);
    }
    [Fact] public void Saved_card_assignment_keeps_clean_details_in_sync_and_preserves_other_draft_fields()
    {
        using var repo = Repo(); repo.Mutate(c => { c.CreateWorkspace("original"); c.AddCustomCategory("personal"); });
        var vm = new MainViewModel(repo, new NotImplementedLauncher()); vm.OpenDetailsCommand.Execute(vm.Workspaces.Single());
        vm.EditName = "draft name"; vm.EditCategoryPopupCommand.Execute(vm.Selected!);
        vm.PickCategoryCommand.Execute(vm.CategoryRows.Single(x => x.Name == "personal"));
        Assert.Equal("personal", vm.EditCategory); Assert.Equal("draft name", vm.EditName);
        Assert.Equal("original", repo.Load().Workspaces.Single().Name); Assert.Equal("personal", repo.Load().Workspaces.Single().Category);
    }

    [Fact] public void Duplicate_add_keeps_input_until_acknowledged_then_clears_it_without_saving()
    {
        using var repo = Repo(); var vm = new MainViewModel(repo, new NotImplementedLauncher());
        vm.ShowCreateScreen(); vm.NewName = "draft"; vm.ManageCategoriesCommand.Execute("create");
        vm.NewCategoryName = Workspace.DefaultCategory; vm.AddCategoryCommand.Execute(null);
        Assert.Equal("draft", vm.NewName); Assert.Equal(Workspace.DefaultCategory, vm.NewWorkspaceCategory);
        Assert.True(vm.DuplicateNameOpen); Assert.Equal(Workspace.DefaultCategory, vm.NewCategoryName);
        Assert.False(vm.SaveBlocked); Assert.False(vm.AddCategoryCommand.CanExecute(null));
        vm.AcknowledgeCategoryDuplicateCommand.Execute(null);
        Assert.False(vm.DuplicateNameOpen); Assert.Equal("", vm.NewCategoryName); Assert.Empty(repo.Load().Workspaces);
    }
    [Fact] public void Duplicate_rename_clears_only_rename_input_when_acknowledged()
    {
        using var repo = Repo(); repo.Mutate(c => c.AddCustomCategory("personal"));
        var vm = new MainViewModel(repo, new NotImplementedLauncher()); vm.ManageCategoriesCommand.Execute("catalog");
        vm.NewCategoryName = "unrelated add input"; vm.BeginRenameCategoryCommand.Execute(vm.CategoryRows.Single(x => x.Name == "personal"));
        vm.RenameCategoryText = Workspace.DefaultCategory; vm.SaveRenameCategoryCommand.Execute(null);
        Assert.True(vm.DuplicateNameOpen); Assert.Equal(Workspace.DefaultCategory, vm.RenameCategoryText);
        vm.AcknowledgeCategoryDuplicateCommand.Execute(null);
        Assert.Equal("", vm.RenameCategoryText); Assert.Equal("unrelated add input", vm.NewCategoryName);
        Assert.Contains("personal", repo.Load().CustomCategories); Assert.True(vm.RenameCategoryOpen);
    }
}
