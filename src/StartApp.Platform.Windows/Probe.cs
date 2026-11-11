using StartApp.Core;
namespace StartApp.Platform.Windows;

public enum ProbeKind { NotFound, File, Directory, Error }
public sealed record ProbeResult(ProbeKind Kind, string? Reason = null, bool Reparse = false);

/// <summary>Reads what is at a path without trusting File.Exists/Directory.Exists, which return false for absent, denied, IO error and bad path alike.</summary>
public interface IFileSystemProbe { ProbeResult Probe(string path); }

/// <summary>Real probe: File.GetAttributes throws distinguishable exceptions. The wait is bounded so a dead network share cannot hang Open, BUT a timed-out
/// GetAttributes call cannot be cancelled and keeps its worker until the OS call returns. To avoid leaking unbounded workers, at most
/// <see cref="MaxInFlight"/> probes may be unfinished at once per probe instance (the app uses one); beyond that a probe is not started and reports Unavailable.</summary>
public sealed class FileSystemProbe(TimeSpan? timeout = null) : IFileSystemProbe
{
    public const int MaxInFlight = 8;
    private readonly SemaphoreSlim Slots = new(MaxInFlight, MaxInFlight);
    private readonly TimeSpan _timeout = timeout ?? TimeSpan.FromSeconds(3);
    public ProbeResult Probe(string path) => Probe(path, ProbeNow);
    public ProbeResult Probe(string path, Func<string, ProbeResult> work)
    {
        if (!Slots.Wait(0)) return new(ProbeKind.Error, "too many unfinished checks");
        Task<ProbeResult> t;
        try { t = Task.Run(() => { try { return work(path); } finally { Slots.Release(); } }); }
        catch { Slots.Release(); throw; }
        return t.Wait(_timeout) ? t.Result : new(ProbeKind.Error, "check timed out");
    }
    internal static ProbeResult ProbeNow(string path)
    {
        try { var a = File.GetAttributes(path); var rp = a.HasFlag(FileAttributes.ReparsePoint); return a.HasFlag(FileAttributes.Directory) ? new(ProbeKind.Directory, null, rp) : new(ProbeKind.File, null, rp); }
        catch (Exception e) { return ProbeMapping.FromException(e); }
    }
}

public static class ProbeMapping
{
    public static ProbeResult FromException(Exception e) => e switch
    {
        FileNotFoundException or DirectoryNotFoundException => new(ProbeKind.NotFound),
        _ => new(ProbeKind.Error, e.GetType().Name),
    };
}

/// <summary>Maps probe results to preflight findings. No PATH search, no fallback, no launching.</summary>
public sealed class FileSystemPreflight(IFileSystemProbe probe) : IItemPreflight
{
    public PreflightFinding Check(Item item, LaunchConfig? launch)
    {
        if (item.Type == ItemType.Link) return new(PreflightState.Ready);
        var r = probe.Probe(item.Target);
        var finding = Map(item.Type, r, "saved target");
        if (finding.State != PreflightState.Ready) return finding;
        if (r.Reparse) return new(PreflightState.NeedsDecision, "saved target is a link (reparse point); its real target was not verified");
        foreach (var exe in new[] { launch?.OpenWith, launch?.Browser })
            if (exe is not null) { var pr = probe.Probe(exe); var f = Map(ItemType.Program, pr, "launch program"); if (f.State != PreflightState.Ready) return f; if (pr.Reparse) return new(PreflightState.NeedsDecision, "launch program is a link (reparse point); its real target was not verified"); }
        return finding;
    }
    private static PreflightFinding Map(ItemType type, ProbeResult r, string what) => r.Kind switch
    {
        ProbeKind.NotFound => new(PreflightState.Missing, what),
        ProbeKind.Error => new(PreflightState.Unavailable, r.Reason),
        ProbeKind.Directory when type != ItemType.Folder => new(PreflightState.Unsupported, $"{what} is a folder but is saved as {type}"),
        ProbeKind.File when type == ItemType.Folder => new(PreflightState.Unsupported, $"{what} is a file but is saved as Folder"),
        _ => new(PreflightState.Ready),
    };
}
