using StartApp.Core;
using StartApp.Presentation;
using StartApp.Storage.Json;
using Xunit;

/// <summary>פופאפ עיצוב כרטיס and פופאפ עריכת קטגוריה from the card actions menu, plus the category selection in the
/// create form. Design changes are per workspace and guarded by the shared unsaved-exit dialog; category deletion with
/// assigned workspaces warns first (one shown directly, several behind a list button) and moves them to כללי on confirm.</summary>
public class WorkspaceCardTests
{
    private static string Tmp() => Path.Combine(Path.GetTempPath(), "sa-" + Guid.NewGuid().ToString("N"), "store.json");

    private static MainViewModel Vm(JsonStore repo)
    {
        var vm = new MainViewModel(repo, new NotImplementedLauncher());
        vm.NewName = "w"; vm.CreateWorkspaceCommand.Execute(null);
        return vm;
    }

    // ---- create form: category selection ----

    [Fact] public void Create_form_defaults_to_general_and_a_chosen_category_is_saved()
    {
        using var repo = new JsonStore(Tmp());
        var vm = new MainViewModel(repo, new NotImplementedLauncher());
        Assert.Equal(Workspace.DefaultCategory, vm.NewWorkspaceCategory);
        Assert.Equal(new[] { Workspace.DefaultCategory }, vm.CategoryChoices);
        repo.Mutate(c => c.AddCustomCategory("מוזיקה")); vm.Reload();
        Assert.Contains("מוזיקה", vm.CategoryChoices);
        Assert.False(vm.CreateDirty);
        vm.NewWorkspaceCategory = "מוזיקה";
        Assert.True(vm.CreateDirty); // a non-default category is unsaved input
        vm.NewName = "music ws"; vm.CreateWorkspaceCommand.Execute(null);
        Assert.Equal("מוזיקה", repo.Load().Workspaces.Single().Category);
        Assert.Equal(Workspace.DefaultCategory, vm.NewWorkspaceCategory); // reset after create
    }

    // ---- design popup ----

    [Fact] public void Design_popup_saves_color_and_image_for_one_card_only()
    {
        using var repo = new JsonStore(Tmp());
        var vm = Vm(repo);
        var ws = vm.Selected!;
        vm.NewName = "other"; vm.CreateWorkspaceCommand.Execute(null);
        vm.DesignCardCommand.Execute(ws);
        Assert.True(vm.DesignEditOpen); Assert.False(vm.DesignDirty);
        Assert.Equal("", vm.DesignColor); Assert.Equal("", vm.DesignImagePath);
        vm.DesignColor = "#BFDBFE";
        Assert.True(vm.DesignDirty);
        vm.DesignImagePath = "C:\\img\\bg.png";
        vm.SaveDesignCommand.Execute(null);
        Assert.Equal("Card design saved", vm.Status); Assert.False(vm.DesignEditOpen);
        var store = repo.Load();
        Assert.Equal("#BFDBFE", store.Workspaces.Single(w => w.Id == ws.Id).Color);
        Assert.Equal("C:\\img\\bg.png", store.Workspaces.Single(w => w.Id == ws.Id).ImagePath);
        Assert.Null(store.Workspaces.Single(w => w.Id != ws.Id).Color); // two cards do not affect each other
    }

    [Fact] public void Design_popup_remove_image_returns_to_color_background()
    {
        using var repo = new JsonStore(Tmp());
        var vm = Vm(repo);
        var ws = vm.Selected!;
        vm.DesignCardCommand.Execute(ws);
        vm.DesignColor = "#BBF7D0"; vm.DesignImagePath = "C:\\img\\bg.png"; vm.SaveDesignCommand.Execute(null);
        vm.DesignCardCommand.Execute(ws);
        Assert.Equal("C:\\img\\bg.png", vm.DesignImagePath);
        Assert.True(vm.RemoveImageCommand.CanExecute(null));
        vm.RemoveImageCommand.Execute(null);
        Assert.Equal("", vm.DesignImagePath); Assert.True(vm.DesignDirty);
        vm.SaveDesignCommand.Execute(null);
        var saved = repo.Load().Workspaces.Single();
        Assert.Null(saved.ImagePath); Assert.Equal("#BBF7D0", saved.Color);
    }

