using System.Windows.Input;
using System.Collections.ObjectModel;
using StartApp.Core;

namespace StartApp.Presentation;

public sealed partial class MainViewModel
{
    /// <summary>Reuses the category manager for a create/details draft or the filter catalog.</summary>
    public ICommand ManageCategoriesCommand { get; private set; } = null!;
    public bool CategoryAssignmentAvailable => _categoryEdit.Target != CategoryEditTarget.Catalog;
    /// <summary>Opens category assignment for the saved workspace from its card actions.</summary>
    public ICommand EditCategoryPopupCommand { get; private set; } = null!;
    /// <summary>Selection updates the active draft, or saves immediately for the card action.</summary>
    public ICommand PickCategoryCommand { get; private set; } = null!;
    public bool DuplicateNameOpen => _categoryEdit.DuplicateNameOpen;
    public ICommand AcknowledgeCategoryDuplicateCommand { get; private set; } = null!;
    public ICommand AddCategoryCommand { get; private set; } = null!;
    /// <summary>Starts renaming a custom category (built-ins never offer this).</summary>
    public ICommand BeginRenameCategoryCommand { get; private set; } = null!;
    public ICommand SaveRenameCategoryCommand { get; private set; } = null!;
    public ICommand CancelRenameCategoryCommand { get; private set; } = null!;
    /// <summary>Deletes a custom category; with assigned workspaces this opens the warning first.</summary>
    public ICommand DeleteCategoryCommand { get; private set; } = null!;
    public ICommand CloseCategoryPopupCommand { get; private set; } = null!;
    /// <summary>With several assigned workspaces, this button shows the list of them inside the warning.</summary>
    public ICommand ShowCategoryDeleteListCommand { get; private set; } = null!;
    public ICommand ConfirmCategoryDeleteCommand { get; private set; } = null!;
    public ICommand CancelCategoryDeleteCommand { get; private set; } = null!;

    private void InitCategoryEntryCommand()
    {
        EditCategoryPopupCommand = Cmd(p => BeginCategoryEdit((Workspace)p!), p => p is Workspace && !DuplicateNameOpen && !ReadOnly);
        ManageCategoriesCommand = Cmd(p => BeginCategoryManagement(p as string), _ => !DuplicateNameOpen && !ReadOnly);
    }

