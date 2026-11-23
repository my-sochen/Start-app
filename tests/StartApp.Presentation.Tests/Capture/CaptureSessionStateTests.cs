using StartApp.Presentation;
using Stage = StartApp.Presentation.MainViewModel.CaptureStage;

public class CaptureSessionStateTests
{
    [Fact] public void Defaults_are_closed_empty_and_not_scanning()
    {
        var s = new CaptureSessionState(); Assert.Equal(Stage.None, s.Stage); Assert.Equal("", s.WorkspaceId); Assert.Equal("", s.Search);
        Assert.False(s.Scanning); Assert.False(s.IsOpen); Assert.False(s.IsNew); Assert.False(s.IsPick); Assert.False(s.IsSide);
    }
    [Theory] [InlineData(Stage.None, false, false, false, false)] [InlineData(Stage.NewForm, true, true, false, false)]
    [InlineData(Stage.PickWorkspace, true, false, true, false)] [InlineData(Stage.SideBySide, true, false, false, true)]
    public void Stage_flags_match_exact_public_stage(Stage stage, bool open, bool isNew, bool pick, bool side)
    {
        var s = new CaptureSessionState { Stage = stage }; Assert.Equal(open, s.IsOpen); Assert.Equal(isNew, s.IsNew); Assert.Equal(pick, s.IsPick); Assert.Equal(side, s.IsSide);
    }
    [Theory] [InlineData(Stage.None)] [InlineData(Stage.NewForm)] [InlineData(Stage.PickWorkspace)] [InlineData(Stage.SideBySide)]
    public void Dirty_state_depends_on_stage_and_corresponding_draft_only(Stage stage)
    {
        var s = new CaptureSessionState { Stage = stage };
        Assert.False(s.IsDirty(0, false)); Assert.Equal(stage == Stage.SideBySide, s.IsDirty(1, false));
        Assert.Equal(stage == Stage.NewForm, s.IsDirty(0, true)); Assert.Equal(stage is Stage.NewForm or Stage.SideBySide, s.IsDirty(2, true));
    }
    [Fact] public void Identity_search_and_scan_do_not_change_stage_or_share_between_sessions()
    {
        var a = new CaptureSessionState { Stage = Stage.SideBySide, WorkspaceId = " w ", Search = " search ", Scanning = true }; var b = new CaptureSessionState();
        Assert.Equal(" w ", a.WorkspaceId); Assert.Equal(" search ", a.Search); Assert.Equal(Stage.SideBySide, a.Stage);
        Assert.Equal("", b.WorkspaceId); Assert.Equal("", b.Search); Assert.False(b.Scanning); Assert.False(b.IsOpen);
    }
}
