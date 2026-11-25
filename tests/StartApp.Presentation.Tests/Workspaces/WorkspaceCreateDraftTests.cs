using StartApp.Core;
using StartApp.Presentation;

public class WorkspaceCreateDraftTests
{
    [Fact] public void Empty_draft_is_clean_and_preview_uses_original_defaults()
    {
        var s = new WorkspaceCreateDraft(); Assert.False(s.IsDirty);
        Assert.Equal(Workspace.DefaultCategory, s.Category);
        var p = s.Preview; Assert.Equal("פרויקט חדש", p.Name); Assert.Null(p.Color);
        Assert.Equal(Workspace.DefaultCategory, p.Category); Assert.Equal("", p.ImagePath);
    }
    [Theory] [InlineData("name")] [InlineData("description")] [InlineData("category")] [InlineData("color")] [InlineData("image")]
    public void Every_field_makes_draft_dirty_by_exact_value_including_whitespace(string field)
    {
        var s = new WorkspaceCreateDraft();
        switch(field) { case "name": s.Name = " "; break; case "description": s.Description = " "; break; case "category": s.Category = " "; break; case "color": s.Color = " "; break; default: s.ImagePath = " "; break; }
        Assert.True(s.IsDirty);
        s.Name = ""; s.Description = ""; s.Category = Workspace.DefaultCategory; s.Color = ""; s.ImagePath = ""; Assert.False(s.IsDirty);
    }
    [Fact] public void Preview_is_detached_and_carries_draft_values_without_trimming()
    {
        var s = new WorkspaceCreateDraft { Name = " name ", Category = "category", Description = "description", Color = " #FFFFFF ", ImagePath = " image " };
        var p = s.Preview; Assert.Equal(" name ", p.Name); Assert.Equal("category", p.Category); Assert.Equal(" #FFFFFF ", p.Color); Assert.Equal(" image ", p.ImagePath);
        p.Name = "changed"; Assert.Equal(" name ", s.Name); Assert.NotSame(p, s.Preview);
    }
    [Fact] public void Whitespace_name_and_color_use_preview_defaults_without_changing_dirty_draft()
    {
        var s = new WorkspaceCreateDraft { Name = " ", Color = " " };
        Assert.Equal("פרויקט חדש", s.Preview.Name); Assert.Null(s.Preview.Color);
        Assert.True(s.IsDirty); Assert.Equal(" ", s.Name); Assert.Equal(" ", s.Color);
    }
    [Fact] public void Drafts_have_independent_values_and_previews()
    {
        var a = new WorkspaceCreateDraft { Name = "a", Color = "#000000" }; var b = new WorkspaceCreateDraft();
        Assert.False(b.IsDirty); Assert.Equal("", b.Name); Assert.Null(b.Preview.Color);
        a.Preview.Color = "other"; Assert.Equal("#000000", a.Color);
    }
}
