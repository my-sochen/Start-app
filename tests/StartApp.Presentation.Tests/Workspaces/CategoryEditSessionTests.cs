using StartApp.Core;
using StartApp.Presentation;

public class CategoryEditSessionTests
{
    [Fact] public void New_session_is_closed_and_has_no_target_draft_or_warning()
    {
        var s = new CategoryEditSession();
        Assert.False(s.CategoryEditOpen); Assert.False(s.RenameCategoryOpen);
        Assert.False(s.CategoryDeletePending); Assert.False(s.CategoryDeleteListShown);
        Assert.Equal(Workspace.DefaultCategory, s.CurrentCategoryName);
        Assert.Equal("", s.WorkspaceId); Assert.Equal("", s.NewCategoryName);
        Assert.Equal("", s.RenameOriginalName); Assert.Equal("", s.RenameCategoryText);
        Assert.Equal("", s.DeleteCategoryName); Assert.Equal("", s.CategoryDeleteMessage);
        Assert.Empty(s.CategoryDeleteWorkspaces);
    }

    [Fact] public void Every_visible_state_notifies_once_on_change_not_on_same_value()
    {
        var s = new CategoryEditSession(); var names = new List<string?>();
        s.PropertyChanged += (_, e) => names.Add(e.PropertyName);
        void Assign()
        {
            s.CategoryEditOpen = true; s.CurrentCategoryName = "custom"; s.NewCategoryName = "new";
            s.RenameCategoryOpen = true; s.RenameCategoryText = "renamed";
            s.CategoryDeletePending = true; s.CategoryDeleteMessage = "warning"; s.CategoryDeleteListShown = true;
        }
        Assign(); Assign();
        Assert.Equal(new[] { "CategoryEditOpen", "CurrentCategoryName", "NewCategoryName", "RenameCategoryOpen", "RenameCategoryText", "CategoryDeletePending", "CategoryDeleteMessage", "CategoryDeleteListShown" }, names);
    }

    [Fact] public void Null_draft_inputs_are_empty_without_trimming_other_values()
    {
        var s = new CategoryEditSession { NewCategoryName = "  draft  ", RenameCategoryText = "  rename  " };
        Assert.Equal("  draft  ", s.NewCategoryName); Assert.Equal("  rename  ", s.RenameCategoryText);
        s.NewCategoryName = null!; s.RenameCategoryText = null!;
        Assert.Equal("", s.NewCategoryName); Assert.Equal("", s.RenameCategoryText);
    }

    [Fact] public void Sessions_do_not_share_identity_drafts_warning_or_workspace_list()
    {
        var a = new CategoryEditSession { WorkspaceId = "a", RenameOriginalName = "old", RenameCategoryText = "new", DeleteCategoryName = "delete", CategoryDeletePending = true };
        var b = new CategoryEditSession { WorkspaceId = "b" };
        a.CategoryDeleteWorkspaces.Add("workspace a");
        Assert.Equal("b", b.WorkspaceId); Assert.Equal("", b.RenameOriginalName);
        Assert.Equal("", b.RenameCategoryText); Assert.Equal("", b.DeleteCategoryName);
        Assert.False(b.CategoryDeletePending); Assert.Empty(b.CategoryDeleteWorkspaces);
        a.CategoryEditOpen = false;
        Assert.Equal("a", a.WorkspaceId); Assert.Equal("new", a.RenameCategoryText);
        Assert.Single(a.CategoryDeleteWorkspaces);
    }
}
