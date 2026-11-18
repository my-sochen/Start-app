using System.Globalization;
using System.Windows;
using System.Windows.Data;
namespace StartApp.App;
public sealed class ItemEditorVisibilityConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) =>
        values.Length == 2 && values[0] is string row && row.Length > 0 && values[1] is string active && row == active
            ? Visibility.Visible : Visibility.Collapsed;
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
