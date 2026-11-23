namespace StartApp.Presentation;

/// <summary>Owns operation generations and cancellation sources for checking and launching.
/// Cancellation is cooperative: it does not interrupt OS calls or undo sent requests.
/// Sources stay alive for late workers, matching the original host lifetime.</summary>
public sealed class OpenOperationLifetime
{
    private int _generation;
    private CancellationTokenSource? _check;
    private CancellationTokenSource? _launch;

    public (int Generation, CancellationToken Token) BeginCheck()
    {
        _check?.Cancel();
        _check = new CancellationTokenSource();
        return (++_generation, _check.Token);
    }

    public void CancelCheckAndInvalidate()
    {
        _check?.Cancel();
        _generation++;
    }

    public int NextGeneration() => ++_generation;
    public bool IsCurrent(int generation) => generation == _generation;

    public CancellationTokenSource BeginLaunch()
    {
        _launch = new CancellationTokenSource();
        return _launch;
    }

    public void CancelLaunch() => _launch?.Cancel();
}
