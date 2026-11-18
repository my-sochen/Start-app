using System.Net.Http;
using System.Reflection;
using StartApp.Presentation;

namespace StartApp.App;

internal static class UpdateComposition
{
    // Working notice-only composition. No beta enrollment is implied: reviewed
    // MAIN candidate:null yields no offer; missing/unknown source is unavailable.
    // Rolling catalog endpoint remains dormant and is never a fallback.
    private static Uri? ApprovedBetaNoticeEndpoint => null;
    internal static UpdateViewModel Create() => CreateWithMainNoticeTransport(MainBetaNoticeSource.CreateTransport(),
        uri => { using var process=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri){UseShellExecute=true}); },
        () => DateTimeOffset.UtcNow);
    // Internal exact-product native-test seam; production uses this same factory.
    // No environment/CLI endpoint or descriptor override is exposed.
    internal static UpdateViewModel CreateWithMainNoticeTransport(HttpMessageHandler handler, Action<Uri> browser, Func<DateTimeOffset> clock)
    {
        var assembly=typeof(App).Assembly;
        var metadata=assembly.GetCustomAttributes<AssemblyMetadataAttribute>().Where(a=>a.Key=="StartAppSourceCommit").ToArray();
        var identity=InstalledBuildIdentity.Parse(assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            metadata.Length==1?metadata[0].Value:null);
        IApprovedUpdateSource source=identity is null ? new InactiveUpdateSource() : new MainBetaNoticeSource(identity.Version,handler,clock);
        if(identity is null)handler.Dispose();
        return new(source,identity?.Version,browser,revalidateBeforeDownload:true);
    }
    // Internal test seam only. No environment/argument/config override is exposed by the product.
    internal static UpdateViewModel CreateWithTransport(Uri? endpoint, HttpMessageHandler? handler,
        Action<Uri>? browser, Func<DateTimeOffset>? clock)
    {
        var assembly = typeof(App).Assembly; // Actual executable, not Presentation/test assembly.
        var metadata = assembly.GetCustomAttributes<AssemblyMetadataAttribute>().ToArray();
        var commits = metadata.Where(a => a.Key == "StartAppSourceCommit").ToArray();
        var identity = InstalledBuildIdentity.Parse(assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion,
            commits.Length == 1 ? commits[0].Value : null);
        IApprovedUpdateSource source = endpoint is not null && identity is not null
            ? new BetaNoticeSource(endpoint, identity.Version, handler ?? BetaNoticeSource.CreateTransport(), clock)
            : new InactiveUpdateSource();
        // Identity alone must not make inactive publication look like a working check.
        return new(source, identity?.Version,
            browser ?? (uri => { using var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(uri.AbsoluteUri) { UseShellExecute = true }); }),
            revalidateBeforeDownload: true);
    }
}
