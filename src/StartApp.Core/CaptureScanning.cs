namespace StartApp.Core;

/// <summary>Runs a capture scan off the caller's thread with a hard time bound. A source stuck in a COM/UI Automation call
/// can never freeze the caller: after the timeout the result is an empty scan with an honest note, and the stuck task is
/// abandoned (never aborted, so no Thread.Abort-style corruption). Cancellation stops the wait and discards the result.</summary>
public static class CaptureScanning
{
    public static async Task<CaptureScan> BoundedAsync(IOpenItemCapturer capturer, TimeSpan timeout, CancellationToken cancel = default, Action? scanSettled = null)
    {
        var scan = Task.Run(() => capturer.Scan());
        // Fires when the underlying Scan() call returns in ANY state - including long after a timeout abandoned it. This is what
        // lets the caller bound abandoned threads: the call itself is never aborted (impossible safely), only stopped being waited on.
        if (scanSettled != null) _ = scan.ContinueWith(_ => scanSettled(), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        var limit = Task.Delay(timeout, cancel);
        var done = await Task.WhenAny(scan, limit).ConfigureAwait(false);
        if (done == scan) return await scan; // finished in time; a source failure propagates to the caller's handler
        _ = scan.ContinueWith(t => { _ = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted); // abandoned, never unobserved
        cancel.ThrowIfCancellationRequested();
        return new CaptureScan(Array.Empty<CapturedItem>(), new[] { "The scan took too long and was stopped; items can still be added by hand or by scanning again" });
    }
}
