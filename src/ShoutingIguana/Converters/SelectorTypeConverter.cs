using System;
using System.Globalization;
using System.Windows.Data;

namespace ShoutingIguana.Converters;

/// <summary>
/// Converts SelectorType integer (0=CSS, 1=XPath, 2=Regex) to display string.
/// </summary>
public class SelectorTypeConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is int selectorType)
        {
            return selectorType switch
            {
                0 => "CSS",
                1 => "XPath",
                2 => "Regex",
                _ => "Unknown"
            };
        }
        
        return "Unknown";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

