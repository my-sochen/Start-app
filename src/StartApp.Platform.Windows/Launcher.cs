using System.Diagnostics;
using StartApp.Core;
namespace StartApp.Platform.Windows;

/// <summary>What will be started. Arguments are separate strings (ProcessStartInfo.ArgumentList, no escaping or parsing by us). UseShell is only ever
/// true for a File/Folder/Link with no explicit program, where FileName is the single saved path or http(s) URL.</summary>
public sealed record StartRequest(string FileName, IReadOnlyList<string> Arguments, bool UseShell);
public interface IProcessStarter { void Start(StartRequest request); }

public sealed class ProcessStarter : IProcessStarter
{
    public void Start(StartRequest r)
    {
        var psi = new ProcessStartInfo(r.FileName) { UseShellExecute = r.UseShell };
        foreach (var a in r.Arguments) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi); // handle is released immediately: process identity is not window/resource ownership
    }
}

/// <summary>Opens one item. We never build a command line, call cmd.exe or search PATH ourselves. That is NOT a guarantee about what the Windows shell does when
/// UseShell is true: the shell resolves the file association and may run a handler. For that reason File/Folder paths with executable or script-capable extensions,
/// alternate-stream syntax or a non-absolute form are refused before reaching the shell (PreflightRules.ShellOpenBlockReason); other types still depend on the user's
/// associations, and the check cannot see through reparse points or changes after the check. Ownership is always Unknown, so nothing started here can ever be closed by the app.</summary>
public sealed class WindowsLauncher(IProcessStarter starter) : IItemLauncher
{
    public LaunchResult Launch(Item item, LaunchConfig? launch)
    {
        var form = PreflightRules.CheckForm(item, launch);
        if (form != null) return new(LaunchStatus.Unavailable, Detail: form.Detail);
        var req = item.Type switch
        {
            ItemType.Program => new StartRequest(item.Target, [], false),
            ItemType.Link when launch?.Browser is { } b => new StartRequest(b, [item.Target], false),
            ItemType.File or ItemType.Folder when launch?.OpenWith is { } w => new StartRequest(w, [item.Target], false),
            _ => new StartRequest(item.Target, [], true),
        };
        try { starter.Start(req); return new(LaunchStatus.Started, Ownership.Unknown); }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or FileNotFoundException or IOException or UnauthorizedAccessException)
        { return new(LaunchStatus.Failed, Detail: e.GetType().Name + ": " + e.Message); }
    }
}

/// <summary>Real detection of open programs (any process with a visible main window, by its exe path) and open folders (File Explorer windows, by their location).
/// It cannot see documents opened inside programs (Windows has no general way to ask) and says so in a note; sites come from a separate browser source.</summary>
public sealed class WindowsOpenItemCapturer : IOpenItemCapturer
{
    public CaptureScan Scan()
    {
        var items = new List<CapturedItem>(); var notes = new List<string>();
        try
        {
            foreach (var p in System.Diagnostics.Process.GetProcesses())
            {
                try
                {
                    if (p.MainWindowHandle == IntPtr.Zero || string.IsNullOrWhiteSpace(p.MainWindowTitle)) continue;
                    if (CaptureRules.NonProgramProcesses.Contains(p.ProcessName)) continue;
                    var path = p.MainModule?.FileName;
                    if (string.IsNullOrEmpty(path) || !Catalog.IsAbsoluteWindowsPath(path)) continue;
                    var desc = p.MainModule?.FileVersionInfo.FileDescription;
                    items.Add(new CapturedItem(ItemType.Program, string.IsNullOrWhiteSpace(desc) ? Path.GetFileNameWithoutExtension(path) : desc!, path));
                }
                catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or NotSupportedException) { /* protected or exited process: not listed */ }
                finally { p.Dispose(); }
            }
        }
        catch (Exception e) when (e is System.ComponentModel.Win32Exception or InvalidOperationException or PlatformNotSupportedException) { notes.Add("Programs: could not read the process list (" + e.GetType().Name + ")"); }
        try
        {
            var t = Type.GetTypeFromProgID("Shell.Application");
            if (t is null) notes.Add("Folders: File Explorer windows could not be read on this system");
            else
            {
                dynamic shell = Activator.CreateInstance(t)!;
                foreach (dynamic w in shell.Windows())
                {
                    try
                    {
                        string full = w.FullName ?? "";
                        if (!full.EndsWith("explorer.exe", StringComparison.OrdinalIgnoreCase)) continue;
                        var path = CaptureRules.FileUrlToPath((string?)w.LocationURL);
                        if (path is null) continue;
                        var name = Path.GetFileName(path.TrimEnd('\\'));
                        items.Add(new CapturedItem(ItemType.Folder, string.IsNullOrEmpty(name) ? path : name, path));
                    }
                    catch (Exception e) when (e is System.Runtime.InteropServices.COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException) { }
                }
            }
        }
        catch (Exception e) when (e is System.Runtime.InteropServices.COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or PlatformNotSupportedException or NotSupportedException) { notes.Add("Folders: could not read File Explorer windows (" + e.GetType().Name + ")"); }
        notes.Add("Files: documents open inside programs are not detected by the current version");
        return new(CaptureRules.Dedupe(items), notes);
    }
}
