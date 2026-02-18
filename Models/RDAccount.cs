using System;
using System.Globalization;

namespace AgentBuddy.Models;

/// <summary>
/// Represents a Recurring Deposit account from the DOP Agent portal
/// </summary>
public class RDAccount
{
    public int Id { get; set; }
    public string AccountNo { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string AslaasNo { get; set; } = string.Empty;
    public string Denomination { get; set; } = string.Empty;
    public string MonthPaidUpto { get; set; } = string.Empty;
    public string NextInstallmentDate { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public int MonthPaidUptoNumber { get; set; }
    public string NextDueDateIso { get; set; } = string.Empty;
    public decimal TotalDeposit { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime FirstSeen { get; set; }
    public DateTime LastUpdated { get; set; }
    public bool IsActive { get; set; }

    /// <summary>
    /// Gets the numeric amount from denomination string (e.g., "2,000.00 Cr." -> 2000)
    /// </summary>
    public decimal GetAmount()
    {
        if (Amount > 0)
            return Amount;

        if (string.IsNullOrEmpty(Denomination))
            return 0;

        // Remove "Cr." and parse
        var cleanAmount = Denomination.Replace(" Cr.", "").Replace(",", "").Trim();
        return decimal.TryParse(cleanAmount, out var amount) ? amount : 0;
    }

    /// <summary>
    /// Calculates portal rebate for advance installment payment.
    /// Rule used by report output:
    /// - 6+ installments: Rs.10 per Rs.100 denomination
    /// - 12+ installments: Rs.40 per Rs.100 denomination
    /// </summary>
    public decimal GetAdvanceRebate(int installments)
    {
        var effectiveInstallments = installments > 0 ? installments : 1;
        var amount = GetAmount();
        if (amount <= 0)
        {
            return 0;
        }

        var hundreds = Math.Floor(amount / 100m);
        if (effectiveInstallments >= 12)
        {
            return hundreds * 40m;
        }

        if (effectiveInstallments >= 6)
        {
            return hundreds * 10m;
        }

        return 0;
    }

    public decimal GetPayableAmount(int installments)
    {
        var effectiveInstallments = installments > 0 ? installments : 1;
        var gross = GetAmount() * effectiveInstallments;
        var rebate = GetAdvanceRebate(effectiveInstallments);
        var payable = gross - rebate;
        return payable >= 0 ? payable : 0;
    }

    /// <summary>
    /// Gets the next installment date as DateTime
    /// </summary>
    public DateTime? GetNextInstallmentDate()
    {
        if (!string.IsNullOrWhiteSpace(NextDueDateIso) &&
            DateTime.TryParseExact(NextDueDateIso,
                new[] { "yyyy-MM-dd", "yyyy-M-d" },
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var isoDate))
        {
            return isoDate;
        }

        // Try parsing date in format "13-Jan-2026"
        if (DateTime.TryParseExact(NextInstallmentDate,
            new[] { "dd-MMM-yyyy", "d-MMM-yyyy" }, 
            CultureInfo.InvariantCulture,
            DateTimeStyles.None, 
            out var date))
        {
            return date;
        }

        if (DateTime.TryParse(NextInstallmentDate, out var parsed))
        {
            return parsed;
        }

        return null;
    }

    /// <summary>
    /// Checks if account is due within specified days
    /// </summary>
    public bool IsDueWithinDays(int days)
    {
        var dueDate = GetNextInstallmentDate();
        if (!dueDate.HasValue)
            return false;

        var daysUntilDue = (dueDate.Value.Date - DateTime.Today).Days;
        return daysUntilDue >= 0 && daysUntilDue <= days;
    }

    public int GetMonthPaidNumber()
    {
        if (MonthPaidUptoNumber > 0)
            return MonthPaidUptoNumber;

        return int.TryParse(MonthPaidUpto, out var value) ? value : 0;
    }

    /// <summary>
    /// Gets the short code (first 2 chars of account name)
    /// </summary>
    public string GetShortCode()
    {
        if (string.IsNullOrEmpty(AccountName) || AccountName.Length < 2)
            return "??";
        
        return AccountName.Substring(0, 2).ToUpper();
    }

    public string ShortCode => GetShortCode();
}
