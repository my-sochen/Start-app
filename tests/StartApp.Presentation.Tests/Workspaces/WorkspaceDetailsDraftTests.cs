using StartApp.Core;
using StartApp.Presentation;

public class WorkspaceDetailsDraftTests
{
    [Fact] public void Load_copies_current_values_and_normalizes_absent_description_without_retaining_workspace_reference()
    {
        var w = new Workspace { Name = "name" }; var s = new WorkspaceDetailsDraft(); s.Load(w);
        Assert.Equal("name", s.Name); Assert.Equal(Workspace.DefaultCategory, s.Category); Assert.Equal("", s.Description);
        Assert.False(s.IsDirty(true, w)); w.Name = "other"; Assert.Equal("name", s.Name); Assert.True(s.IsDirty(true, w));
    }
    [Theory] [InlineData("name")] [InlineData("category")] [InlineData("description")]
    public void Exact_change_is_dirty_only_in_details_with_a_selection_and_without_suppression(string field)
    {
        var w = new Workspace { Name = "name", Category = "category", Description = "description" };
        var s = new WorkspaceDetailsDraft(); s.Load(w);
        switch (field) { case "name": s.Name += " "; break; case "category": s.Category += " "; break; default: s.Description += " "; break; }
        Assert.True(s.IsDirty(true, w)); Assert.False(s.IsDirty(false, w)); Assert.False(s.IsDirty(true, null));
        s.SuppressDirty = true; Assert.False(s.IsDirty(true, w)); s.SuppressDirty = false; Assert.True(s.IsDirty(true, w));
        s.Load(w); Assert.False(s.IsDirty(true, w));
    }
    [Fact] public void Switching_comparison_workspace_does_not_silently_reload_or_retarget_draft()
    {
        var a = new Workspace { Name = "a" }; var b = new Workspace { Name = "b" };
        var s = new WorkspaceDetailsDraft(); s.Load(a);
        Assert.False(s.IsDirty(true, a)); Assert.True(s.IsDirty(true, b)); Assert.Equal("a", s.Name);
    }
    [Fact] public void Load_does_not_change_suppression_and_separate_drafts_remain_independent()
    {
        var a = new WorkspaceDetailsDraft { SuppressDirty = true }; var b = new WorkspaceDetailsDraft();
        a.Load(new Workspace { Name = "a" }); b.Load(new Workspace { Name = "b" });
        Assert.True(a.SuppressDirty); Assert.False(b.SuppressDirty); Assert.Equal("b", b.Name); Assert.Equal("a", a.Name);
    }
}
