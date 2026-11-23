using System.Collections.ObjectModel;
using StartApp.Core;

namespace StartApp.Presentation;

public sealed partial class MainViewModel
{
    // ---- category popup (פופאפ עריכת קטגוריה, settled Nov 11): assign from the managed list; custom add/rename/delete with
    // the settled deletion warning (one workspace shown directly; several behind a list button; confirm moves them to כללי) ----
    /// <summary>One row in the category popup: built-ins expose no edit/delete (their commands guard on IsBuiltIn).</summary>
    public sealed record CategoryRow(string Name, bool IsBuiltIn);
    public ObservableCollection<CategoryRow> CategoryRows { get; } = new();
    /// <summary>The flat list for the create form and the details screen: built-ins first, then custom.</summary>
    public ObservableCollection<string> CategoryChoices { get; } = new();
    private readonly CategoryEditSession _categoryEdit = new();
    private void OnCategorySessionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e) => Raise(e.PropertyName!);
    public bool CategoryEditOpen { get => _categoryEdit.CategoryEditOpen; private set { _categoryEdit.CategoryEditOpen = value; RaiseCommands(); } }
    /// <summary>The category the workspace in the popup currently has (displayed as the selected one).</summary>
    public string CurrentCategoryName { get => _categoryEdit.CurrentCategoryName; private set => _categoryEdit.CurrentCategoryName = value; }
    public string NewCategoryName { get => _categoryEdit.NewCategoryName; set { _categoryEdit.NewCategoryName = value; RaiseCommands(); } }
    public bool RenameCategoryOpen { get => _categoryEdit.RenameCategoryOpen; private set => _categoryEdit.RenameCategoryOpen = value; }
    public string RenameCategoryText { get => _categoryEdit.RenameCategoryText; set { _categoryEdit.RenameCategoryText = value; RaiseCommands(); } }
    /// <summary>True while the delete-category warning waits for the user's answer.</summary>
    public bool CategoryDeletePending { get => _categoryEdit.CategoryDeletePending; private set { _categoryEdit.CategoryDeletePending = value; RaiseCommands(); } }
    public string CategoryDeleteMessage { get => _categoryEdit.CategoryDeleteMessage; private set => _categoryEdit.CategoryDeleteMessage = value; }
    /// <summary>Names of the workspaces assigned to the category being deleted.</summary>
    public ObservableCollection<string> CategoryDeleteWorkspaces => _categoryEdit.CategoryDeleteWorkspaces;
    /// <summary>With one assigned workspace the warning shows it directly; with several, this becomes true via the list button.</summary>
    public bool CategoryDeleteListShown { get => _categoryEdit.CategoryDeleteListShown; private set { _categoryEdit.CategoryDeleteListShown = value; RaiseCommands(); } }
    private void BeginCategoryEdit(Workspace w)
    {
        _categoryEdit.WorkspaceId = w.Id; CategoryEditOpen = true; RenameCategoryOpen = false; CancelCategoryDelete();
        RefreshCategories();
    }
    private void RefreshCategories()
    {
        var cats = new Catalog(_current).AllCategories();
        CategoryRows.Clear(); foreach (var c in cats) CategoryRows.Add(new CategoryRow(c, Catalog.IsBuiltInCategory(c)));
        // Rebuilding CategoryChoices with Clear() raises Reset, which wipes each bound ComboBox selection, and the
        // TwoWay SelectedItem binding pushes null back into the view model; restoring while the list is momentarily
        // empty leaves the combo visibly blank (seen on the create form). Rebuild diff-wise instead so an unchanged
        // selection never gets a Reset, and still capture/restore for the removed-item (rename/delete) case.
        var keepNew = _newWsCat; var keepEdit = _editCategory;
        for (var i = CategoryChoices.Count - 1; i >= 0; i--) if (!cats.Contains(CategoryChoices[i])) CategoryChoices.RemoveAt(i);
        foreach (var c in cats) if (!CategoryChoices.Contains(c)) CategoryChoices.Add(c);
        _newWsCat = keepNew; Raise(nameof(NewWorkspaceCategory)); Raise(nameof(CreateDirty));
        _editCategory = keepEdit; Raise(nameof(EditCategory)); RaiseDirty();
        var w = _current.Workspaces.FirstOrDefault(x => x.Id == _categoryEdit.WorkspaceId);
        CurrentCategoryName = w?.EffectiveCategory ?? Workspace.DefaultCategory;
    }
    private void PickCategory(CategoryRow row)
    {
        if (Apply(c => c.SetCategory(_categoryEdit.WorkspaceId, row.Name))) Status = "Category saved";
    }
    private void AddCategory()
    {
        if (Catalog.ValidateCategoryName(NewCategoryName) is { } categoryError) { FieldError("category", "שם הקטגוריה אינו תקין. הזינו שם באורך עד 100 תווים ללא תווי בקרה.", "Not saved: " + categoryError); return; }
        if (Apply(c => c.AddCustomCategory(NewCategoryName))) { NewCategoryName = ""; Status = "Category added"; }
    }
    private void SaveRenameCategory()
    {
        if (Catalog.ValidateCategoryName(RenameCategoryText) is { } categoryError) { FieldError("category", "שם הקטגוריה אינו תקין. הזינו שם באורך עד 100 תווים ללא תווי בקרה.", "Not saved: " + categoryError); return; }
        if (Apply(c => c.RenameCustomCategory(_categoryEdit.RenameOriginalName, RenameCategoryText))) { RenameCategoryOpen = false; Status = "Category renamed"; }
    }
    private void BeginDeleteCategory(CategoryRow row)
    {
        var used = new Catalog(_current).WorkspacesInCategory(row.Name);
        if (used.Count == 0)
        {
            // settled edge case: no assigned workspaces means no warning at all
            if (Apply(c => c.DeleteCustomCategory(row.Name))) Status = "Category deleted";
            return;
        }
        _categoryEdit.DeleteCategoryName = row.Name;
        CategoryDeleteWorkspaces.Clear(); foreach (var w in used) CategoryDeleteWorkspaces.Add(w.Name);
        CategoryDeleteMessage = used.Count == 1
            ? $"The workspace '{used[0].Name}' uses the category '{row.Name}'. Delete it and move the workspace to {Workspace.DefaultCategory}?"
            : $"{used.Count} workspaces use the category '{row.Name}'. Delete it and move them to {Workspace.DefaultCategory}?";
        CategoryDeleteListShown = used.Count == 1; // one workspace is shown directly; several sit behind the list button
        CategoryDeletePending = true;
    }
    private void ConfirmCategoryDelete()
    {
        if (Apply(c => c.DeleteCustomCategory(_categoryEdit.DeleteCategoryName))) Status = "Category deleted, its workspaces moved to " + Workspace.DefaultCategory;
        CancelCategoryDelete();
    }
    private void CancelCategoryDelete(string? status = null)
    {
        CategoryDeletePending = false; CategoryDeleteMessage = ""; CategoryDeleteWorkspaces.Clear(); CategoryDeleteListShown = false; _categoryEdit.DeleteCategoryName = "";
        if (status != null) Status = status;
    }
    private void CloseCategoryPopup()
    {
        if (CategoryDeletePending) { CancelCategoryDelete("Category deletion cancelled, nothing was changed"); return; }
        if (RenameCategoryOpen) { RenameCategoryOpen = false; return; }
        CategoryEditOpen = false;
    }

}
