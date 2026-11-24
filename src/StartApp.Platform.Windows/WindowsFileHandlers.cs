using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Runtime.Versioning;

namespace StartApp.Platform.Windows;

/// <summary>Read Windows' recommended file handlers. Only direct EXE + one file argument
/// registrations fit the current launch model; shell verbs, packaged activation and extra
/// command switches are deliberately not advertised as launchable choices.</summary>
public static class WindowsFileHandlers
{
    public sealed record Handler(string Label, string Path);

    [SupportedOSPlatform("windows")]
    public static IReadOnlyList<Handler> ForTarget(string target)
    {
        var result = new List<Handler>();
        IEnumAssocHandlers? handlers = null;
        try
        {
            var extension = Path.GetExtension(target);
            if (string.IsNullOrWhiteSpace(extension)) return result;
            if (SHAssocEnumHandlers(extension, 1 /* recommended */, out handlers) != 0 || handlers is null) return result;
            while (handlers.Next(1, out var handler, out var fetched) == 0 && fetched == 1)
            {
                try
                {
                    if (handler.GetName(out var path) != 0 || !Path.IsPathFullyQualified(path) || !path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) continue;
                    // Resolve by application executable name, never run or parse a shell command.
                    var command = AssociationCommand(Path.GetFileName(path));
                    if (!DirectFileCommand(command, path)) continue;
                    handler.GetUIName(out var label);
                    if (!result.Any(x => x.Path.Equals(path, StringComparison.OrdinalIgnoreCase))) result.Add(new(string.IsNullOrWhiteSpace(label) ? Path.GetFileNameWithoutExtension(path) : label, path));
                }
                finally { Release(handler); }
            }
        }
        catch (Exception error) when (error is not (OutOfMemoryException or StackOverflowException or AccessViolationException)) { }
        finally { if (handlers is not null) Release(handlers); }
        return result;
    }

    [SupportedOSPlatform("windows")]
    private static void Release(object instance)
    {
        try { Marshal.ReleaseComObject(instance); }
        catch (Exception error) when (error is COMException or InvalidComObjectException or ArgumentException) { }
    }

    private static string AssociationCommand(string executable)
    {
        uint length = 0;
        AssocQueryString(2 /* INIT_BYEXENAME */, 1 /* COMMAND */, executable, "open", null, ref length);
        if (length is 0 or > 32768) return "";
        var text = new StringBuilder((int)length);
        return AssocQueryString(2, 1, executable, "open", text, ref length) == 0 ? text.ToString() : "";
    }

    public static bool DirectFileCommand(string command, string executable)
    {
        command = Environment.ExpandEnvironmentVariables(command).Trim();
        if (command.Length == 0) return false;
        string program, arguments;
        if (command[0] == '"')
        {
            var end = command.IndexOf('"', 1);
            if (end < 0) return false;
            program = command[1..end]; arguments = command[(end + 1)..].Trim();
        }
        else
        {
            var end = command.IndexOfAny([' ', '\t']);
            if (end < 0) return false;
            program = command[..end]; arguments = command[end..].Trim();
        }
        return program.Equals(executable, StringComparison.OrdinalIgnoreCase)
            && new[] { "\"%1\"", "%1", "\"%l\"", "%l" }.Contains(arguments, StringComparer.OrdinalIgnoreCase);
    }

    [DllImport("Shlwapi.dll", CharSet = CharSet.Unicode)]
    private static extern int AssocQueryString(uint flags, uint kind, string association, string extra, StringBuilder? output, ref uint characters);
    [DllImport("Shell32.dll", CharSet = CharSet.Unicode, PreserveSig = true)]
    private static extern int SHAssocEnumHandlers(string extension, uint filter, out IEnumAssocHandlers handlers);

    [ComImport, Guid("973810AE-9599-4B88-9E4D-6EE98C9552DA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IEnumAssocHandlers
    {
        [PreserveSig] int Next(uint count, [MarshalAs(UnmanagedType.Interface)] out IAssocHandler handler, out uint fetched);
    }
    [ComImport, Guid("F04061AC-1659-4A3F-A954-775AA57FC083"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IAssocHandler
    {
        [PreserveSig] int GetName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        [PreserveSig] int GetUIName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        [PreserveSig] int GetIconLocation([MarshalAs(UnmanagedType.LPWStr)] out string path, out int index);
        [PreserveSig] int IsRecommended();
        [PreserveSig] int MakeDefault([MarshalAs(UnmanagedType.LPWStr)] string description);
        [PreserveSig] int Invoke(IntPtr dataObject);
        [PreserveSig] int CreateInvoker(IntPtr dataObject, out IntPtr invoker);
    }
}
