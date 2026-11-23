using StartApp.Presentation;

public class CaptureScanLifetimeTests
{
    [Fact] public void Begin_cancels_prior_source_and_invalidates_generation()
    {
        var s = new CaptureScanLifetime(); var first = s.Begin(); var token = first.Source.Token;
        var second = s.Begin(); Assert.True(token.IsCancellationRequested); Assert.False(s.IsCurrent(first.Generation));
        Assert.True(s.IsCurrent(second.Generation)); Assert.False(second.Source.IsCancellationRequested);
    }
    [Fact] public void Cancel_invalidates_but_does_not_release_an_outstanding_worker()
    {
        var s = new CaptureScanLifetime(); var scan = s.Begin(); s.WorkerStarted();
        s.CancelAndInvalidate(); s.CancelAndInvalidate();
        Assert.True(scan.Source.IsCancellationRequested); Assert.False(s.IsCurrent(scan.Generation)); Assert.True(s.HasOutstandingScan);
        s.WorkerReturned(); Assert.False(s.HasOutstandingScan);
    }
    [Fact] public void Superseding_a_timed_out_scan_preserves_outstanding_worker_until_return()
    {
        var s = new CaptureScanLifetime(); s.Begin(); s.WorkerStarted(); var later = s.Begin();
        Assert.True(s.HasOutstandingScan); Assert.True(s.IsCurrent(later.Generation));
        s.WorkerReturned(); Assert.False(s.HasOutstandingScan);
        s.WorkerStarted(); Assert.True(s.HasOutstandingScan); s.WorkerReturned(); Assert.False(s.HasOutstandingScan);
    }
    [Fact] public void Cancel_without_source_and_disposed_source_are_safe()
    {
        var s = new CaptureScanLifetime(); s.CancelAndInvalidate();
        var first = s.Begin(); first.Source.Dispose(); s.CancelAndInvalidate();
        var second = s.Begin(); Assert.True(s.IsCurrent(second.Generation)); Assert.False(second.Source.IsCancellationRequested);
    }
    [Fact] public async Task Worker_return_from_another_thread_releases_outstanding_accounting()
    {
        var s = new CaptureScanLifetime(); s.WorkerStarted();
        await Task.Run(s.WorkerReturned); Assert.False(s.HasOutstandingScan);
    }
    [Fact] public void Sessions_do_not_share_cancellation_generation_or_worker_count()
    {
        var a = new CaptureScanLifetime(); var b = new CaptureScanLifetime();
        var ca = a.Begin(); var cb = b.Begin(); a.WorkerStarted(); a.CancelAndInvalidate();
        Assert.True(ca.Source.IsCancellationRequested); Assert.False(cb.Source.IsCancellationRequested);
        Assert.True(b.IsCurrent(cb.Generation)); Assert.False(b.HasOutstandingScan); a.WorkerReturned();
    }
}
