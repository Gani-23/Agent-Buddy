using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace AgentBuddy.Converters;

public class SidebarWidthConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isExpanded)
        {
            return isExpanded ? 240d : 64d;
        }

        return 240d;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
