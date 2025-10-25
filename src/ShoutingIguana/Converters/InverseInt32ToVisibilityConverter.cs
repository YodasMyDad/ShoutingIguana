using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ShoutingIguana.Converters;

/// <summary>
/// Converts an integer value to Visibility (inverse logic).
/// Returns Visible if the value is 0 or less, otherwise Collapsed.
/// Useful for showing empty states when a collection has no items.
/// </summary>
public class InverseInt32ToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int intValue)
        {
            return intValue <= 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        
        return Visibility.Visible; // Show empty state by default if value is null
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

