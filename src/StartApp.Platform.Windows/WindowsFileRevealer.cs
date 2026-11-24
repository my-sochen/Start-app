using System.Runtime.InteropServices;
using StartApp.Core;

namespace StartApp.Platform.Windows;

/// <summary>Uses a shell item ID, never a command line or file association. Shows the saved entry itself,
/// including a shortcut/reparse entry, not its target. Shell resources never leave the dedicated worker STA.</summary>
public sealed class WindowsFileRevealer(IFileSystemProbe? probe = null, Action<string>? select = null,
    Func<string, (bool Selected, bool FolderReady)>? confirm = null) : IFileRevealer
{
    private int _active;
    private readonly IFileSystemProbe _probe = probe ?? new FileSystemProbe();
    private readonly Action<string> _select = select ?? SelectInExplorer;

    private RevealResult RequestReveal(string path)
    {
        if (!ValidPath(path)) return new(RevealStatus.Invalid);
        var found = _probe.Probe(path);
        if (found.Kind == ProbeKind.NotFound) return new(RevealStatus.Missing);
        if (found.Kind == ProbeKind.Error) return new(RevealStatus.Unavailable, found.Reason);
        if (found.Kind != ProbeKind.File) return new(RevealStatus.Invalid);
        try { _select(path); return new(RevealStatus.Revealed); }
        catch (Exception e) when (e is COMException or System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException or ArgumentException or PlatformNotSupportedException)
        { return new(RevealStatus.Unavailable, e.GetType().Name + ": " + e.Message); }
    }

    public Task<RevealResult> RevealAsync(string path)
    {
        // Do not clear this gate on an awaiter's timeout/abandonment. A blocked native call still
        // owns this adapter until its worker returns and releases all resources on its own STA.
        if (Interlocked.CompareExchange(ref _active, 1, 0) != 0)
            return Task.FromResult(new RevealResult(RevealStatus.Unavailable, "Another reveal is still active."));
        var completion = new TaskCompletionSource<RevealResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        var worker = new Thread(() =>
        {
            RevealResult result;
            var initialized = false;
            try
            {
                Marshal.ThrowExceptionForHR(CoInitializeEx(IntPtr.Zero, 2));
                initialized = true;
                result = RevealOnWorker(path);
            }
            catch (Exception e) { result = new(RevealStatus.Unavailable, e.GetType().Name + ": " + e.Message); }
            finally
            {
                if (initialized) CoUninitialize();
                Interlocked.Exchange(ref _active, 0);
            }
            completion.TrySetResult(result);
        }) { IsBackground = true, Name = "StartApp Explorer reveal STA" };
        try { worker.SetApartmentState(ApartmentState.STA); worker.Start(); }
        catch (Exception e)
        {
            Interlocked.Exchange(ref _active, 0);
            completion.TrySetResult(new(RevealStatus.Unavailable, e.GetType().Name + ": " + e.Message));
        }
        return completion.Task;
    }

    private RevealResult RevealOnWorker(string path)
    {
        var request = RequestReveal(path);
        if (request.Status != RevealStatus.Revealed || (select is not null && confirm is null)) return request;
        var retried = false;
        // These delays limit retries, not native call duration. All probing, shell calls, COM
        // inspection and final COM releases stay on this background STA, never the WPF thread.
        for (var i = 0; i < 40; i++)
        {
            var state = Confirm(path);
            if (state.Selected) return request;
            if (state.FolderReady && !retried)
            {
                retried = true;
                request = RequestReveal(path);
                if (request.Status != RevealStatus.Revealed) return request;
            }
            Thread.Sleep(150);
        }
        return new(RevealStatus.Unavailable, "Explorer did not confirm the selected item.");
    }

    private (bool Selected, bool FolderReady) Confirm(string path)
    {
        if (confirm is not null) return confirm(path);
        var selected = ExplorerSelection.Contains(path, out var ready);
        return (selected, ready);
    }

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(IntPtr reserved, uint mode);
    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();

    // Only explicit drive/UNC file paths. Refuse shell names, device paths, streams, traversal and
    // wildcard/control syntax rather than letting the shell reinterpret them. Never resolve PATH aliases.
    public static bool ValidPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Catalog.IsAbsoluteWindowsPath(path) || path.StartsWith(@"\\?\") || path.StartsWith(@"\\.\")) return false;
        if (path.Any(c => char.IsControl(c) || "\"<>|?*".Contains(c)) || path.Contains('/')) return false;
        var rest = path.Length > 1 && path[1] == ':' ? path[2..] : path;
        if (rest.Contains(':')) return false;
        return rest.TrimStart('\\').Split('\\').All(part => part.Length > 0 && part != "." && part != ".." && !part.EndsWith(' ') && !part.EndsWith('.'));
    }

    private static void SelectInExplorer(string path)
    {
        IntPtr item = IntPtr.Zero, folder = IntPtr.Zero, selection = IntPtr.Zero;
        try
        {
            Marshal.ThrowExceptionForHR(SHParseDisplayName(path, IntPtr.Zero, out item, 0, out _));
            var parent = Path.GetDirectoryName(path)!;
            Marshal.ThrowExceptionForHR(SHParseDisplayName(parent, IntPtr.Zero, out folder, 0, out _));
            // Supply the parent folder plus a child-relative ID. Some Explorer versions open
            // the parent but fail to select when given only a full item with cidl=0.
            selection = Marshal.AllocCoTaskMem(IntPtr.Size);
            Marshal.WriteIntPtr(selection, ILFindLastID(item));
            Marshal.ThrowExceptionForHR(SHOpenFolderAndSelectItems(folder, 1, selection, 0));
        }
        finally
        {
            if (selection != IntPtr.Zero) Marshal.FreeCoTaskMem(selection);
            if (folder != IntPtr.Zero) Marshal.FreeCoTaskMem(folder);
            if (item != IntPtr.Zero) Marshal.FreeCoTaskMem(item);
        }
    }
    [DllImport("shell32.dll")]
    private static extern IntPtr ILFindLastID(IntPtr pidl);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHParseDisplayName(string name, IntPtr bind, out IntPtr pidl, uint attributes, out uint foundAttributes);
    [DllImport("shell32.dll")]
    private static extern int SHOpenFolderAndSelectItems(IntPtr pidl, uint count, IntPtr children, uint flags);
}
