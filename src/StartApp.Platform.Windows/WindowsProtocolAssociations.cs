using System.Runtime.InteropServices;
using System.Text;

namespace StartApp.Platform.Windows;

/// <summary>Shared, read-only protocol association lookup for the picker and dedicated browser launch.
/// Never parses a command string or changes Windows associations.</summary>
public static class WindowsProtocolAssociations
{
    public static string Query(uint flags, uint kind, string association)
    {
        uint length = 0;
        AssocQueryString(flags, kind, association, null, null, ref length);
        if (length is 0 or > 32768) return "";
        var text = new StringBuilder((int)length);
        return AssocQueryString(flags, kind, association, null, text, ref length) == 0 ? text.ToString().Trim() : "";
    }

    public static string ExecutableFor(string scheme) => Query(0x1000 /* IS_PROTOCOL */, 2 /* EXECUTABLE */, scheme);

    [DllImport("Shlwapi.dll", EntryPoint = "AssocQueryStringW", CharSet = CharSet.Unicode)]
    private static extern int AssocQueryString(uint flags, uint kind, string association, string? extra, StringBuilder? output, ref uint characters);
}
