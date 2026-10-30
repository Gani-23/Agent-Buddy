using System;

namespace AgentBuddy.Models;

public sealed class OverdueBalanceAccount
{
    public required RDAccount Account { get; init; }
    public int BalanceMonths { get; init; }

    public string AccountNo => Account.AccountNo;
    public string AccountName => Account.AccountName;
    public string AslaasNo => Account.AslaasNo;
    public string Denomination => Account.Denomination;
    public string MonthPaidUpto => Account.MonthPaidUpto;
    public string NextInstallmentDate => Account.NextInstallmentDate;
    public decimal Amount => Account.GetAmount();
    public decimal BalanceAmount => Amount * Math.Max(1, BalanceMonths);
    public string BalanceLabel => BalanceMonths == 1 ? "1 month" : $"{BalanceMonths} months";
    public string AmountDisplay => $"Rs. {Amount:N0}";
    public string BalanceAmountDisplay => $"Rs. {BalanceAmount:N0}";
    public string AslaasDisplay => string.IsNullOrWhiteSpace(AslaasNo) ? "-" : AslaasNo;

    /// <summary>
    /// Compact shortcut notation for overdue duration:
    /// Months as 'M' (e.g. 1M, 6M) and Years as 'Y' (e.g. 1Y, 1Y 2M, 2Y).
    /// </summary>
    public string DurationShortDisplay
    {
        get
        {
            if (BalanceMonths <= 0)
                return "0M";
            if (BalanceMonths < 12)
                return $"{BalanceMonths}M";

            int years = BalanceMonths / 12;
            int remMonths = BalanceMonths % 12;
            return remMonths == 0 ? $"{years}Y" : $"{years}Y {remMonths}M";
        }
    }

    /// <summary>
    /// Detailed tooltip for duration explanation.
    /// </summary>
    public string DurationTooltip
    {
        get
        {
            if (BalanceMonths <= 0)
                return "Current / No overdue";
            if (BalanceMonths < 12)
                return BalanceMonths == 1 ? "1 month overdue" : $"{BalanceMonths} months overdue";

            int years = BalanceMonths / 12;
            int remMonths = BalanceMonths % 12;
            string yearText = years == 1 ? "1 year" : $"{years} years";
            string monthText = remMonths == 1 ? "1 month" : $"{remMonths} months";

            return remMonths == 0
                ? $"{BalanceMonths} months overdue ({yearText})"
                : $"{BalanceMonths} months overdue ({yearText}, {monthText})";
        }
    }
}
