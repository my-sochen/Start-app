using System.Collections.ObjectModel;

namespace StartApp.Presentation;

/// <summary>Owns capture duplicate-warning state for a single drag and a grouped add.
/// Rows/choices are opaque. Host retains duplicate identity rules, staging and notification order.</summary>
public sealed class CaptureDuplicateSession<TRow, TChoice> where TRow : class
{
    public TRow? SingleTarget { get; set; }
    public string SingleMessage { get; set; } = "";
    public bool GroupPending { get; set; }
    public ObservableCollection<TChoice> Choices { get; } = new();
    public bool SinglePending => SingleTarget != null;
    public bool IsIdle => !GroupPending && SingleTarget == null;
}
