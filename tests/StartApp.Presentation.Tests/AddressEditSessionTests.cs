using StartApp.Presentation;

public class AddressEditSessionTests
{
    [Fact] public void New_session_has_no_target_and_is_closed_clean_and_empty()
    {
        var s = new AddressEditSession<object>();
        Assert.False(s.IsOpen); Assert.False(s.IsDirty); Assert.Null(s.StagedTarget);
        Assert.Equal("", s.WorkspaceId); Assert.Equal("", s.InstanceId);
        Assert.Equal("", s.Address); Assert.Equal("", s.OriginalAddress);
    }
    [Fact] public void Dirty_is_open_scoped_and_compares_exact_untrimmed_address()
    {
        var s = new AddressEditSession<object> { OriginalAddress = "address", Address = "address", IsOpen = true };
        Assert.False(s.IsDirty); s.Address = "address "; Assert.True(s.IsDirty);
        s.IsOpen = false; Assert.False(s.IsDirty); Assert.Equal("address ", s.Address);
        s.IsOpen = true; Assert.True(s.IsDirty); s.Address = "address"; Assert.False(s.IsDirty);
    }
    [Fact] public void Clearing_target_releases_saved_and_staged_identity_but_preserves_draft_and_original()
    {
        var s = new AddressEditSession<object> { WorkspaceId = "w", InstanceId = "i", StagedTarget = new object(), Address = "draft", OriginalAddress = "original", IsOpen = false };
        s.ClearTarget(); s.ClearTarget();
        Assert.Equal("", s.WorkspaceId); Assert.Equal("", s.InstanceId); Assert.Null(s.StagedTarget);
        Assert.Equal("draft", s.Address); Assert.Equal("original", s.OriginalAddress); Assert.False(s.IsOpen);
    }
    [Fact] public void Closing_and_clearing_are_separate_to_preserve_host_notification_order()
    {
        var staged = new object();
        var s = new AddressEditSession<object> { WorkspaceId = "w", InstanceId = "i", StagedTarget = staged, IsOpen = true };
        s.IsOpen = false;
        Assert.Equal("w", s.WorkspaceId); Assert.Equal("i", s.InstanceId); Assert.Same(staged, s.StagedTarget);
        s.ClearTarget(); Assert.Null(s.StagedTarget);
    }
    [Fact] public void Separate_sessions_cannot_change_each_others_target_or_draft()
    {
        var target = new object();
        var a = new AddressEditSession<object> { StagedTarget = target, Address = "a", IsOpen = true };
        var b = new AddressEditSession<object> { WorkspaceId = "b", InstanceId = "ib", Address = "b" };
        a.ClearTarget(); a.Address = "changed";
        Assert.Equal("b", b.WorkspaceId); Assert.Equal("ib", b.InstanceId); Assert.Equal("b", b.Address); Assert.False(b.IsOpen);
    }
}