    private void InitCategoryEditCommands()
    {
        AcknowledgeCategoryDuplicateCommand = Cmd(_ => {
            if (_categoryEdit.DuplicateWasRename) RenameCategoryText = ""; else NewCategoryName = "";
            _categoryEdit.DuplicateNameOpen = false; RaiseCommands();
        }, _ => DuplicateNameOpen);
        PickCategoryCommand = Cmd(p => PickCategory((CategoryRow)p!), p => p is CategoryRow && CategoryAssignmentAvailable && !DuplicateNameOpen && !ReadOnly);
        AddCategoryCommand = Cmd(_ => AddCategory(), _ => CategoryEditOpen && !DuplicateNameOpen && !ReadOnly && NewCategoryName.Length > 0);
        BeginRenameCategoryCommand = Cmd(p => { _categoryEdit.RenameOriginalName = ((CategoryRow)p!).Name; RenameCategoryText = _categoryEdit.RenameOriginalName; RenameCategoryOpen = true; }, p => p is CategoryRow { IsBuiltIn: false } && !DuplicateNameOpen && !ReadOnly);
        SaveRenameCategoryCommand = Cmd(_ => SaveRenameCategory(), _ => RenameCategoryOpen && !DuplicateNameOpen && !ReadOnly && RenameCategoryText.Length > 0);
        CancelRenameCategoryCommand = Cmd(_ => RenameCategoryOpen = false, _ => RenameCategoryOpen);
        DeleteCategoryCommand = Cmd(p => BeginDeleteCategory((CategoryRow)p!), p => p is CategoryRow { IsBuiltIn: false } && !DuplicateNameOpen && !ReadOnly);
        CloseCategoryPopupCommand = Cmd(_ => CloseCategoryPopup(), _ => CategoryEditOpen);
        ShowCategoryDeleteListCommand = Cmd(_ => CategoryDeleteListShown = true, _ => CategoryDeletePending && !CategoryDeleteListShown);
        ConfirmCategoryDeleteCommand = Cmd(_ => ConfirmCategoryDelete(), _ => CategoryDeletePending && !DuplicateNameOpen && !ReadOnly);
        CancelCategoryDeleteCommand = Cmd(_ => CancelCategoryDelete("Category deletion cancelled, nothing was changed"), _ => CategoryDeletePending);
    }

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
        _categoryEdit.Target = CategoryEditTarget.SavedWorkspace; Raise(nameof(CategoryAssignmentAvailable));
        _categoryEdit.WorkspaceId = w.Id; CategoryEditOpen = true; RenameCategoryOpen = false; CancelCategoryDelete();
        RefreshCategories();
    }
    private void BeginCategoryManagement(string? target)
    {
        _categoryEdit.Target = target switch { "create" => CategoryEditTarget.CreateDraft, "details" => CategoryEditTarget.DetailsDraft, _ => CategoryEditTarget.Catalog };
        _categoryEdit.WorkspaceId = Selected?.Id ?? "";
        CategoryEditOpen = true; RenameCategoryOpen = false; CancelCategoryDelete();
        Raise(nameof(CategoryAssignmentAvailable)); RefreshCategories();
    }
    private string CategoryForTarget() => _categoryEdit.Target switch
    {
        CategoryEditTarget.CreateDraft => NewWorkspaceCategory,
        CategoryEditTarget.DetailsDraft => EditCategory,
        CategoryEditTarget.Catalog => "",
        _ => _current.Workspaces.FirstOrDefault(x => x.Id == _categoryEdit.WorkspaceId)?.EffectiveCategory ?? Workspace.DefaultCategory
    };
    // Catalog edits save immediately, but must never save or discard an unrelated workspace draft.
    private bool ApplyCategoryChange(Action<Catalog> change, string? oldName = null, string? replacement = null)
    {
        var create = (NewName, NewWorkspaceDescription, NewWorkspaceCategory, NewWorkspaceColor, NewWorkspaceImagePath);
        var details = (EditName, EditDescription, EditCategory);
        var ok = Apply(change);
        string Reconcile(string value) => ok && oldName != null && value == oldName ? replacement ?? Workspace.DefaultCategory : value;
        NewName = create.NewName; NewWorkspaceDescription = create.NewWorkspaceDescription;
        NewWorkspaceCategory = Reconcile(create.NewWorkspaceCategory); NewWorkspaceColor = create.NewWorkspaceColor; NewWorkspaceImagePath = create.NewWorkspaceImagePath;
        EditName = details.EditName; EditDescription = details.EditDescription; EditCategory = Reconcile(details.EditCategory);
        CurrentCategoryName = CategoryForTarget();
        return ok;
    }
    private void RefreshCategories()
    {
        var cats = new Catalog(_current).AllCategories();
        CategoryRows.Clear(); foreach (var c in cats) CategoryRows.Add(new CategoryRow(c, Catalog.IsBuiltInCategory(c)));
        // Rebuilding CategoryChoices with Clear() raises Reset, which wipes each bound ComboBox selection, and the
        // TwoWay SelectedItem binding pushes null back into the view model; restoring while the list is momentarily
        // empty leaves the combo visibly blank (seen on the create form). Rebuild diff-wise instead so an unchanged
        // selection never gets a Reset, and still capture/restore for the removed-item (rename/delete) case.
        var keepNew = _workspaceCreate.Category; var keepEdit = _workspaceDetails.Category;
        for (var i = CategoryChoices.Count - 1; i >= 0; i--) if (!cats.Contains(CategoryChoices[i])) CategoryChoices.RemoveAt(i);
        foreach (var c in cats) if (!CategoryChoices.Contains(c)) CategoryChoices.Add(c);
        _workspaceCreate.Category = keepNew; Raise(nameof(NewWorkspaceCategory)); Raise(nameof(CreateDirty));
        _workspaceDetails.Category = keepEdit; Raise(nameof(EditCategory)); RaiseDirty();
        CurrentCategoryName = CategoryForTarget();
    }
    private void PickCategory(CategoryRow row)
    {
        if (_categoryEdit.Target == CategoryEditTarget.CreateDraft) NewWorkspaceCategory = row.Name;
        else if (_categoryEdit.Target == CategoryEditTarget.DetailsDraft) EditCategory = row.Name;
        else if (_categoryEdit.Target == CategoryEditTarget.SavedWorkspace)
        {
            var previous = CategoryForTarget();
            var followsSavedCategory = Selected?.Id == _categoryEdit.WorkspaceId && EditCategory == previous;
            if (ApplyCategoryChange(c => c.SetCategory(_categoryEdit.WorkspaceId, row.Name)))
            {
                if (followsSavedCategory) EditCategory = row.Name;
                Status = "Category saved";
            }
        }
        CurrentCategoryName = CategoryForTarget();
    }
    private bool DuplicateCategoryName(string name, string? current = null)
    {
        if (name == current || !new Catalog(_current).AllCategories().Any(x => string.Equals(x, name, StringComparison.OrdinalIgnoreCase))) return false;
        _categoryEdit.DuplicateWasRename = current != null; _categoryEdit.DuplicateNameOpen = true; RaiseCommands();
        return true;
    }
    private void AddCategory()
    {
        if (Catalog.ValidateCategoryName(NewCategoryName) is { } categoryError) { FieldError("category", "שם הקטגוריה אינו תקין. הזינו שם באורך עד 100 תווים ללא תווי בקרה.", "Not saved: " + categoryError); return; }
        if (DuplicateCategoryName(NewCategoryName)) return;
        if (ApplyCategoryChange(c => c.AddCustomCategory(NewCategoryName))) { NewCategoryName = ""; Status = "Category added"; }
    }
    private void SaveRenameCategory()
    {
        if (Catalog.ValidateCategoryName(RenameCategoryText) is { } categoryError) { FieldError("category", "שם הקטגוריה אינו תקין. הזינו שם באורך עד 100 תווים ללא תווי בקרה.", "Not saved: " + categoryError); return; }
        if (DuplicateCategoryName(RenameCategoryText, _categoryEdit.RenameOriginalName)) return;
        if (ApplyCategoryChange(c => c.RenameCustomCategory(_categoryEdit.RenameOriginalName, RenameCategoryText), _categoryEdit.RenameOriginalName, RenameCategoryText)) { RenameCategoryOpen = false; Status = "Category renamed"; }
    }
    private void BeginDeleteCategory(CategoryRow row)
    {
        var used = new Catalog(_current).WorkspacesInCategory(row.Name);
        if (used.Count == 0)
        {
            // settled edge case: no assigned workspaces means no warning at all
            if (ApplyCategoryChange(c => c.DeleteCustomCategory(row.Name), row.Name)) Status = "Category deleted";
            return;
        }
        _categoryEdit.DeleteCategoryName = row.Name;
        CategoryDeleteWorkspaces.Clear(); foreach (var w in used) CategoryDeleteWorkspaces.Add(w.Name);
        CategoryDeleteMessage = used.Count == 1
            ? $"סביבת העבודה '{used[0].Name}' משויכת לקטגוריה '{row.Name}'. למחוק את הקטגוריה ולהעביר את סביבת העבודה לקטגוריה {Workspace.DefaultCategory}?"
            : $"{used.Count} סביבות עבודה משויכות לקטגוריה '{row.Name}'. למחוק את הקטגוריה ולהעביר אותן לקטגוריה {Workspace.DefaultCategory}?";
        CategoryDeleteListShown = used.Count == 1; // one workspace is shown directly; several sit behind the list button
        CategoryDeletePending = true;
    }
    private void ConfirmCategoryDelete()
    {
        if (ApplyCategoryChange(c => c.DeleteCustomCategory(_categoryEdit.DeleteCategoryName), _categoryEdit.DeleteCategoryName))
        {
            Status = "Category deleted, its workspaces moved to " + Workspace.DefaultCategory;
            CancelCategoryDelete();
        }
    }
    private void CancelCategoryDelete(string? status = null)
    {
        CategoryDeletePending = false; CategoryDeleteMessage = ""; CategoryDeleteWorkspaces.Clear(); CategoryDeleteListShown = false; _categoryEdit.DeleteCategoryName = "";
        if (status != null) Status = status;
    }
    private void CloseCategoryPopup()
    {
        if (DuplicateNameOpen) return;
        if (CategoryDeletePending) { CancelCategoryDelete("Category deletion cancelled, nothing was changed"); return; }
        if (RenameCategoryOpen) { RenameCategoryOpen = false; return; }
        CategoryEditOpen = false;
    }

}
