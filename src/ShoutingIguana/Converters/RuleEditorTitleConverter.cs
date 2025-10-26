using System;
using System.Globalization;
using System.Windows.Data;

namespace ShoutingIguana.Converters;

/// <summary>
/// Converts EditingRuleId (0 = new, non-zero = edit) to dialog title.
/// </summary>
public class RuleEditorTitleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is int ruleId)
        {
            return ruleId == 0 ? "New Rule" : "Edit Rule";
        }
        
        return "Edit Rule";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}

