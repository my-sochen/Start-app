using System.Reflection;
using StartApp.Presentation;

namespace StartApp.App;

internal static class UpdateComposition
{
    // Explicit activation boundary. No live endpoint, network request or publisher is
    // authorized by this feature preparation. Never fall back to prototype pointer.
    private static Uri? ApprovedBetaNoticeEndpoint => null;
    internal static UpdateViewModel Create()
    {
        var assembly = typeof(App).Assembly; // Actual executable, not Presentation/test assembly.
        var metadata = assembly.GetCustomAttributes<AssemblyMetadataAttribute>().ToArray();
        var commits = metadata.Where(a => a.Key == "StartAppSourceCommit").ToArray();
        var identity = InstalledBuildIdentity.Parse(assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            commits.Length == 1 ? commits[0].Value : null);
        IApprovedUpdateSource source = ApprovedBetaNoticeEndpoint is { } endpoint && identity is not null
            ? new BetaNoticeSource(endpoint, identity.Version, BetaNoticeSource.CreateTransport())
            : new InactiveUpdateSource();
        // Identity alone must not make inactive publication look like a working check.
        return new(source, identity?.Version,
            uri => { using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); },
            revalidateBeforeDownload: true);
    }
}
