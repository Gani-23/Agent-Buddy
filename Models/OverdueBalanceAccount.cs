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
}
