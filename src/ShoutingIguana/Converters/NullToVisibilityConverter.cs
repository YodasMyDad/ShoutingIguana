using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ShoutingIguana.Converters;

public class NullToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        // If value is not null, show the control (Visible)
        // If value is null, hide the control (Collapsed)
        return value != null ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

