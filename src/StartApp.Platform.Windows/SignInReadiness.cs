using StartApp.Core;
namespace StartApp.Platform.Windows;

/// <summary>Local readiness at 10 seconds. Sites join at 10 when online, otherwise when online is detected,
/// or at 25 seconds even offline. No external URL probes. The OS can still delay actual opening.</summary>
[System.Runtime.Versioning.SupportedOSPlatform("windows")]
public sealed class SignInReadiness : ISignInReadiness
{
    public SignInSchedule Start(CancellationToken token)
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        var local = Task.Delay(TimeSpan.FromSeconds(10), token);
        return new(local, WaitForSites(local, clock, token));
    }
    private static async Task<bool> WaitForSites(Task local, System.Diagnostics.Stopwatch clock, CancellationToken token)
    {
        Task<bool>? probe = null;
        while (clock.Elapsed < TimeSpan.FromSeconds(25))
        {
            token.ThrowIfCancellationRequested();
            probe ??= Task.Run(HasInternet, token);
            var remaining = TimeSpan.FromSeconds(25) - clock.Elapsed;
            if (remaining <= TimeSpan.Zero) break;
            var pause = Task.Delay(remaining < TimeSpan.FromMilliseconds(250) ? remaining : TimeSpan.FromMilliseconds(250), token);
            if (await Task.WhenAny(probe, pause) == probe)
            {
                if (await probe) { await local; return true; }
                probe = null;
                await pause;
            }
        }
        token.ThrowIfCancellationRequested();
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
