using StartApp.Presentation;

public class OpenWithSessionTests
{
    [Fact] public void Default_session_is_closed_without_targets_program_or_default_choice()
    {
        var s = new OpenWithSession<object, object>();
        Assert.False(s.IsOpen); Assert.Null(s.SavedTarget); Assert.Null(s.StagedTarget);
        Assert.Equal("", s.Program); Assert.False(s.SetDefault);
    }
    [Fact] public void Clearing_targets_is_repeatable_and_does_not_change_draft_or_open_state()
    {
        var s = new OpenWithSession<object, object> { IsOpen = true, SavedTarget = new object(), StagedTarget = new object(), Program = "  browser  ", SetDefault = true };
        s.ClearTargets(); s.ClearTargets();
        Assert.Null(s.SavedTarget); Assert.Null(s.StagedTarget); Assert.True(s.IsOpen);
        Assert.Equal("  browser  ", s.Program); Assert.True(s.SetDefault);
    }
    [Fact] public void Closing_before_target_cleanup_preserves_targets_for_host_notification()
    {
        var saved = new object(); var staged = new object();
        var s = new OpenWithSession<object, object> { IsOpen = true, SavedTarget = saved, StagedTarget = staged };
        s.IsOpen = false; Assert.Same(saved, s.SavedTarget); Assert.Same(staged, s.StagedTarget);
        s.ClearTargets(); Assert.Null(s.SavedTarget); Assert.Null(s.StagedTarget);
    }
    [Fact] public void Sessions_do_not_share_targets_or_program_default_drafts()
    {
        var a = new OpenWithSession<object, object> { IsOpen = true, SavedTarget = new object(), Program = "browser", SetDefault = true };
        var staged = new object(); var b = new OpenWithSession<object, object> { StagedTarget = staged };
        a.ClearTargets(); a.Program = "other";
        Assert.Same(staged, b.StagedTarget); Assert.Null(b.SavedTarget); Assert.False(b.IsOpen); Assert.False(b.SetDefault); Assert.Equal("", b.Program);
    }
}
