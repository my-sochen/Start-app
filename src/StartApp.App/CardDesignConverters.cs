using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace StartApp.App;

/// <summary>#RRGGBB string to a frozen SolidColorBrush; null/empty/invalid yields null (no paint).</summary>
public sealed class ColorBrushConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object? parameter, CultureInfo culture) => BrushFrom(value as string);
    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
    internal static SolidColorBrush? BrushFrom(string? hex)
    {
        if (hex is null || hex.Length != 7 || hex[0] != '#') return null;
        try { var b = new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString(hex)); b.Freeze(); return b; }
        catch (FormatException) { return null; }
    }
}

/// <summary>The card frame: with a background image the color is a frame with a thick top and thin other sides
/// (settled Nov 11; exact measurements were never fixed, so these are provisional); without an image there is no frame.</summary>
public sealed class FrameThicknessConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string s && s.Length > 0 ? new Thickness(1, 8, 1, 1) : new Thickness(0);
    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Card/preview background: the uploaded image when a path exists (UniformToFill), otherwise the chosen color,
/// otherwise nothing. An unreadable image path falls back to the color instead of crashing the card.</summary>
public sealed class CardBrushConverter : IMultiValueConverter
{
    public object? Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var color = values.Length > 0 ? values[0] as string : null;
        var image = values.Length > 1 ? values[1] as string : null;
        if (!string.IsNullOrEmpty(image))
        {
            try { var b = new ImageBrush(new BitmapImage(new Uri(image, UriKind.Absolute))) { Stretch = Stretch.UniformToFill }; b.Freeze(); return b; }
            catch (Exception e) when (e is UriFormatException or System.IO.IOException or NotSupportedException or ArgumentException) { /* fall through to the color */ }
        }
        return ColorBrushConverter.BrushFrom(color);
    }
    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
