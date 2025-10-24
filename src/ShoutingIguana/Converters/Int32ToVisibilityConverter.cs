using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ShoutingIguana.Converters;

/// <summary>
/// Converts an integer value to Visibility.
/// Returns Visible if the value is greater than 0, otherwise Collapsed.
/// </summary>
public class Int32ToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int intValue)
        {
            return intValue > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        
        return Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

