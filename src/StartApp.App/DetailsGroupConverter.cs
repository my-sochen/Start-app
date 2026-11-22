using System.Globalization;
using System.Windows.Data;

namespace StartApp.App;

public sealed class DetailsGroupConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var name = value as string;
        if (parameter as string == "hint") return name switch
        {
            "Sites" => "לחיצה על שם האתר פותחת אותו",
            "Programs" => "פתיחה באמצעות התוכנה עצמה",
            _ => ""
        };
        return name switch { "Sites" => "אתרים", "Programs" => "תוכנות", "Folders" => "תיקיות", "Files" => "קבצים", _ => name ?? "" };
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
