using System;
using System.Globalization;
using Avalonia.Data.Converters;
using AgentBuddy.Models;

namespace AgentBuddy.Converters;

public class ListItemToTagConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not ListItem item)
        {
            return "Valid";
        }

        if (item.IsProcessedInCurrentRun)
        {
            return "Processed";
        }

        if (item.Status == AccountValidationStatus.Invalid ||
            item.Status == AccountValidationStatus.Closed ||
            item.Status == AccountValidationStatus.Matured)
        {
            return "Invalid";
        }

        if (item.Status == AccountValidationStatus.Duplicate)
        {
            return "Duplicate";
        }

        if (item.EffectiveInstallment <= 1)
        {
            return "Valid";
        }

        var rebate = item.AccountDetails?.GetAdvanceRebate(item.EffectiveInstallment) ?? 0m;
        return rebate > 0 ? "PayingAdvance" : "AdditionalInstallment";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
