namespace StartApp.Presentation;

/// <summary>Owns the session-wide ceiling on blocking preflight workers, including workers from stale runs.
/// A timed-out worker keeps its slot until its finally block releases it. No task, launcher or view access.</summary>
public sealed class OpenCheckWorkerBudget(int limit)
{
    private int _active;

    public bool TryAcquire()
    {
        if (Interlocked.Increment(ref _active) <= limit) return true;
        Interlocked.Decrement(ref _active);
        return false;
    }

    public void Release() => Interlocked.Decrement(ref _active);
}
