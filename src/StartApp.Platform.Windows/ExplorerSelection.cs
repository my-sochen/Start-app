using System.Runtime.InteropServices;

namespace StartApp.Platform.Windows;

/// <summary>Reads Explorer's own selected item paths. The shell API can return success before a newly
/// opened Explorer window is ready, so that return alone is not proof of selection.</summary>
internal static class ExplorerSelection
{
    internal static bool Contains(string path, out bool folderReady)
    {
        folderReady = false;
        object? shell = null, windows = null;
        try
        {
            shell = Activator.CreateInstance(Type.GetTypeFromProgID("Shell.Application")!);
            dynamic s = shell!; windows = s.Windows(); dynamic ws = windows;
            for (var i = 0; i < ws.Count; i++)
            {
                object? window = null, document = null, folder = null, self = null, items = null;
                try
                {
                    window = ws.Item(i); dynamic w = window!;
                    document = w.Document; dynamic doc = document!;
                    folder = doc.Folder; dynamic f = folder; self = f.Self; dynamic entry = self;
                    if (!string.Equals((string)entry.Path, Path.GetDirectoryName(path), StringComparison.OrdinalIgnoreCase)) continue;
                    folderReady = true;
                    items = doc.SelectedItems(); dynamic selected = items;
                    for (var j = 0; j < selected.Count; j++)
                    {
                        object? item = null;
                        try { item = selected.Item(j); dynamic value = item; if (string.Equals((string)value.Path, path, StringComparison.OrdinalIgnoreCase)) return true; }
                        finally { Release(item); }
                    }
                }
                catch (Exception e) when (e is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or InvalidCastException) { }
                finally { Release(items); Release(self); Release(folder); Release(document); Release(window); }
            }
            return false;
        }
        catch (Exception e) when (e is COMException or Microsoft.CSharp.RuntimeBinder.RuntimeBinderException or InvalidCastException) { return false; }
        finally { Release(windows); Release(shell); }
    }
    private static void Release(object? value) { if (value is not null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
}
