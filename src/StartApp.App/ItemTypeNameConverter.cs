using System;
using System.Globalization;
using System.Windows.Data;
using StartApp.Core;
using StartApp.Presentation;

namespace StartApp.App;

/// <summary>Uses the existing Hebrew type names without changing enum command parameters.</summary>
public sealed class ItemTypeNameConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is ItemType type ? MainViewModel.TypeName(type) : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
