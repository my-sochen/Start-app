namespace StartApp.Presentation;

/// <summary>Owns the open-with dialog's saved/staged target and program/default draft.
/// Targets are opaque. No launcher, persistence, commands, view or MainViewModel access.</summary>
public sealed class OpenWithSession<TSaved, TStaged> where TSaved : class where TStaged : class
{
    public bool IsOpen { get; set; }
    public TSaved? SavedTarget { get; set; }
    public TStaged? StagedTarget { get; set; }
    public string Program { get; set; } = "";
    public bool SetDefault { get; set; }

    /// <summary>The explicit close path releases target references after host notification.
    /// Program/default draft and open state are intentionally independent of target cleanup.</summary>
    public void ClearTargets()
    {
        SavedTarget = null; StagedTarget = null;
    }
}