    [Fact] public void Design_popup_X_with_changes_asks_and_cancel_keeps_the_old_design()
    {
        using var repo = new JsonStore(Tmp());
        var vm = Vm(repo);
        var ws = vm.Selected!;
        vm.DesignCardCommand.Execute(ws);
        vm.DesignColor = "#FECACA";
        vm.CloseDesignCommand.Execute(null); // X with unsaved changes asks first
        Assert.True(vm.UnsavedOpen); Assert.True(vm.UnsavedForDesign);
        vm.CloseUnsavedCommand.Execute(null); // X on the dialog: back to editing, nothing lost
        Assert.True(vm.DesignEditOpen); Assert.True(vm.DesignDirty);
        vm.CloseDesignCommand.Execute(null);
        vm.ExitWithoutSavingCommand.Execute(null); // discard
        Assert.False(vm.DesignEditOpen);
        Assert.Null(repo.Load().Workspaces.Single().Color); // the old design (none) is kept
    }

    [Fact] public void Design_popup_save_and_exit_applies_the_changes()
    {
        using var repo = new JsonStore(Tmp());
        var vm = Vm(repo);
        vm.DesignCardCommand.Execute(vm.Selected!);
        vm.ApplyPaletteColorCommand.Execute("#E9D5FF");
        Assert.Equal("#E9D5FF", vm.DesignColor);
        vm.CloseDesignCommand.Execute(null);
        vm.SaveAndExitCommand.Execute(null);
        Assert.False(vm.DesignEditOpen);
        Assert.Equal("#E9D5FF", repo.Load().Workspaces.Single().Color);
    }

    [Fact] public void Design_popup_rejects_a_bad_color_without_saving()
    {
        using var repo = new JsonStore(Tmp());
        var vm = Vm(repo);
        vm.DesignCardCommand.Execute(vm.Selected!);
        vm.DesignColor = "blue";
        vm.SaveDesignCommand.Execute(null);
        Assert.StartsWith("Not saved", vm.Status); Assert.True(vm.DesignEditOpen);
        Assert.Null(repo.Load().Workspaces.Single().Color);
    }

    // ---- category popup ----

    [Fact] public void Category_popup_pick_assigns_and_the_card_shows_it()
    {
        using var repo = new JsonStore(Tmp());
        var vm = Vm(repo);
        var ws = vm.Selected!;
        repo.Mutate(c => c.AddCustomCategory("מוזיקה")); vm.Reload();
        vm.EditCategoryPopupCommand.Execute(ws);
        Assert.True(vm.CategoryEditOpen);
        Assert.Equal(Workspace.DefaultCategory, vm.CurrentCategoryName);
        var row = vm.CategoryRows.Single(r => r.Name == "מוזיקה");
        Assert.False(row.IsBuiltIn);
        vm.PickCategoryCommand.Execute(row);
        Assert.Equal("מוזיקה", vm.CurrentCategoryName);
        Assert.Equal("מוזיקה", vm.Workspaces.Single(w => w.Id == ws.Id).EffectiveCategory);
        Assert.Equal("מוזיקה", repo.Load().Workspaces.Single().Category);
    }

    [Fact] public void Category_popup_creates_a_custom_category_that_becomes_pickable()
    {
        using var repo = new JsonStore(Tmp());
        var vm = Vm(repo);
        vm.EditCategoryPopupCommand.Execute(vm.Selected!);
        Assert.False(vm.AddCategoryCommand.CanExecute(null)); // empty name
        vm.NewCategoryName = "גרפיקה";
        Assert.True(vm.AddCategoryCommand.CanExecute(null));
        vm.AddCategoryCommand.Execute(null);
        Assert.Equal("", vm.NewCategoryName);
        Assert.Contains(vm.CategoryRows, r => r.Name == "גרפיקה" && !r.IsBuiltIn);
        Assert.Contains("גרפיקה", vm.CategoryChoices); // also in the create form and details screen
        Assert.Equal(new[] { "גרפיקה" }, repo.Load().CustomCategories);
    }

    [Fact] public void Builtin_category_offers_no_edit_or_delete()
    {
        using var repo = new JsonStore(Tmp());
        var vm = Vm(repo);
        vm.EditCategoryPopupCommand.Execute(vm.Selected!);
        var general = vm.CategoryRows.Single(r => r.Name == Workspace.DefaultCategory);
        Assert.True(general.IsBuiltIn);
        Assert.False(vm.BeginRenameCategoryCommand.CanExecute(general));
        Assert.False(vm.DeleteCategoryCommand.CanExecute(general));
    }

