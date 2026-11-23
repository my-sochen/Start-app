using System.Runtime.ExceptionServices;
using StartApp.Core;

namespace StartApp.Presentation;

/// <summary>Owns bounded blocking preflight workers and their deadline publication.
/// Worker capacity lasts until actual return, even after timeout/cancellation. No UI, store or launch access.</summary>
public sealed class OpeningCheckRunner
{
    private readonly int _capacity;
    private readonly OpenCheckWorkerBudget _workers;
    public OpeningCheckRunner(int capacity)
    {
        _capacity = capacity;
        _workers = new(capacity);
    }

    public async Task<((Item Item, LaunchConfig? Launch, PreflightFinding Finding, OpenState Dup)?[] Results, Action Stop)> RunAsync(
        List<(Membership M, Item? Item)> snapshot, IItemPreflight? preflight, IOpenStateDetector detector,
        TimeSpan timeout, CancellationToken token, Action<Exception> reportLate)
    {
        var effectiveDetector = detector is IOpenSnapshotSource ? new NoOpenStateDetection() : detector;
        var stop = CancellationTokenSource.CreateLinkedTokenSource(token); // deliberately not disposed: a late worker may still call stop.Cancel(), and the parent token source is per operation, so nothing outlives the operation // the deadline stops workers from taking NEW items; a call already running finishes by itself
        var publication = new CheckResultPublication<(Item Item, LaunchConfig? Launch, PreflightFinding Finding, OpenState Dup)>(snapshot.Count);
        var next = -1;
        // Publication is the fault-ownership handoff. Before it Guard owns delivery;
        // afterwards report promptly even if another worker or launch is still blocked.
        void ObserveFault(Exception error)
        {
            if (publication.ObserveFault(error) is { } late) reportLate(late);
        }
        Task Worker() => Task.Factory.StartNew(() => // dedicated threads: these calls block, and must not starve the thread pool (or the deadline timer)
        {
            try { while (!stop.Token.IsCancellationRequested)
            {
                var i = Interlocked.Increment(ref next); if (i >= snapshot.Count) return;
                try { var r = Evaluate(snapshot[i].M, snapshot[i].Item, preflight, effectiveDetector); publication.Record(i, r); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidOperationException)
                { var r = (snapshot[i].Item ?? new Item(snapshot[i].M.ItemId, ItemType.File, snapshot[i].M.ItemId, ""), snapshot[i].M.Launch, new PreflightFinding(PreflightState.Unavailable, e.GetType().Name), OpenState.Unknown); publication.Record(i, r); }
                catch (Exception e) { ObserveFault(e); stop.Cancel(); return; } // a programming bug is not "unavailable": stop and surface it
            } }
            finally { _workers.Release(); }
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

    private static (Item Item, LaunchConfig? Launch, PreflightFinding Finding, OpenState Dup) Evaluate(Membership m, Item? it, IItemPreflight? preflight, IOpenStateDetector detector)
    {
        if (it is null) return (new Item(m.ItemId, ItemType.File, "(missing item " + m.ItemId + ")", ""), m.Launch, new(PreflightState.Unsupported, "the saved item record is missing from the catalog"), OpenState.NotOpen);
        var f = PreflightRules.CheckForm(it, m.Launch) ?? preflight?.Check(it, m.Launch) ?? new(PreflightState.Ready);
        var dup = f.State != PreflightState.Ready ? OpenState.Unknown : detector.Detect(it, m.Launch);
        return (it, m.Launch, f, dup);
    }

}
