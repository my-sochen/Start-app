using System.Text.RegularExpressions;

namespace StartApp.Presentation;

// Pure stamp contract only. Caller must read the executing application assembly,
// not a library/test assembly. Default assembly 1.0.0 is not an update identity.
public sealed record InstalledBuildIdentity(string Version, string Commit)
{
    public static InstalledBuildIdentity? Parse(string? informationalVersion, string? sourceCommit)
    {
        if (informationalVersion is null || informationalVersion.Length > 112 ||
            sourceCommit is null || !Regex.IsMatch(sourceCommit, "\\A[0-9a-f]{40}\\z")) return null;
        var suffix = "+sha." + sourceCommit;
        if (!informationalVersion.EndsWith(suffix, StringComparison.Ordinal)) return null;
        var version = informationalVersion[..^suffix.Length];
        return UpdateVersion.Parse(version) is null ? null : new(version, sourceCommit);
    }
}
