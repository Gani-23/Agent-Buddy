using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace AgentBuddy.Converters;

public class BoolToThemeIconConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is bool isDark)
        {
            return isDark ? "🌙" : "☀️";
        }
        return "☀️";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}