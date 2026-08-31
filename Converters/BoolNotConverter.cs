using System;
using System.Globalization;
using Avalonia.Data.Converters;

namespace AgentBuddy.Converters;

public class BoolNotConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is bool flag && !flag;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
