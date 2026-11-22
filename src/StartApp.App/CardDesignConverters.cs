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

/// <summary>Card top bar brush (approved mocks 22.11): the workspace color, or the neutral #A7AEB9 when unset.</summary>
public sealed class TopBarBrushConverter : IValueConverter
{
    private static readonly SolidColorBrush Fallback = Frozen("#A7AEB9");
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        ColorBrushConverter.BrushFrom(value as string) ?? Fallback;
    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
    internal static SolidColorBrush Frozen(string hex)
    { var b = new SolidColorBrush((Color)System.Windows.Media.ColorConverter.ConvertFromString(hex)); b.Freeze(); return b; }
}

/// <summary>Card surface (approved mocks): the uploaded image when readable, otherwise plain white.</summary>
public sealed class CardSurfaceConverter : IMultiValueConverter
{
    private static readonly SolidColorBrush White = TopBarBrushConverter.Frozen("#FFFFFF");
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var image = values.Length > 1 ? values[1] as string : null;
        if (!string.IsNullOrEmpty(image))
        {
            try { var b = new ImageBrush(new BitmapImage(new Uri(image, UriKind.Absolute))) { Stretch = Stretch.UniformToFill }; b.Freeze(); return b; }
            catch (Exception e) when (e is UriFormatException or System.IO.IOException or NotSupportedException or ArgumentException) { }
        }
        return White;
    }
    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Card frame: with an image the frame is the workspace color (Nov 11 design); without one the mock's neutral border.</summary>
public sealed class CardFrameBrushConverter : IMultiValueConverter
{
    private static readonly SolidColorBrush Neutral = TopBarBrushConverter.Frozen("#C9D0DC");
    public object? Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        var image = values.Length > 1 ? values[1] as string : null;
        if (!string.IsNullOrEmpty(image) && System.IO.File.Exists(image))
            return ColorBrushConverter.BrushFrom(values.Length > 0 ? values[0] as string : null) ?? Neutral;
        return Neutral;
    }
    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Card frame thickness: the Nov 11 thick-top frame only with a readable image; a uniform 1px hairline otherwise.</summary>
public sealed class CardFrameThicknessConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string s && s.Length > 0 && System.IO.File.Exists(s) ? new Thickness(1, 8, 1, 1) : new Thickness(1);
    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Visible when the card has no readable image (the colored top bar shows only then).</summary>
public sealed class NoImageToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string s && s.Length > 0 && System.IO.File.Exists(s) ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>First letter of a name for the סביבה גלובלית row tile (G/W/ת/ל...); Latin letters uppercased.</summary>
public sealed class FirstLetterConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string s && s.Length > 0 ? char.ToUpperInvariant(s[0]).ToString() : "";
    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Card stats line via the window view-model (needs the item store for types): values[0] Workspace, values[1] MainViewModel.</summary>
public sealed class StatsTextConverter : IMultiValueConverter
{
    public object? Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Length > 1 && values[0] is StartApp.Core.Workspace w && values[1] is StartApp.Presentation.MainViewModel vm ? vm.StatsText(w) : "";
    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Card footer first line via the window view-model (relative Hebrew date): values[0] Workspace, values[1] MainViewModel.</summary>
public sealed class FooterLine1Converter : IMultiValueConverter
{
    public object? Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Length > 1 && values[0] is StartApp.Core.Workspace w && values[1] is StartApp.Presentation.MainViewModel vm ? vm.FooterLine1(w) : "";
    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>List-row last-opened text (approved 22.11 list mock): same as FooterLine1 but an empty workspace shows
/// plain "סביבה ריקה" (the card's " · הוסף אייטם בפרטים" suffix is card-specific). values[0] Workspace, values[1] MainViewModel.</summary>
public sealed class FooterLine1ListConverter : IMultiValueConverter
{
    public object? Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Length > 1 && values[0] is StartApp.Core.Workspace w && values[1] is StartApp.Presentation.MainViewModel vm ? vm.FooterLine1List(w) : "";
    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>סביבת ברירת מחדל green-dot badge on the card title (approved 22.11 handoff): values[0] Workspace,
/// values[1] MainViewModel → Visible only on the one default workspace's card, Collapsed everywhere else.</summary>
public sealed class DefaultBadgeVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Length > 1 && values[0] is StartApp.Core.Workspace w && values[1] is StartApp.Presentation.MainViewModel vm && vm.IsDefaultWorkspace(w) ? Visibility.Visible : Visibility.Collapsed;
    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Card context-menu default row (path 2, approved 22.11): values[0] Workspace, values[1] MainViewModel →
/// "הסר הגדרה כברירת מחדל" on the current default's card, "הגדר כברירת מחדל" on every other card (owner wording 22.11).</summary>
public sealed class DefaultMenuHeaderConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Length > 1 && values[0] is StartApp.Core.Workspace w && values[1] is StartApp.Presentation.MainViewModel vm && vm.IsDefaultWorkspace(w)
            ? "הסר הגדרה כברירת מחדל" : "הגדר כברירת מחדל";
    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>Search-box placeholder overlay (approved 22.11 home toolbar): Visible while the bound text is
/// null/empty, Collapsed as soon as the user types.</summary>
public sealed class StringEmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        string.IsNullOrEmpty(value as string) ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>List-row deploy button (approved 22.11 list mock): Visible only when the workspace has items to
/// deploy; an empty workspace shows the plain "סביבה ריקה" text in place of the button.</summary>
public sealed class HasItemsToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is StartApp.Core.Workspace w && w.Items.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>false → Visible, true → Collapsed (the chips bar swaps chip row and single-filter short form).</summary>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
