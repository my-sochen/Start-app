namespace StartApp.Presentation;

/// <summary>Owns the pending default-workspace confirmation. Null target means removal, not idle;
/// IsOpen distinguishes those states. Persistence, current names and notifications belong to the host.</summary>
public sealed class DefaultWorkspaceSession
{
    public string? PendingId { get; set; }
    public bool IsOpen { get; set; }
    public bool IsRemoval => PendingId is null;
    public string Title => IsRemoval ? "להסיר את ברירת המחדל?" : "כבר מוגדרת סביבת ברירת מחדל";
    public string AcceptLabel => IsRemoval ? "כן, הסר ברירת מחדל" : "כן, הגדר כברירת מחדל";
    public void Close() { PendingId = null; IsOpen = false; }
}
