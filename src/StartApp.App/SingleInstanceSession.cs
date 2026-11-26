using System.Security.Principal;

namespace StartApp.App;

/// <summary>Per-user/session singleton. A second shortcut click reopens the existing window rather than
/// creating a second hidden writer. The updater helper bypasses this in the composition root.</summary>
internal sealed class SingleInstanceSession : IDisposable
{
    private readonly Mutex _mutex;
    private readonly EventWaitHandle _restore;
    private readonly RegisteredWaitHandle? _wait;
    public bool IsFirst { get; }
    public SingleInstanceSession(Action restore)
    {
        var name = @"Local\StartApp-" + WindowsIdentity.GetCurrent().User!.Value;
        _mutex = new Mutex(true, name, out var first); IsFirst = first;
        _restore = new EventWaitHandle(false, EventResetMode.AutoReset, name + "-restore");
        if (first) _wait = ThreadPool.RegisterWaitForSingleObject(_restore, (_, _) => restore(), null, Timeout.Infinite, false);
        else _restore.Set();
    }
    public void Dispose()
    {
        _wait?.Unregister(null); _restore.Dispose();
        if (IsFirst) _mutex.ReleaseMutex();
        _mutex.Dispose();
    }
}
