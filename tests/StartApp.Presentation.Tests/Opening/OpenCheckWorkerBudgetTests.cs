using StartApp.Presentation;

public class OpenCheckWorkerBudgetTests
{
    [Fact] public void Capacity_is_held_until_explicit_release_even_after_an_acquisition_is_refused()
    {
        var b = new OpenCheckWorkerBudget(2);
        Assert.True(b.TryAcquire()); Assert.True(b.TryAcquire()); Assert.False(b.TryAcquire()); Assert.False(b.TryAcquire());
        b.Release(); Assert.True(b.TryAcquire()); Assert.False(b.TryAcquire()); b.Release(); b.Release();
        Assert.True(b.TryAcquire()); Assert.True(b.TryAcquire()); Assert.False(b.TryAcquire()); b.Release(); b.Release();
    }
    [Fact] public async Task Concurrent_workers_never_enter_beyond_capacity_and_leave_capacity_reusable()
    {
        var budget = new OpenCheckWorkerBudget(4); var active = 0; var maximum = 0; var entered = 0;
        using var barrier = new Barrier(16);
        var workers = Enumerable.Range(0, 16).Select(_ => Task.Factory.StartNew(() =>
        {
            barrier.SignalAndWait();
            for (var i = 0; i < 1000; i++)
            {
                if (!budget.TryAcquire()) { Thread.Yield(); continue; }
                try
                {
                    var count = Interlocked.Increment(ref active); Interlocked.Increment(ref entered);
                    int old; do { old = Volatile.Read(ref maximum); if (old >= count) break; } while (Interlocked.CompareExchange(ref maximum, count, old) != old);
                    Thread.Yield(); Interlocked.Decrement(ref active);
                }
                finally { budget.Release(); }
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default)).ToArray();
        await Task.WhenAll(workers);
        Assert.InRange(maximum, 1, 4); Assert.True(entered > 0); Assert.Equal(0, active);
        for (var i = 0; i < 4; i++) Assert.True(budget.TryAcquire());
        Assert.False(budget.TryAcquire()); for (var i = 0; i < 4; i++) budget.Release();
    }
    [Fact] public void Separate_sessions_have_separate_worker_budgets()
    {
        var a = new OpenCheckWorkerBudget(1); var b = new OpenCheckWorkerBudget(1);
        Assert.True(a.TryAcquire()); Assert.True(b.TryAcquire()); Assert.False(a.TryAcquire()); Assert.False(b.TryAcquire());
        a.Release(); Assert.False(b.TryAcquire()); Assert.True(a.TryAcquire()); a.Release(); b.Release();
    }
}
