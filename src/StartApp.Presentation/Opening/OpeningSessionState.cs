using StartApp.Core;

namespace StartApp.Presentation;

/// <summary>Owns the current opening session's target, pending checked plan and UI phase flags.
/// Host retains checks, launches, cancellation, persistence and notification order.</summary>
public sealed class OpeningSessionState
{
    public List<(Item Item, LaunchConfig? Launch, PreflightFinding Finding, OpenState Dup)>? PendingPlan { get; set; }
    public string WorkspaceId { get; set; } = "";
    public bool SingleItem { get; set; }
    public bool SelectionPending { get; set; }
    public bool ResultsOpen { get; set; }
    public bool Checking { get; set; }
    public bool Launching { get; set; }
    public bool DecisionPending => PendingPlan != null;
    public bool ShowOpenPanel => Checking || Launching;
    public bool CanBackToSelection => DecisionPending && !SingleItem;
}
