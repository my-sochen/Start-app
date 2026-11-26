using System.Collections.ObjectModel;
using StartApp.Core;

namespace StartApp.Presentation;

/// <summary>Owns one category editor's target, rename draft and deletion warning state.
/// No catalog, repository, commands, window or MainViewModel access. Host owns writes and command invalidation.</summary>
public enum CategoryEditTarget { SavedWorkspace, CreateDraft, DetailsDraft, Catalog }

public sealed class CategoryEditSession : ObservableBase
{
    public CategoryEditTarget Target { get; set; }
    public bool DuplicateNameOpen { get => _duplicateNameOpen; set => Set(ref _duplicateNameOpen, value); }
    private bool _duplicateNameOpen;
    public bool DuplicateWasRename { get; set; }
    public string WorkspaceId { get; set; } = "";
    public string RenameOriginalName { get; set; } = "";
    public string DeleteCategoryName { get; set; } = "";
    public ObservableCollection<string> CategoryDeleteWorkspaces { get; } = new();
    public bool CategoryEditOpen { get => _categoryEditOpen; set => Set(ref _categoryEditOpen, value); }
    private bool _categoryEditOpen = false;
    public string CurrentCategoryName { get => _currentCategoryName; set => Set(ref _currentCategoryName, value ?? ""); }
    private string _currentCategoryName = Workspace.DefaultCategory;
    public string NewCategoryName { get => _newCategoryName; set => Set(ref _newCategoryName, value ?? ""); }
    private string _newCategoryName = "";
    public bool RenameCategoryOpen { get => _renameCategoryOpen; set => Set(ref _renameCategoryOpen, value); }
    private bool _renameCategoryOpen = false;
    public string RenameCategoryText { get => _renameCategoryText; set => Set(ref _renameCategoryText, value ?? ""); }
    private string _renameCategoryText = "";
    public bool CategoryDeletePending { get => _categoryDeletePending; set => Set(ref _categoryDeletePending, value); }
    private bool _categoryDeletePending = false;
    public string CategoryDeleteMessage { get => _categoryDeleteMessage; set => Set(ref _categoryDeleteMessage, value ?? ""); }
    private string _categoryDeleteMessage = "";
    public bool CategoryDeleteListShown { get => _categoryDeleteListShown; set => Set(ref _categoryDeleteListShown, value); }
    private bool _categoryDeleteListShown = false;
}
