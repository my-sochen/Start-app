using System.Runtime.ExceptionServices;
using StartApp.Core;

namespace StartApp.Presentation;

/// <summary>Owns bounded blocking preflight workers and their deadline publication.
/// Worker capacity lasts until actual return, even after timeout/cancellation. No UI, store or launch access.</summary>
internal readonly record struct OpeningCheckResult(Item Item, LaunchConfig? Launch, PreflightFinding Finding, OpenState Dup);

public sealed class OpeningCheckRunner
{
    private readonly int _capacity;
    private readonly OpenCheckWorkerBudget _workers;
    public OpeningCheckRunner(int capacity)
    {
        _capacity = capacity;
        _workers = new(capacity);
    }

    internal async Task<(OpeningCheckResult?[] Results, Action Stop)> RunAsync(
        List<(Membership M, Item? Item)> snapshot, IItemPreflight? preflight, IOpenStateDetector detector,
        TimeSpan timeout, CancellationToken token, Action<Exception> reportLate)
    {
        var effectiveDetector = detector is IOpenSnapshotSource ? new NoOpenStateDetection() : detector;
        var stop = CancellationTokenSource.CreateLinkedTokenSource(token); // deliberately not disposed: a late worker may still call stop.Cancel(), and the parent token source is per operation, so nothing outlives the operation // the deadline stops workers from taking NEW items; a call already running finishes by itself
        var publication = new CheckResultPublication<OpeningCheckResult>(snapshot.Count);
        var next = -1;
        // Publication is the fault-ownership handoff. Before it Guard owns delivery;
        // afterwards report promptly even if another worker or launch is still blocked.
        void ObserveFault(Exception error)
        {
            if (publication.ObserveFault(error) is { } late) reportLate(late);
        }
        Task Worker() => Task.Factory.StartNew(() => // dedicated threads: these calls block, and must not starve the thread pool (or the deadline timer)
        {
            try
            {
                while (!stop.Token.IsCancellationRequested)
                {
                    var index = Interlocked.Increment(ref next);
                    if (index >= snapshot.Count)
                    {
                        return;
                    }

                    var row = snapshot[index];
                    try
                    {
                        var result = Evaluate(row.M, row.Item, preflight, effectiveDetector);
                        publication.Record(index, result);
                    }
                    catch (Exception error) when (error is IOException or UnauthorizedAccessException or InvalidOperationException)
                    {
                        var item = row.Item ?? new Item(row.M.ItemId, ItemType.File, row.M.ItemId, "");
                        var finding = new PreflightFinding(PreflightState.Unavailable, error.GetType().Name);
                        var result = new OpeningCheckResult(item, row.M.Launch, finding, OpenState.Unknown);
                        publication.Record(index, result);
                    }
                    catch (Exception error)
                    {
                        // A programming bug is not "unavailable": stop and surface it.
                        ObserveFault(error);
                        stop.Cancel();
                        return;
                    }
                }
            }
            finally
            {
                _workers.Release();
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        var workers = new List<Task>();
        for (int i = 0; i < Math.Min(_capacity, snapshot.Count); i++)
        {
            if (!_workers.TryAcquire()) break;
            workers.Add(Worker());
        }
        var all = Task.WhenAll(workers);
        await Task.WhenAny(all, Task.Delay(timeout, token)).ConfigureAwait(true);
        // Observe infrastructure faults too, without competing for pre-publication
        // error delivery. This fallback waits for all workers: only errors caught
        // by a worker's Evaluate catch are guaranteed prompt late reporting.
        _ = all.ContinueWith(t => ObserveFault(t.Exception!.GetBaseException()), CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        var (done, f0) = publication.Publish();
        if (f0 is not null) ExceptionDispatchInfo.Capture(f0).Throw();
        return (done, stop.Cancel);
    }

    private static OpeningCheckResult Evaluate(Membership membership, Item? item, IItemPreflight? preflight, IOpenStateDetector detector)
    {
        if (item is null)
        {
            var missing = new Item(membership.ItemId, ItemType.File, "(missing item " + membership.ItemId + ")", "");
            var unsupported = new PreflightFinding(PreflightState.Unsupported, "the saved item record is missing from the catalog");
            return new OpeningCheckResult(missing, membership.Launch, unsupported, OpenState.NotOpen);
        }

        var finding = PreflightRules.CheckForm(item, membership.Launch)
            ?? preflight?.Check(item, membership.Launch)
            ?? new PreflightFinding(PreflightState.Ready);
        var duplicate = OpenState.Unknown;
        if (finding.State == PreflightState.Ready)
        {
            duplicate = detector.Detect(item, membership.Launch);
        }

        return new OpeningCheckResult(item, membership.Launch, finding, duplicate);
    }
}
