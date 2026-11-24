using StartApp.Core;
using StartApp.Presentation;

public class OpeningSessionStateTests
{
    [Fact] public void New_session_has_no_target_plan_or_open_phase()
    {
        var s = new OpeningSessionState(); Assert.Equal("", s.WorkspaceId); Assert.Null(s.PendingPlan);
        Assert.False(s.SingleItem); Assert.False(s.SelectionPending); Assert.False(s.ResultsOpen);
        Assert.False(s.Checking); Assert.False(s.Launching); Assert.False(s.DecisionPending); Assert.False(s.ShowOpenPanel); Assert.False(s.CanBackToSelection);
    }
    [Fact] public void Nonnull_empty_plan_is_still_a_pending_decision_and_single_item_blocks_back()
    {
        var s = new OpeningSessionState { PendingPlan = new() };
        Assert.True(s.DecisionPending); Assert.True(s.CanBackToSelection); Assert.False(s.ShowOpenPanel);
        s.SingleItem = true; Assert.False(s.CanBackToSelection); Assert.True(s.DecisionPending);
        s.PendingPlan = null; Assert.False(s.DecisionPending);
    }
    [Theory] [InlineData(false, false, false)] [InlineData(true, false, true)]
    [InlineData(false, true, true)] [InlineData(true, true, true)]
    public void Busy_panel_depends_only_on_check_or_launch_not_selection_result_or_decision(bool checking, bool launching, bool expected)
    {
        var s = new OpeningSessionState { Checking = checking, Launching = launching, SelectionPending = true, ResultsOpen = true, PendingPlan = new() };
        Assert.Equal(expected, s.ShowOpenPanel);
    }
    [Fact] public void Clearing_plan_does_not_retarget_workspace_or_reset_unrelated_flags()
    {
        var s = new OpeningSessionState { WorkspaceId = "captured", SingleItem = true, SelectionPending = true, ResultsOpen = true, Checking = true, PendingPlan = new() };
        s.PendingPlan = null; Assert.Equal("captured", s.WorkspaceId); Assert.True(s.SingleItem);
        Assert.True(s.SelectionPending); Assert.True(s.ResultsOpen); Assert.True(s.Checking);
    }
    [Fact] public void Independent_sessions_do_not_share_phase_or_plan()
    {
        var a = new OpeningSessionState { WorkspaceId = "a", PendingPlan = new(), Checking = true };
        var b = new OpeningSessionState(); a.PendingPlan.Clear();
        Assert.Null(b.PendingPlan); Assert.False(b.Checking); Assert.Equal("", b.WorkspaceId);
    }
}
