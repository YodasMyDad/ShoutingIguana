using System;
using System.Globalization;
using System.Windows.Data;
using ShoutingIguana.PluginSdk;

namespace ShoutingIguana.Converters;

/// <summary>
/// Converts a Severity enum value to its string representation.
/// </summary>
public class SeverityToStringConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null)
            return null;
        
        // If it's already a string, return it
        if (value is string str)
            return str;
        
        // If it's a Severity enum, convert to string
        if (value is Severity severity)
            return severity.ToString();
        
        // Try to parse as Severity if it's some other type
        try
        {
            if (Enum.TryParse(value.ToString(), out Severity parsed))
                return parsed.ToString();
        }
        catch
        {
            // Ignore parsing errors
        }
        
        return value?.ToString();
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null)
            return Severity.Info;
        
        if (value is Severity severity)
            return severity;
        
        if (value is string str && Enum.TryParse<Severity>(str, out var parsed))
            return parsed;
        
        return Severity.Info;
    }
}

