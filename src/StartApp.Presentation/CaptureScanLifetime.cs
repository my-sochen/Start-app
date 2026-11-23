namespace StartApp.Presentation;

/// <summary>Owns capture scan generations, cancellation source and outstanding worker accounting.
/// A timed-out scan stays outstanding until its worker returns. Begin/cancel stay on the host thread;
/// only worker accounting crosses threads. No scan adapter, dispatcher or view dependency.</summary>
public sealed class CaptureScanLifetime
{
    private int _generation, _outstanding;
    private CancellationTokenSource? _source;
    public bool HasOutstandingScan => Volatile.Read(ref _outstanding) >= 1;

    public (int Generation, CancellationTokenSource Source) Begin()
    {
        var generation = ++_generation;
        try { _source?.Cancel(); } catch (ObjectDisposedException) { }
        _source?.Dispose();
        var source = new CancellationTokenSource(); _source = source;
        return (generation, source);
    }
    public bool IsCurrent(int generation) => generation == _generation;
    public void WorkerStarted() => Interlocked.Increment(ref _outstanding);
    public void WorkerReturned() => Interlocked.Decrement(ref _outstanding);
    public void CancelAndInvalidate()
    {
        _generation++;
        try { _source?.Cancel(); } catch (ObjectDisposedException) { }
    }
}
