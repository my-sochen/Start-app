namespace StartApp.Core;

/// <summary>Immutable observation for one opening attempt. Implementations must not reuse a stale previous successful snapshot on failure.</summary>
public interface IOpenSnapshotSource : IOpenStateDetector
{
    Task<IOpenStateDetector> ObserveAsync(IReadOnlyCollection<Item> selected, CancellationToken token);
}

/// <summary>Owner-approved Nov17 policy: only scheme/ASCII DNS host case and EMPTY root path/slash equivalence. No network requests or URI rewriting.</summary>
public static class ExactObservedUrl
{
    public static string? Key(string value)
    {
        if (PreflightRules.LinkFormProblem(value) is not null) return null;
        var schemeEnd = value.IndexOf("://", StringComparison.Ordinal); if (schemeEnd < 0) return null;
        var start = schemeEnd + 3; var cut = value.IndexOfAny(new[] {'/','?','#'}, start);
        if (cut < 0) cut = value.Length;
        var authority = value[start..cut];
        // DNS host only for now. Other authority forms are Unknown rather than silently normalized.
        var portStart = authority.LastIndexOf(':');
        var host = portStart < 0 ? authority : authority[..portStart]; var port = portStart < 0 ? "" : authority[portStart..];
        if (host.Length == 0 || host.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '.' or '-'))) return null;
        if (port.Length > 0 && (port.Length == 1 || port[1..].Any(c => c is < '0' or > '9'))) return null;
        var remainder = value[cut..];
        if (remainder.Length == 0 || remainder[0] is '?' or '#') remainder = "/" + remainder;
        return value[..schemeEnd].ToLowerInvariant() + "://" + host.ToLowerInvariant() + port + remainder;
    }
    public static bool Same(string saved, string observed) => Key(saved) is { } left && Key(observed) is { } right && left == right;
}

/// <summary>Only positive exact evidence can confirm Open. Absence, inaccessible and incomplete coverage all remain Unknown.</summary>
public sealed class ObservedOpenSnapshot(IEnumerable<CapturedItem> observations) : IOpenStateDetector
{
    private readonly CapturedItem[] _items = observations.ToArray();
    public OpenState Detect(Item item, LaunchConfig? launch)
    {
        if (item.Type == ItemType.File) return OpenState.Unknown; // no generic document inspection exists
        var found = _items.Any(o => o.Type == item.Type && (item.Type == ItemType.Link
            ? ExactObservedUrl.Same(item.Target, o.Target)
            : string.Equals(item.Target, o.Target, StringComparison.Ordinal)));
        return found ? OpenState.Open : OpenState.Unknown;
    }
}