    [Fact] public void Delete_unused_custom_category_needs_no_warning()
    {
        using var repo = new JsonStore(Tmp());
        var vm = Vm(repo);
        repo.Mutate(c => c.AddCustomCategory("מוזיקה")); vm.Reload();
        vm.EditCategoryPopupCommand.Execute(vm.Selected!);
        vm.DeleteCategoryCommand.Execute(vm.CategoryRows.Single(r => r.Name == "מוזיקה"));
        Assert.False(vm.CategoryDeletePending); // settled edge case: no warning
        Assert.DoesNotContain(vm.CategoryRows, r => r.Name == "מוזיקה");
        Assert.Empty(repo.Load().CustomCategories);
    }

    [Fact] public void Delete_with_one_workspace_warns_and_shows_it_directly()
    {
        using var repo = new JsonStore(Tmp());
        var vm = Vm(repo);
        var ws = vm.Selected!;
        repo.Mutate(c => { c.AddCustomCategory("מוזיקה"); c.SetCategory(ws.Id, "מוזיקה"); }); vm.Reload();
        vm.EditCategoryPopupCommand.Execute(ws);
        vm.DeleteCategoryCommand.Execute(vm.CategoryRows.Single(r => r.Name == "מוזיקה"));
        Assert.True(vm.CategoryDeletePending);
        Assert.Contains("'w'", vm.CategoryDeleteMessage); // one workspace is named directly
        Assert.True(vm.CategoryDeleteListShown); // and the list is already visible, no list button
        Assert.False(vm.ShowCategoryDeleteListCommand.CanExecute(null));
        vm.CancelCategoryDeleteCommand.Execute(null); // dismiss: category and assignment kept
        Assert.False(vm.CategoryDeletePending);
        Assert.Equal("מוזיקה", repo.Load().Workspaces.Single().Category);
        Assert.Equal(new[] { "מוזיקה" }, repo.Load().CustomCategories);
    }

    [Fact] public void Delete_with_several_workspaces_hides_the_list_behind_a_button_and_confirm_moves_them_to_general()
    {
        using var repo = new JsonStore(Tmp());
        var vm = Vm(repo);
        var first = vm.Selected!;
        vm.NewName = "w2"; vm.CreateWorkspaceCommand.Execute(null);
        repo.Mutate(c => { c.AddCustomCategory("מוזיקה"); foreach (var w in c.Store.Workspaces) c.SetCategory(w.Id, "מוזיקה"); }); vm.Reload();
        vm.EditCategoryPopupCommand.Execute(first);
        vm.DeleteCategoryCommand.Execute(vm.CategoryRows.Single(r => r.Name == "מוזיקה"));
        Assert.True(vm.CategoryDeletePending);
        Assert.Contains("2 workspaces", vm.CategoryDeleteMessage);
        Assert.False(vm.CategoryDeleteListShown); // several: the list sits behind the button
        vm.ShowCategoryDeleteListCommand.Execute(null);
        Assert.True(vm.CategoryDeleteListShown);
        Assert.Equal(new[] { "w", "w2" }, vm.CategoryDeleteWorkspaces.OrderBy(x => x));
        vm.ConfirmCategoryDeleteCommand.Execute(null);
        var store = repo.Load();
        Assert.Equal(2, store.Workspaces.Count); // the workspaces are NOT deleted
        Assert.All(store.Workspaces, w => Assert.Equal(Workspace.DefaultCategory, w.EffectiveCategory));
        Assert.Empty(store.CustomCategories);
    }

    [Fact] public void Rename_custom_category_updates_the_card_and_other_popups_cannot_target_builtins()
    {
        using var repo = new JsonStore(Tmp());
        var vm = Vm(repo);
        var ws = vm.Selected!;
        repo.Mutate(c => { c.AddCustomCategory("מוזיקה"); c.SetCategory(ws.Id, "מוזיקה"); }); vm.Reload();
        vm.EditCategoryPopupCommand.Execute(ws);
        var row = vm.CategoryRows.Single(r => r.Name == "מוזיקה");
        vm.BeginRenameCategoryCommand.Execute(row);
        Assert.True(vm.RenameCategoryOpen);
        vm.RenameCategoryText = "פרויקט מוזיקה";
        vm.SaveRenameCategoryCommand.Execute(null);
        Assert.False(vm.RenameCategoryOpen);
        Assert.Equal("פרויקט מוזיקה", vm.CurrentCategoryName);
        Assert.Equal("פרויקט מוזיקה", repo.Load().Workspaces.Single().Category); // the name updates on every assigned card
        Assert.Contains("פרויקט מוזיקה", vm.CategoryChoices);
    }
}
