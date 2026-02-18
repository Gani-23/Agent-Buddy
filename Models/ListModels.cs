using System.Collections.Generic;
using System.Linq;

namespace AgentBuddy.Models;

/// <summary>
/// Represents an account in a payment list with installment support
/// </summary>
public class ListItem
{
    public string AccountNo { get; set; } = string.Empty;
    public int Installment { get; set; } = 1; // Default to 1st installment
    public AccountValidationStatus Status { get; set; }
    public RDAccount? AccountDetails { get; set; }
    public int EffectiveInstallment => Installment > 0 ? Installment : 1;
    public decimal TotalAmount => (AccountDetails?.GetAmount() ?? 0) * EffectiveInstallment;
    public string AccountNameDisplay =>
        string.IsNullOrWhiteSpace(AccountDetails?.AccountName) ? "-" : AccountDetails.AccountName;
    public string DenominationDisplay =>
        string.IsNullOrWhiteSpace(AccountDetails?.Denomination) ? "-" : AccountDetails.Denomination;
    public string NextInstallmentDateDisplay =>
        string.IsNullOrWhiteSpace(AccountDetails?.NextInstallmentDate) ? "-" : AccountDetails.NextInstallmentDate;
    
    /// <summary>
    /// Gets the formatted string for Python script: AccountNo_Installment
    /// Example: 020001_2 for 2nd installment, or just 020001 for 1st installment
    /// </summary>
    public string GetFormattedString()
    {
        if (EffectiveInstallment > 1)
        {
            return $"{AccountNo}_{EffectiveInstallment}";
        }
        return AccountNo;
    }
}

/// <summary>
/// Represents a collection of accounts for payment processing
/// </summary>
public class AccountList
{
    public string Name { get; set; } = string.Empty;
    public List<ListItem> Items { get; set; } = new();
    public int MaxSize { get; set; } = 20000;
    
    public int Count => Items.Count;
    public bool IsFull => Items.Sum(i => i.TotalAmount) >= MaxSize;
    
    /// <summary>
    /// Converts list to format expected by Python script
    /// Format: [acc1, acc2_2, acc3] - NO QUOTES!
    /// Example output: [020001, 020002_2, 020003]
    /// </summary>
    public string ToScriptFormat()
    {
        var accountStrings = Items
            .Where(item => item.AccountDetails != null && !string.IsNullOrWhiteSpace(item.AccountNo))
            .Select(item => item.GetFormattedString())
            .ToList();
        return $"[{string.Join(", ", accountStrings)}]";
    }
}

/// <summary>
/// Print settings for report generation
/// </summary>
public class PrintSettings
{
    public string Format { get; set; } = "add_balance_column";
    public string Preview { get; set; } = "direct_print";
    public int Copies { get; set; } = 2;
}

/// <summary>
/// Result from Python script execution
/// </summary>
public class ProcessingResult
{
    public bool Success { get; set; }
    public List<string> ReferenceNumbers { get; set; } = new();
    public List<string> FailedLists { get; set; } = new();
    public string ErrorMessage { get; set; } = string.Empty;
}

public class AslaasUpdateItem
{
    public string AccountNo { get; set; } = string.Empty;
    public string AslaasNo { get; set; } = string.Empty;
}

public class DopChequeInputItem
{
    public int ListIndex { get; set; }
    public string AccountNo { get; set; } = string.Empty;
    public string ChequeNo { get; set; } = string.Empty;
    public string PaymentAccountNo { get; set; } = string.Empty;
}
