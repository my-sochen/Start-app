namespace StartApp.Presentation;

/// <summary>Owns the unsaved-exit dialog state, host flags and deferred continuation.
/// Saving/discard policy stays with the host. Continue consumes the callback before invoking it.</summary>
public sealed class UnsavedExitSession
{
    public bool IsOpen { get; set; }
    public bool ForCreate { get; set; }
    public bool ForItemEdit { get; set; }
    public bool ForAddress { get; set; }
    public bool ForDesign { get; set; }
    public bool ForCapture { get; set; }
    public Action Continuation { get; set; } = () => { };
    public void ClearContinuation() => Continuation = () => { };
    public void Continue()
    {
        var action = Continuation;
        ClearContinuation();
        action();
    }
}
