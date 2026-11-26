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
        for (var elapsed = 0; elapsed <= 60; elapsed += 2)
        {
            if (await Task.Run(HasInternet, cancellationToken)) return true;
            if (elapsed < 60) await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
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
