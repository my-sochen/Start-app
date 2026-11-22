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
/// (settled Nov 11; exact measurements were never fixed, so these are provisional); without an image (or with an image whose file is missing) there is no frame - the color is the background.</summary>
public sealed class FrameThicknessConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string s && s.Length > 0 && System.IO.File.Exists(s) ? new Thickness(1, 8, 1, 1) : new Thickness(0);
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

/// <summary>Null → Collapsed, anything else → Visible (chip swatches hide when the chip has no color).</summary>
public sealed class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>גודל כרטיסים → title font size: קטן 11, בינוני 13 (default), גדול 16.</summary>
public sealed class CardSizeFontConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is StartApp.Presentation.CardSizeOption s ? s switch
        { StartApp.Presentation.CardSizeOption.Small => 11.0, StartApp.Presentation.CardSizeOption.Large => 16.0, _ => 13.0 } : 13.0;
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>גודל כרטיסים → card padding: קטן 2, בינוני 4 (default), גדול 8.</summary>
public sealed class CardSizePaddingConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is StartApp.Presentation.CardSizeOption s
            ? new Thickness(s switch { StartApp.Presentation.CardSizeOption.Small => 2.0, StartApp.Presentation.CardSizeOption.Large => 8.0, _ => 4.0 })
            : new Thickness(4);
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
