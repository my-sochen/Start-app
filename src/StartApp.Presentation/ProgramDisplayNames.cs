using System.Diagnostics;
using StartApp.Core;

namespace StartApp.Presentation;

/// <summary>
/// Resolves the short display name for the program a row opens with (icon + name display, settled Nov 14: אייקון ושם
/// הדפדפן במקום הנתיב הארוך). Browser names come from the single existing source, Core CaptureRules.BrowserDisplayName
/// (no duplicate map here); non-browser programs use their version-info description (product name as a second choice);
/// anything unreadable falls back to the exe file name. The full path itself is untouched: it stays in the tooltip and
/// in the open-with popup, and a missing/unreadable exe falls back to the full-path text in the row (settled fallback).
/// </summary>
public static class ProgramDisplayNames
{
    /// <summary>Version-info lookup, replaceable in tests. Returns null when nothing readable.</summary>
    public static Func<string, string?> VersionInfoName { get; set; } = DefaultVersionInfoName;

    /// <summary>Short display name for a program path; "" for an empty path.</summary>
    public static string Resolve(string? program)
    {
        if (string.IsNullOrWhiteSpace(program)) return "";
        var trimmed = program.Trim();
        // split on both separators explicitly: the app runs on Windows paths, and tests may run on any OS
        var cut = trimmed.LastIndexOfAny(new[] { '\\', '/' });
        var file = cut >= 0 ? trimmed[(cut + 1)..] : trimmed;
        if (file.Length == 0) return "";
        var stem = file.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? file[..^4] : file;
        // a known browser: Core's single-source map (CaptureRules.KnownBrowserName, shared with capture-row naming)
        if (CaptureRules.KnownBrowserName(trimmed) is { Length: > 0 } known) return known;
        try
        {
            var fromInfo = VersionInfoName(trimmed);
            if (!string.IsNullOrWhiteSpace(fromInfo)) return fromInfo.Trim();
        }
        catch { /* unreadable version info falls through to the file name */ }
        return stem;
    }

    private static string? DefaultVersionInfoName(string path)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(path);
            return string.IsNullOrWhiteSpace(info.FileDescription) ? info.ProductName : info.FileDescription;
        }
        catch { return null; }
    }
}
