using System.Drawing;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace StartApp.App;

/// <summary>
/// Icon for the program a row opens with (icon + name display, settled Nov 14). The icon is extracted from the exe
/// once per path and cached (frozen, so the binding can read it on the UI thread). Any failure - missing exe,
/// unreadable icon - yields null, and the row falls back to the full-path text (the settled fallback).
/// </summary>
public static class ProgramIconCache
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, ImageSource?> Cache = new();

    public static ImageSource? ForPath(string? program)
    {
        if (string.IsNullOrWhiteSpace(program)) return null;
        var key = program.Trim();
        lock (Gate)
        {
            if (Cache.TryGetValue(key, out var cached)) return cached;
            var extracted = Extract(key);
            Cache[key] = extracted;
            return extracted;
        }
    }

    private static ImageSource? Extract(string path)
    {
        try
        {
            using var icon = Icon.ExtractAssociatedIcon(path);
            if (icon is null) return null;
            var bmp = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromWidthAndHeight(16, 16));
            bmp.Freeze();
            return bmp;
        }
        catch { return null; }
    }
}

/// <summary>Path -> cached exe icon (null when unreadable).</summary>
public sealed class ProgramIconConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) => ProgramIconCache.ForPath(value as string);
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Path -> whether an exe icon is readable (drives the icon panel vs full-path-text fallback).</summary>
public sealed class ProgramHasIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => ProgramIconCache.ForPath(value as string) is not null;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
