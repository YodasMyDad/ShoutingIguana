using System;
using System.Globalization;
using System.IO;
using System.Windows.Data;

namespace ShoutingIguana.Converters;

/// <summary>
/// Returns a friendly display name for a recent project entry.
/// Falls back to the file name when the stored project name is empty.
/// </summary>
public class RecentProjectNameConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var name = values.Length > 0 ? values[0] as string : string.Empty;
        var path = values.Length > 1 ? values[1] as string : string.Empty;

        if (!string.IsNullOrWhiteSpace(name))
        {
            return name.Trim();
        }

        if (!string.IsNullOrWhiteSpace(path))
        {
            try
            {
                var fileName = Path.GetFileNameWithoutExtension(path);
                if (!string.IsNullOrWhiteSpace(fileName))
                {
                    return fileName;
                }
            }
            catch
            {
                // Fall through to generic label if path parsing fails.
            }
        }

        return "Untitled Project";
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
