namespace StartApp.Core;

public enum LaunchStatus { Started, Failed, Unavailable }
/// <summary>Whether the adapter could attribute a window/document to OUR launch. PID or a new HWND alone is not proof (instance redirection, HWND reuse).</summary>
public enum Ownership { Unknown, Confirmed }
public sealed record LaunchResult(LaunchStatus Status, Ownership Ownership = Ownership.Unknown, string? OpaqueHandle = null, string? Detail = null);

public enum CloseOutcome { Closed, Refused, NotOwned, Unsupported }

/// <summary>Port only; no implementation yet and no Windows behavior is verified. Launches an item with an explicit, validated executable (never a shell command line).</summary>
public interface IItemLauncher
{
    LaunchResult Launch(Item item, LaunchConfig? launch);
}

/// <summary>Adapter-side graceful close of something it launched. Do not call directly: use <see cref="OwnedClose.Try"/>, which enforces the
/// ownership rule before the adapter is reached. No force-kill exists in this contract.</summary>
public interface IItemCloser
{
    CloseOutcome TryCloseGracefully(string opaqueHandle);
}

/// <summary>The single entry point for closing. Ownership Unknown (or no handle) never reaches the adapter.</summary>
public static class OwnedClose
{
    public static CloseOutcome Try(IItemCloser closer, LaunchResult launch)
        => launch.Status == LaunchStatus.Started && launch.Ownership == Ownership.Confirmed && !string.IsNullOrEmpty(launch.OpaqueHandle)
            ? closer.TryCloseGracefully(launch.OpaqueHandle)
            : CloseOutcome.NotOwned;
}

/// <summary>One open resource found by a capture source: the resource's original name and its exact address. Browser is the
/// full exe path of the browser a site was found in (null when unknown or not a site): a captured site keeps its source browser as the item's default browser (spec Nov 11:
/// דפדפן שזוהה בזמן הלכידה יישמר; browser profiles are deferred until after the prototype).</summary>
public sealed record CapturedItem(ItemType Type, string Name, string Target, string? Browser = null);

/// <summary>Result of one scan. Notes say honestly what this source cannot see (shown on the capture screen), so a partial detector never looks complete.</summary>
public sealed record CaptureScan(IReadOnlyList<CapturedItem> Items, IReadOnlyList<string> Notes);

/// <summary>Port: lists what is open on the machine at this moment (לכידה). Implementations must never guess: only what the OS or the app itself reported.</summary>
public interface IOpenItemCapturer { CaptureScan Scan(); }

/// <summary>No detection available (tests, non-Windows).</summary>
public sealed class NoCapture : IOpenItemCapturer
{
    public CaptureScan Scan() => new(Array.Empty<CapturedItem>(), new[] { "Detection of open items is not available here" });
}

/// <summary>Runs several sources and merges them; one failing source never hides the others (its failure is reported as a note).</summary>
public sealed class CompositeCapturer(params IOpenItemCapturer[] sources) : IOpenItemCapturer
{
    public CaptureScan Scan()
    {
        var items = new List<CapturedItem>(); var notes = new List<string>();
        foreach (var s in sources)
        {
            try { var r = s.Scan(); items.AddRange(r.Items); notes.AddRange(r.Notes); }
            catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or System.Runtime.InteropServices.COMException or UnauthorizedAccessException or NotSupportedException)
            { notes.Add(s.GetType().Name + " failed: " + e.GetType().Name); }
        }
        return new(CaptureRules.Dedupe(items), notes);
    }
}

public static class CaptureRules
{
    /// <summary>Scan identity is Type+Target, and for links ALSO the source browser (owner decision Nov 14): the same site
    /// open in two browsers stays two choices, one row per browser with its browser named, so the user picks what to keep -
    /// the software never picks a browser by scan order. Within one browser (or when no browser was identified) the first
    /// row still wins. Order is kept.</summary>
    public static IReadOnlyList<CapturedItem> Dedupe(IEnumerable<CapturedItem> items)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal); var list = new List<CapturedItem>();
        foreach (var i in items) if (seen.Add(Catalog.Key(i.Type, i.Target) + "\u0000" + (i.Browser ?? ""))) list.Add(i);
        return list;
    }

    /// <summary>Short display name for a captured browser's exe path, shown next to a captured site so the user can tell two
    /// browsers apart; null when no browser was identified. Known executables map to their product name, anything else keeps
    /// its file name (without extension) as-is.</summary>
    public static string? BrowserDisplayName(string? exePath)
    {
        if (string.IsNullOrEmpty(exePath)) return null;
        var cut = exePath.LastIndexOfAny(new[] { '\\', '/' });
        var file = cut >= 0 ? exePath[(cut + 1)..] : exePath;
        var stem = file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? file[..^4] : file;
        return stem.ToLowerInvariant() switch
        {
            "msedge" => "Edge",
            "chrome" => "Chrome",
            "firefox" => "Firefox",
            "brave" => "Brave",
            "opera" => "Opera",
            _ => stem,
        };
    }

    /// <summary>file:///C:/Users/a%20b -> C:\Users\a b; null when it is not a local file URL (or not an absolute drive/UNC path).</summary>
    public static string? FileUrlToPath(string? url)
    {
        if (string.IsNullOrEmpty(url) || !Uri.TryCreate(url, UriKind.Absolute, out var u) || !u.IsFile) return null;
        var p = u.LocalPath;
        return Catalog.IsAbsoluteWindowsPath(p) ? p : null;
    }

    /// <summary>Browsers hide the scheme in the address bar. Accepts only a single token that parses as an http(s) URL after adding https:// when no scheme is shown; anything else (search text, internal pages) is not a site.</summary>
    public static string? SiteFromAddressText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Any(char.IsWhiteSpace) || text.Any(char.IsControl)) return null;
        var t = text.Contains("://") ? text : "https://" + text;
        if (!Uri.TryCreate(t, UriKind.Absolute, out var u) || (u.Scheme != "http" && u.Scheme != "https") || !u.Host.Contains('.')) return null;
        return t;
    }

    /// <summary>Process names that are shell/system hosts, not user programs.</summary>
    public static readonly IReadOnlySet<string> NonProgramProcesses = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    { "explorer", "ApplicationFrameHost", "TextInputHost", "SearchHost", "ShellExperienceHost", "StartMenuExperienceHost", "LockApp", "SystemSettings", "StartApp.App", "dwm", "csrss", "RuntimeBroker" };
}
