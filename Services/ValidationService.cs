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
        // Check if account exists in database
        var account = await _databaseService.GetAccountByNumberAsync(accountNo);
        
        if (account == null)
        {
            return (AccountValidationStatus.Invalid, null);
        }

        // Check for duplicates across lists
        if (existingAccountsInLists.Contains(accountNo))
        {
            return (AccountValidationStatus.Duplicate, account);
        }

        // Check if due soon (within 30 days)
        if (account.IsDueWithinDays(30))
        {
            return (AccountValidationStatus.DueSoon, account);
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

            // Add to existing list for duplicate checking
            if (account != null)
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
            AccountValidationStatus.DueSoon => "#FFC853",    // Yellow
            AccountValidationStatus.Invalid => "#E03E3E",    // Red
            AccountValidationStatus.Duplicate => "#E91E63",  // Pink
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
            AccountValidationStatus.DueSoon => "Payment due within 30 days",
            AccountValidationStatus.Invalid => "Account not found in database",
            AccountValidationStatus.Duplicate => "Duplicate account in lists",
            _ => string.Empty
        };
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
