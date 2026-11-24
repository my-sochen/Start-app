using System.Runtime.InteropServices;
using StartApp.Core;

namespace StartApp.Platform.Windows;

/// <summary>Uses a shell item ID, never a command line or file association. Shows the saved entry itself,
/// including a shortcut/reparse entry, not its target. The caller must be on a COM-initialized STA (WPF UI).</summary>
public sealed class WindowsFileRevealer(IFileSystemProbe? probe = null, Action<string>? select = null) : IFileRevealer
{
    private readonly IFileSystemProbe _probe = probe ?? new FileSystemProbe();
    private readonly Action<string> _select = select ?? SelectInExplorer;

    public RevealResult Reveal(string path)
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
