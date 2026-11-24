using StartApp.Presentation;

public class UnsavedExitSessionTests
{
    [Fact] public void Default_session_is_closed_with_no_hosts_or_effect_on_continue()
    {
        var s = new UnsavedExitSession(); s.Continue(); s.Continue();
        Assert.False(s.IsOpen); Assert.False(s.ForCreate); Assert.False(s.ForItemEdit);
        Assert.False(s.ForAddress); Assert.False(s.ForDesign); Assert.False(s.ForCapture);
    }
    [Fact] public void Continuation_runs_once_and_does_not_implicitly_close_or_clear_host_flags()
    {
        var calls = 0; var s = new UnsavedExitSession { IsOpen = true, ForCreate = true, ForAddress = true, Continuation = () => calls++ };
        s.Continue(); s.Continue(); Assert.Equal(1, calls);
        Assert.True(s.IsOpen); Assert.True(s.ForCreate); Assert.True(s.ForAddress);
    }
    [Fact] public void Clearing_continuation_cancels_only_the_callback()
    {
        var calls = 0; var s = new UnsavedExitSession { ForCapture = true, Continuation = () => calls++ };
        s.ClearContinuation(); s.Continue(); Assert.Equal(0, calls); Assert.True(s.ForCapture);
    }
    [Fact] public void Continuation_is_consumed_before_invocation_even_if_callback_reenters_or_throws()
    {
        var s = new UnsavedExitSession(); var calls = 0;
        s.Continuation = () => { calls++; s.Continue(); throw new InvalidOperationException("callback failed"); };
        Assert.Throws<InvalidOperationException>(() => s.Continue()); s.Continue(); Assert.Equal(1, calls);
    }
    [Fact] public void Callback_can_install_a_new_continuation_without_it_being_erased_afterward()
    {
        var s = new UnsavedExitSession(); var calls = 0;
        s.Continuation = () => s.Continuation = () => calls++;
        s.Continue(); Assert.Equal(0, calls); s.Continue(); s.Continue(); Assert.Equal(1, calls);
    }
    [Fact] public void Sessions_do_not_share_continuations_or_host_flags()
    {
        var calls = 0; var a = new UnsavedExitSession { IsOpen = true, ForDesign = true, Continuation = () => calls++ };
        var b = new UnsavedExitSession(); b.Continue(); Assert.Equal(0, calls); Assert.False(b.IsOpen); Assert.False(b.ForDesign);
        a.Continue(); Assert.Equal(1, calls);
    }
}
