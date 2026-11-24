namespace StartApp.Presentation;

/// <summary>Owns the current toast content/flags and generation used to reject stale auto-hide callbacks.
/// No timer, dispatcher, commands or view-model dependency. Host preserves publication order and delay policy.</summary>
public sealed class ToastSession
{
    public string Message { get; set; } = "";
    public string TechnicalDetails { get; set; } = "";
    public bool Visible { get; set; }
    public bool HasOpeningResults { get; set; }
    public bool Persistent { get; set; }
    private int _generation;
    public int NextGeneration() => ++_generation;
    public bool IsCurrent(int generation) => generation == _generation;
}
