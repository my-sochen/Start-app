using System.Text.Json;
using System.Text.RegularExpressions;

namespace StartApp.Presentation;

// Offline, pure syntax/identity/freshness contract. Not approval authentication.
// Production transport and composition remain inactive. Only an authenticated,
// reviewed publisher may supply eligible records after exact owner approval.
public sealed record CatalogPolicy(int MaximumBytes, int MaximumRecords, TimeSpan MaximumAge, TimeSpan MaximumLifetime);
public sealed record CatalogRelease(string Version, string Tag, string Commit, long ReleaseId,
    long AssetId, string Asset, long Size, string Sha256, string Layout, string ApprovalRecordId);
public sealed record ValidatedCatalog(long Revision, DateTimeOffset GeneratedUtc, DateTimeOffset ExpiresUtc,
    IReadOnlyList<CatalogRelease> Releases)
{
    public CatalogRelease? SelectNewer(string installedVersion)
    {
        var current = UpdateVersion.Parse(installedVersion) ?? throw new ArgumentException("Invalid installed version.");
        // First contract is explicitly prototype-only. No invented stable publisher.
        if (current.Prototype is null) return null;
        return Releases.Where(r => UpdateVersion.Parse(r.Version)!.CompareTo(current) > 0)
            .OrderByDescending(r => UpdateVersion.Parse(r.Version)).FirstOrDefault();
    }
}

public static class ApprovedCatalogContract
{
    private static readonly string[] EnvelopeFields = ["schema", "revision", "generated_utc", "expires_utc", "channel", "releases"];
    private static readonly string[] RecordFields = ["version", "tag", "commit", "release_id", "asset_id", "asset", "size", "sha256", "layout", "approval_record_id"];
    public static ValidatedCatalog Parse(ReadOnlyMemory<byte> bytes, DateTimeOffset now, CatalogPolicy policy, long? minimumRevision = null)
    {
        if (policy.MaximumBytes <= 0 || policy.MaximumRecords <= 0 || policy.MaximumAge <= TimeSpan.Zero || policy.MaximumLifetime <= TimeSpan.Zero || minimumRevision is <= 0)
            throw new ArgumentOutOfRangeException(nameof(policy));
        if (bytes.Length > policy.MaximumBytes) throw Bad("Response byte limit.");
        try
        {
            using var document = JsonDocument.Parse(bytes, new JsonDocumentOptions { MaxDepth = 16 });
            var root = document.RootElement; Fields(root, EnvelopeFields);
            if (Integer(root, "schema") != 1 || Text(root, "channel", 16) != "prototype") throw Bad("Unsupported schema/channel.");
            var revision = Integer(root, "revision");
            if (minimumRevision is long minimum && revision < minimum) throw Bad("Revision rollback.");
            var generated = Utc(root, "generated_utc"); var expires = Utc(root, "expires_utc");
            if (generated > now || expires <= now || now - generated > policy.MaximumAge || expires <= generated || expires - generated > policy.MaximumLifetime)
                throw Bad("Stale, future or invalid lifetime.");
            var array = root.GetProperty("releases");
            if (array.ValueKind != JsonValueKind.Array || array.GetArrayLength() > policy.MaximumRecords) throw Bad("Record count/type.");
            var releases = new List<CatalogRelease>(); var versions = new HashSet<string>(StringComparer.Ordinal); var assets = new HashSet<long>();
            foreach (var element in array.EnumerateArray())
            {
                Fields(element, RecordFields);
                var version = Text(element, "version", 64); var parsed = UpdateVersion.Parse(version);
                var tag = Text(element, "tag", 65);
                if (parsed?.Prototype is null || tag != "v" + version) throw Bad("Prototype version/tag mismatch.");
                var commit = Text(element, "commit", 40); var hash = Text(element, "sha256", 64);
                if (!Regex.IsMatch(commit, "\\A[0-9a-f]{40}\\z") || !Regex.IsMatch(hash, "\\A[0-9a-f]{64}\\z")) throw Bad("Commit/hash syntax.");
                var releaseId = Integer(element, "release_id"); var assetId = Integer(element, "asset_id"); var size = Integer(element, "size");
                var asset = Text(element, "asset", 64); var layout = Text(element, "layout", 32); var approval = Text(element, "approval_record_id", 128);
                if (asset != "StartApp-win-x64.zip" || layout != "portable-v1" || !Regex.IsMatch(approval, "\\A[A-Za-z0-9._-]{1,128}\\z")) throw Bad("Asset/layout/approval reference syntax.");
                if (!versions.Add(version) || !assets.Add(assetId)) throw Bad("Duplicate version/asset identity.");
                releases.Add(new(version, tag, commit, releaseId, assetId, asset, size, hash, layout, approval));
            }
            return new(revision, generated, expires, releases.AsReadOnly());
        }
        catch (JsonException ex) { throw new InvalidDataException("Invalid catalog JSON.", ex); }
    }
    private static InvalidDataException Bad(string message) => new(message);
    private static void Fields(JsonElement element, string[] expected)
    {
        if (element.ValueKind != JsonValueKind.Object) throw Bad("Expected object.");
        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject()) if (!names.Add(property.Name)) throw Bad("Duplicate JSON property.");
        if (!names.SetEquals(expected)) throw Bad("Unknown or missing fields.");
    }
    private static string Text(JsonElement element, string name, int maximum)
    {
        var value = element.GetProperty(name);
        if (value.ValueKind != JsonValueKind.String) throw Bad("Expected string.");
        var text = value.GetString()!;
        if (text.Length == 0 || text.Length > maximum) throw Bad("String length.");
        return text;
    }
    private static long Integer(JsonElement element, string name)
    {
        var value = element.GetProperty(name);
        if (value.ValueKind != JsonValueKind.Number || !Regex.IsMatch(value.GetRawText(), "\\A[1-9][0-9]*\\z") || !value.TryGetInt64(out var result) || result <= 0) throw Bad("Expected positive integer.");
        return result;
    }
    private static DateTimeOffset Utc(JsonElement element, string name)
    {
        var value = Text(element, name, 20);
        if (!DateTimeOffset.TryParseExact(value, "yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal, out var time)) throw Bad("UTC seconds required.");
        return time;
    }
}
