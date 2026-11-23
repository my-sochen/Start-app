using StartApp.Presentation;

public class OpenOperationLifetimeTests
{
    [Fact] public void New_check_cancels_prior_token_and_invalidates_prior_generation()
    {
        var s = new OpenOperationLifetime(); var first = s.BeginCheck(); var second = s.BeginCheck();
        Assert.True(first.Token.IsCancellationRequested); Assert.False(second.Token.IsCancellationRequested);
        Assert.False(s.IsCurrent(first.Generation)); Assert.True(s.IsCurrent(second.Generation));
    }
    [Fact] public void Cancelling_check_invalidates_even_when_no_source_exists_and_remains_repeatable()
    {
        var s = new OpenOperationLifetime(); s.CancelCheckAndInvalidate();
        var first = s.BeginCheck(); s.CancelCheckAndInvalidate(); s.CancelCheckAndInvalidate();
        Assert.True(first.Token.IsCancellationRequested); Assert.False(s.IsCurrent(first.Generation));
        var second = s.BeginCheck(); Assert.True(s.IsCurrent(second.Generation)); Assert.False(second.Token.IsCancellationRequested);
    }
    [Fact] public void New_generation_for_confirmation_does_not_cancel_check_token()
    {
        var s = new OpenOperationLifetime(); var check = s.BeginCheck(); var confirm = s.NextGeneration();
        Assert.False(check.Token.IsCancellationRequested); Assert.False(s.IsCurrent(check.Generation)); Assert.True(s.IsCurrent(confirm));
    }
    [Fact] public void Cancelling_launch_does_not_cancel_check_or_change_generation()
    {
        var s = new OpenOperationLifetime(); s.CancelLaunch();
        var check = s.BeginCheck(); var launch = s.BeginLaunch(); s.CancelLaunch(); s.CancelLaunch();
        Assert.True(launch.IsCancellationRequested); Assert.False(check.Token.IsCancellationRequested); Assert.True(s.IsCurrent(check.Generation));
    }
    [Fact] public void Starting_launch_does_not_cancel_previous_source_or_advance_generation()
    {
        var s = new OpenOperationLifetime(); var check = s.BeginCheck(); var first = s.BeginLaunch(); var second = s.BeginLaunch();
        Assert.False(first.IsCancellationRequested); Assert.False(second.IsCancellationRequested); Assert.True(s.IsCurrent(check.Generation));
        s.CancelLaunch(); Assert.False(first.IsCancellationRequested); Assert.True(second.IsCancellationRequested);
    }
    [Fact] public void Separate_sessions_do_not_cancel_or_invalidate_each_other()
    {
        var a = new OpenOperationLifetime(); var b = new OpenOperationLifetime();
        var ca = a.BeginCheck(); var cb = b.BeginCheck(); var lb = b.BeginLaunch(); a.CancelCheckAndInvalidate(); a.CancelLaunch();
        Assert.True(ca.Token.IsCancellationRequested); Assert.False(cb.Token.IsCancellationRequested);
        Assert.False(lb.IsCancellationRequested); Assert.True(b.IsCurrent(cb.Generation));
    }
}
