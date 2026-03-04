using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AgentBuddy.Models;

namespace AgentBuddy.Services;

/// <summary>
/// Service for validating accounts in lists
/// </summary>
public class ValidationService
{
    private const int FullyMaturedInstallmentThreshold = 120;
    private readonly DatabaseService _databaseService;

    public ValidationService(DatabaseService databaseService)
    {
        _databaseService = databaseService;
    }

    /// <summary>
    /// Validate a single account number
    /// </summary>
    public async Task<(AccountValidationStatus status, RDAccount? account)> ValidateAccountAsync(
        string accountNo, 
        List<string> existingAccountsInLists)
    {
        var normalizedAccountNo = (accountNo ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(normalizedAccountNo))
        {
            return (AccountValidationStatus.Invalid, null);
        }

        // Check if account exists in active account table first.
        var account = await _databaseService.GetAccountByNumberAsync(normalizedAccountNo);
        if (account == null)
        {
            var archived = await _databaseService.GetClosedAccountByNumberAsync(normalizedAccountNo);
            if (archived != null)
            {
                return (IsMaturedCompletely(archived)
                    ? AccountValidationStatus.Matured
                    : AccountValidationStatus.Closed, archived);
            }

            return (AccountValidationStatus.Invalid, null);
        }

        if (IsMaturedCompletely(account))
        {
            return (AccountValidationStatus.Matured, account);
        }

        if (!account.IsActive || IsClosedStatus(account.Status))
        {
            return (AccountValidationStatus.Closed, account);
        }

        // Check for duplicates across lists.
        var isDuplicate = existingAccountsInLists.Any(existing =>
            string.Equals(existing?.Trim(), normalizedAccountNo, StringComparison.OrdinalIgnoreCase));
        if (isDuplicate)
        {
            return (AccountValidationStatus.Duplicate, account);
        }

        return (AccountValidationStatus.Valid, account);
    }

    /// <summary>
    /// Validate multiple account numbers
    /// </summary>
    public async Task<List<ListItem>> ValidateAccountsAsync(
        List<string> accountNumbers,
        List<string> existingAccountsInLists)
    {
        var items = new List<ListItem>();

        foreach (var accountNo in accountNumbers)
        {
            var (status, account) = await ValidateAccountAsync(accountNo, existingAccountsInLists);
            
            items.Add(new ListItem
            {
                AccountNo = accountNo,
                Status = status,
                AccountDetails = account
            });

            // Add only processable accounts for duplicate checking.
            if (account != null &&
                (status == AccountValidationStatus.Valid || status == AccountValidationStatus.DueSoon))
            {
                existingAccountsInLists.Add(accountNo);
            }
        }

        return items;
    }

    /// <summary>
    /// Get color for validation status
    /// </summary>
    public string GetStatusColor(AccountValidationStatus status)
    {
        return status switch
        {
            AccountValidationStatus.Invalid => "#E03E3E",    // Red
            AccountValidationStatus.Duplicate => "#E91E63",  // Pink
            AccountValidationStatus.Closed => "#B22222",     // Firebrick
            AccountValidationStatus.Matured => "#A94442",    // Matured/closed tone
            _ => "Transparent"                                // Valid - no highlight
        };
    }

    /// <summary>
    /// Get status description
    /// </summary>
    public string GetStatusDescription(AccountValidationStatus status)
    {
        return status switch
        {
            AccountValidationStatus.Valid => "Valid account",
            AccountValidationStatus.Invalid => "Account not found in database",
            AccountValidationStatus.Duplicate => "Duplicate account in lists",
            AccountValidationStatus.Closed => "Account is already closed",
            AccountValidationStatus.Matured => "Account is already matured completely",
            _ => string.Empty
        };
    }

    private static bool IsClosedStatus(string status)
    {
        var normalized = (status ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        return normalized.Contains("closed", StringComparison.Ordinal) ||
               normalized.Contains("inactive", StringComparison.Ordinal) ||
               normalized.Contains("deactivated", StringComparison.Ordinal);
    }

    private static bool IsMaturedStatus(string status)
    {
        var normalized = (status ?? string.Empty).Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return false;
        }

        return normalized.Contains("matured", StringComparison.Ordinal) ||
               normalized.Equals("mature", StringComparison.Ordinal) ||
               normalized.Contains("maturity", StringComparison.Ordinal);
    }

    private static bool IsMaturedCompletely(RDAccount account)
    {
        if (account == null)
        {
            return false;
        }

        return account.GetMonthPaidNumber() >= FullyMaturedInstallmentThreshold ||
               IsMaturedStatus(account.Status);
    }

    /// <summary>
    /// Filter accounts by various criteria
    /// </summary>
    public async Task<List<RDAccount>> FilterAccountsAsync(
        string? searchQuery = null,
        bool? isDueSoon = null,
        decimal? minAmount = null,
        decimal? maxAmount = null,
        int maxResults = 100)
    {
        var accounts = await _databaseService.GetAllActiveAccountsAsync();

        // Apply search filter
        if (!string.IsNullOrWhiteSpace(searchQuery))
        {
            var query = searchQuery.ToLower();
            accounts = accounts.Where(a =>
                a.AccountNo.ToLower().Contains(query) ||
                a.AccountName.ToLower().Contains(query)
            ).ToList();
        }

        // Apply due soon filter
        if (isDueSoon.HasValue && isDueSoon.Value)
        {
            accounts = accounts.Where(a => a.IsDueWithinDays(30)).ToList();
        }

        // Apply amount filters
        if (minAmount.HasValue)
        {
            accounts = accounts.Where(a => a.GetAmount() >= minAmount.Value).ToList();
        }

        if (maxAmount.HasValue)
        {
            accounts = accounts.Where(a => a.GetAmount() <= maxAmount.Value).ToList();
        }

        // Limit results
        return accounts.Take(maxResults).ToList();
    }
}
