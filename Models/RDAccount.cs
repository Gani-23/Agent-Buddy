using System;
using System.Globalization;
using System.Linq;

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
    /// Estimates the opening month/date using the next due date and the number of installments paid.
    /// This is an approximation when the portal does not expose the exact opening date.
    /// </summary>
    public DateTime? GetEstimatedOpeningDate()
    {
        var dueDate = GetNextInstallmentDate();
        var paidInstallments = GetMonthPaidNumber();

        if (dueDate.HasValue && paidInstallments > 0)
        {
            var dueMonth = new DateTime(dueDate.Value.Year, dueDate.Value.Month, 1);
            return dueMonth.AddMonths(-paidInstallments);
        }

        if (FirstSeen != default)
        {
            return FirstSeen.Date;
        }

        return null;
    }

    public string EstimatedOpeningDateDisplay
    {
        get
        {
            var estimated = GetEstimatedOpeningDate();
            return estimated.HasValue
                ? $"{estimated.Value:dd-MMM-yyyy} (estimated)"
                : "-";
        }
    }

    public string EstimatedOpeningDateShortDisplay
    {
        get
        {
            var estimated = GetEstimatedOpeningDate();
            return estimated.HasValue ? estimated.Value.ToString("dd-MMM-yyyy") : "-";
        }
    }

    /// <summary>
    /// Suggest installments required to cover overdue months up to the given date.
    /// Returns at least 1.
    /// </summary>
    public int GetPendingInstallmentsTill(DateTime asOfDate, int maturityInstallments = 120)
    {
        var dueDate = GetNextInstallmentDate();
        if (!dueDate.HasValue)
        {
            return 1;
        }

        var dueMonth = new DateTime(dueDate.Value.Year, dueDate.Value.Month, 1);
        var currentMonth = new DateTime(asOfDate.Year, asOfDate.Month, 1);
        if (dueMonth >= currentMonth)
        {
            return 1;
        }

        // Pending suggestion includes current month when due month is older.
        // Example: due month = Jan, current month = Feb => suggest 2 installments.
        var monthGap = (currentMonth.Year - dueMonth.Year) * 12 + (currentMonth.Month - dueMonth.Month);
        var pending = monthGap + 1;
        pending = Math.Max(1, pending);

        if (maturityInstallments > 0)
        {
            var paid = Math.Max(0, GetMonthPaidNumber());
            var remaining = Math.Max(0, maturityInstallments - paid);
            if (remaining > 0)
            {
                pending = Math.Min(pending, remaining);
            }
        }

        return Math.Max(1, pending);
    }

    public PaymentAnalysis AnalyzePayment(int installments, DateTime? asOfDate = null, int longOverdueThresholdMonths = 2)
    {
        var effectiveInstallments = installments > 0 ? installments : 1;
        var referenceDate = (asOfDate ?? DateTime.Today).Date;
        var currentMonth = new DateTime(referenceDate.Year, referenceDate.Month, 1);
        var dueDate = GetNextInstallmentDate();

        if (!dueDate.HasValue)
        {
            return PaymentAnalysis.CreateMissingDueDate(effectiveInstallments, currentMonth);
        }

        var dueMonth = new DateTime(dueDate.Value.Year, dueDate.Value.Month, 1);
        var monthDelta = ((currentMonth.Year - dueMonth.Year) * 12) + (currentMonth.Month - dueMonth.Month);

        if (monthDelta < 0)
        {
            return new PaymentAnalysis(
                PaymentClassification.AdvancePayment,
                currentMonth,
                dueMonth,
                effectiveInstallments,
                0,
                0,
                effectiveInstallments,
                0);
        }

        var overdueMonths = Math.Max(0, monthDelta);
        var catchUpInstallments = overdueMonths + 1;
        var advanceInstallments = Math.Max(0, effectiveInstallments - catchUpInstallments);
        var remainingOverdueInstallments = Math.Max(0, catchUpInstallments - effectiveInstallments);
        var isLongOverdue = overdueMonths > longOverdueThresholdMonths;

        var classification = monthDelta == 0 && effectiveInstallments == 1
            ? PaymentClassification.CurrentMonth
            : remainingOverdueInstallments > 0
                ? isLongOverdue ? PaymentClassification.LongOverduePartialCatchUp : PaymentClassification.PartialCatchUp
                : isLongOverdue
                    ? PaymentClassification.LongOverdueResolved
                    : advanceInstallments > 0
                        ? overdueMonths > 0 ? PaymentClassification.MixedCatchUpAndAdvance : PaymentClassification.AdvancePayment
                        : PaymentClassification.CatchUpPayment;

        return new PaymentAnalysis(
            classification,
            currentMonth,
            dueMonth,
            effectiveInstallments,
            overdueMonths,
            catchUpInstallments,
            advanceInstallments,
            remainingOverdueInstallments);
    }

    /// <summary>
    /// Gets the short code (last 2 digits of account number, fallback to name)
    /// </summary>
    public string GetShortCode()
    {
        if (!string.IsNullOrWhiteSpace(AccountNo))
        {
            var digits = new string(AccountNo.Where(char.IsDigit).ToArray());
            if (digits.Length >= 2)
            {
                return digits.Substring(digits.Length - 2, 2);
            }

            if (digits.Length == 1)
            {
                return $"0{digits}";
            }
        }

        if (string.IsNullOrEmpty(AccountName) || AccountName.Length < 2)
        {
            return "??";
        }

        return AccountName.Substring(0, 2).ToUpper();
    }

    public string ShortCode => GetShortCode();
}
