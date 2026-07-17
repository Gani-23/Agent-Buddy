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

        if (item.PaymentAnalysis.Classification == PaymentClassification.MissingDueDate)
        {
            return "MissingDueDate";
        }

        if (item.PaymentAnalysis.Classification == PaymentClassification.PartialCatchUp ||
            item.PaymentAnalysis.Classification == PaymentClassification.LongOverduePartialCatchUp)
        {
            return "PartialCatchUpPayment";
        }

        if (item.PaymentAnalysis.DueMonth.HasValue &&
            item.PaymentAnalysis.DueMonth.Value < item.PaymentAnalysis.CurrentMonth)
        {
            return "CatchUpPayment";
        }

        if (item.PaymentAnalysis.DueMonth.HasValue &&
            item.PaymentAnalysis.DueMonth.Value > item.PaymentAnalysis.CurrentMonth)
        {
            return "AdvancePayment";
        }

        return item.PaymentAnalysis.Classification switch
        {
            _ => "Valid"
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
