using System;

namespace AgentBuddy.Models;

public sealed class FixedDepositAccount
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string AccountNo { get; set; } = string.Empty;
    public string CustomerName { get; set; } = string.Empty;
    public string? CustomerPhone { get; set; }
    public string SchemeType { get; set; } = "5-Year TD";
    public decimal DepositAmount { get; set; }
    public decimal InterestRate { get; set; } = 7.5m;
    public DateTime DepositDate { get; set; } = DateTime.Today;
    public DateTime MaturityDate { get; set; } = DateTime.Today.AddYears(5);
    public decimal MaturityAmount { get; set; }
    public decimal AnnualInterest { get; set; }
    public string Status { get; set; } = "Active";
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    public int DaysRemaining => Math.Max(0, (int)Math.Ceiling((MaturityDate.Date - DateTime.Today).TotalDays));

    public bool IsMatured => DateTime.Today >= MaturityDate.Date;
    public bool IsMaturingSoon => !IsMatured && DaysRemaining <= 30;

    public string StatusBadge => IsMatured
        ? "Matured"
        : (IsMaturingSoon ? "Maturing Soon" : "Active");

    public string StatusTag => IsMatured
        ? "Matured"
        : (IsMaturingSoon ? "Warning" : "Active");

    public string FormattedDepositAmount => $"Rs. {DepositAmount:N0}";
    public string FormattedMaturityAmount => $"Rs. {MaturityAmount:N0}";
    public string FormattedDepositDate => DepositDate.ToString("dd-MMM-yyyy");
    public string FormattedMaturityDate => MaturityDate.ToString("dd-MMM-yyyy");
    public string FormattedInterestRate => $"{InterestRate:F1}%";
}
