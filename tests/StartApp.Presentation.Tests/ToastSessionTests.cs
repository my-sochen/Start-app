using StartApp.Presentation;

public class ToastSessionTests
{
    [Fact] public void New_session_is_empty_hidden_nonpersistent_and_not_an_opening_result()
    {
        var s = new ToastSession(); Assert.Equal("", s.Message); Assert.Equal("", s.TechnicalDetails);
        Assert.False(s.Visible); Assert.False(s.Persistent); Assert.False(s.HasOpeningResults);
    }
    [Fact] public void New_generation_rejects_old_auto_hide_without_changing_content_or_visibility()
    {
        var s = new ToastSession { Message = "message", TechnicalDetails = "details", Visible = true, Persistent = true, HasOpeningResults = true };
        var old = s.NextGeneration(); var current = s.NextGeneration();
        Assert.False(s.IsCurrent(old)); Assert.True(s.IsCurrent(current)); Assert.True(s.Visible);
        Assert.Equal("message", s.Message); Assert.Equal("details", s.TechnicalDetails); Assert.True(s.Persistent); Assert.True(s.HasOpeningResults);
    }
    [Fact] public void Hiding_preserves_generation_and_content_until_host_explicitly_invalidates()
    {
        var s = new ToastSession { Message = "message", TechnicalDetails = "details", Visible = true };
        var gen = s.NextGeneration(); s.Visible = false;
        Assert.True(s.IsCurrent(gen)); Assert.Equal("message", s.Message); Assert.Equal("details", s.TechnicalDetails);
        s.NextGeneration(); Assert.False(s.IsCurrent(gen));
    }
    [Fact] public void Sessions_do_not_share_generations_or_toast_drafts()
    {
        var a = new ToastSession(); var b = new ToastSession(); var ga = a.NextGeneration(); var gb = b.NextGeneration();
        a.Message = "a"; a.Visible = true; a.NextGeneration();
        Assert.False(a.IsCurrent(ga)); Assert.True(b.IsCurrent(gb)); Assert.Equal("", b.Message); Assert.False(b.Visible);
    }
}
