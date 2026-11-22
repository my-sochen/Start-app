using System.Collections;
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
            "Files" => "נפתחים בתוכנה המשויכת",
            _ => ""
        };
        return name switch { "Sites" => "אתרים", "Programs" => "תוכנות", "Folders" => "תיקיות", "Files" => "קבצים", _ => name ?? "" };
    }
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class DetailsGroupComparer : IComparer
{
    private static int Rank(object? value) => ((value as CollectionViewGroup)?.Name as string) switch
    {
        "Sites" => 0, "Programs" => 1, "Files" => 2, "Folders" => 3, _ => 4
    };
    public int Compare(object? x, object? y) => Rank(x).CompareTo(Rank(y));
}
