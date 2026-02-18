using System;
using System.Globalization;
using Avalonia.Data.Converters;
using AgentBuddy.Models;

namespace AgentBuddy.Converters;

public class StatusToTagConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is AccountValidationStatus status)
        {
            return status switch
            {
                AccountValidationStatus.DueSoon => "DueSoon",
                AccountValidationStatus.Invalid => "Invalid",
                AccountValidationStatus.Duplicate => "Duplicate",
                _ => "Valid"
            };
        }
        return "Valid";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
