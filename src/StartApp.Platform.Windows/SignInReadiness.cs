using StartApp.Core;
namespace StartApp.Platform.Windows;

/// <summary>15 seconds to settle, then at most 60 seconds for Windows' internet-connectivity indication.
/// This is not a per-site reachability guarantee. No external URL probes or private data leave the device.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class SignInReadiness : ISignInReadiness
{
    public async Task<bool> WaitAsync(CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(15), cancellationToken);
        var deadline = System.Diagnostics.Stopwatch.StartNew();
        Task<bool>? probe = null;
        while (deadline.Elapsed < TimeSpan.FromSeconds(60))
        {
            cancellationToken.ThrowIfCancellationRequested();
            // At most one native probe, even if Windows' connectivity service stalls.
            probe ??= Task.Run(HasInternet, cancellationToken);
            var remaining = TimeSpan.FromSeconds(60) - deadline.Elapsed;
            var pause = Task.Delay(remaining < TimeSpan.FromSeconds(2) ? remaining : TimeSpan.FromSeconds(2), cancellationToken);
            if (await Task.WhenAny(probe, pause) == probe)
            {
                if (await probe) return true;
                probe = null;
                await pause;
            }
        }
        return false;
    }
    private static bool HasInternet()
    {
        object? manager = null;
        try
        {
            var type = Type.GetTypeFromCLSID(new Guid("DCB00C01-570F-4A9B-8D69-199FDBA5723B"));
            manager = Activator.CreateInstance(type!);
            return ((dynamic)manager!).IsConnectedToInternet;
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or System.Runtime.InteropServices.InvalidComObjectException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { return false; }
        finally { if (manager is not null && System.Runtime.InteropServices.Marshal.IsComObject(manager)) System.Runtime.InteropServices.Marshal.ReleaseComObject(manager); }
    }
}
