using StartApp.Core;
using StartApp.Presentation;

public class ItemEditSessionTests
{
    private static ItemEditSession Session()
    {
        var s = new ItemEditSession(); s.CaptureOriginal("workspace", "instance", "name", "description", "target", ItemType.Link);
        s.OriginalOpenWith = "browser"; s.Name = "name"; s.Description = "description"; s.Target = "target"; s.OpenWith = "browser"; s.IsOpen = true;
        return s;
    }
    [Fact] public void Captures_identity_type_and_originals_without_publishing_or_opening_draft()
    {
        var s = new ItemEditSession { Name = "draft", Description = "draft description", Target = "draft target", OpenWith = "draft browser" };
        s.CaptureOriginal("w", "i", "name", null, "target", ItemType.Folder);
        Assert.Equal("w", s.WorkspaceId); Assert.Equal("i", s.InstanceId); Assert.Equal(ItemType.Folder, s.Type);
        Assert.Equal("name", s.OriginalName); Assert.Equal("", s.OriginalDescription); Assert.Equal("target", s.OriginalTarget);
        Assert.Equal("draft", s.Name); Assert.Equal("draft description", s.Description); Assert.Equal("draft target", s.Target); Assert.Equal("draft browser", s.OpenWith); Assert.False(s.IsOpen);
    }
    [Theory] [InlineData("name")] [InlineData("description")] [InlineData("target")] [InlineData("openWith")]
    public void Every_draft_field_is_dirty_by_exact_value_only_while_open(string field)
    {
        var s = Session(); Assert.False(s.IsDirty);
        switch (field) { case "name": s.Name += " "; break; case "description": s.Description += " "; break; case "target": s.Target += " "; break; default: s.OpenWith += " "; break; }
        Assert.True(s.IsDirty); s.IsOpen = false; Assert.False(s.IsDirty); s.IsOpen = true; Assert.True(s.IsDirty);
        s.Name = s.OriginalName; s.Description = s.OriginalDescription; s.Target = s.OriginalTarget; s.OpenWith = s.OriginalOpenWith;
        Assert.False(s.IsDirty);
    }
    [Fact] public void Draft_changes_and_close_never_retarget_the_captured_item()
    {
        var s = Session(); s.Name = "other"; s.Target = "new target"; s.IsOpen = false;
        Assert.Equal("workspace", s.WorkspaceId); Assert.Equal("instance", s.InstanceId); Assert.Equal("target", s.OriginalTarget);
        Assert.Equal("new target", s.Target);
    }
    [Fact] public void Independent_sessions_do_not_share_draft_identity_or_open_state()
    {
        var a = Session(); var b = Session(); a.CaptureOriginal("other", "second", "n", null, "t", ItemType.Program); a.Target = "changed"; a.IsOpen = false;
        Assert.Equal("workspace", b.WorkspaceId); Assert.Equal("instance", b.InstanceId); Assert.Equal(ItemType.Link, b.Type);
        Assert.Equal("target", b.Target); Assert.True(b.IsOpen); Assert.False(b.IsDirty);
    }
}
