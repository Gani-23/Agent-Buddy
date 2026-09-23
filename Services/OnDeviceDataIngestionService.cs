using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using AgentBuddy.Models;

namespace AgentBuddy.Services;

/// <summary>
/// Raw extracted account record model from portal DOM / TSV.
/// </summary>
public sealed class PortalRawAccountRecord
{
    public string AccountNo { get; set; } = string.Empty;
    public string AccountName { get; set; } = string.Empty;
    public string Denomination { get; set; } = string.Empty;
    public string MonthPaidUpto { get; set; } = string.Empty;
    public string NextInstallmentDate { get; set; } = string.Empty;
    public string AslaasNo { get; set; } = string.Empty;
}

/// <summary>
/// Core database ingestion engine replicating Fetch_RDAccounts.py data normalization and archival.
/// </summary>
public sealed class OnDeviceDataIngestionService
{
    private static readonly string[] DateFormats =
    {
        "dd-MMM-yyyy",
        "d-MMM-yyyy",
        "dd-MMMM-yyyy",
        "d-MMMM-yyyy",
        "yyyy-MM-dd",
        "dd/MM/yyyy",
        "d/M/yyyy"
    };

    private readonly DatabaseService _databaseService;

    public OnDeviceDataIngestionService(DatabaseService databaseService)
    {
        _databaseService = databaseService;
    }

    /// <summary>
    /// Ingests raw portal records into the local SQLite database.
    /// </summary>
    public async Task<(int newCount, int updatedCount, int removedCount)> IngestPortalAccountsAsync(
        IReadOnlyCollection<PortalRawAccountRecord> rawRecords)
    {
        if (rawRecords == null || rawRecords.Count == 0)
        {
            return (0, 0, 0);
        }

        var normalizedList = new List<RDAccount>();
        var validFetchedAccounts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var raw in rawRecords)
        {
            var acctNo = (raw.AccountNo ?? string.Empty).Trim();
            if (string.IsNullOrWhiteSpace(acctNo) || acctNo.Length < 10)
            {
                continue;
            }

            var amount = ParseDenomination(raw.Denomination);
            var monthPaidNum = ParseMonthPaidNumber(raw.MonthPaidUpto);
            var nextDueIso = ParseDueDateIso(raw.NextInstallmentDate);
            var totalDeposit = amount > 0 && monthPaidNum > 0 ? amount * monthPaidNum : 0;

            validFetchedAccounts.Add(acctNo);

            normalizedList.Add(new RDAccount
            {
                AccountNo = acctNo,
                AccountName = (raw.AccountName ?? string.Empty).Trim(),
                Denomination = (raw.Denomination ?? string.Empty).Trim(),
                MonthPaidUpto = (raw.MonthPaidUpto ?? string.Empty).Trim(),
                MonthPaidUptoNumber = monthPaidNum,
                NextInstallmentDate = (raw.NextInstallmentDate ?? string.Empty).Trim(),
                NextDueDateIso = nextDueIso ?? string.Empty,
                Amount = amount,
                TotalDeposit = totalDeposit,
                Status = "activate",
                IsActive = true,
                AslaasNo = (raw.AslaasNo ?? string.Empty).Trim(),
                LastUpdated = DateTime.Now
            });
        }

        // Upsert all fetched active accounts into rd_accounts and account_detail
        var (newCount, updatedCount) = await _databaseService.SaveAccountsBatchAsync(normalizedList);

        // Detect accounts missing from this fetch (matured/closed) and archive to closed_accounts
        var existingActiveAccounts = await _databaseService.GetAllActiveAccountsAsync();
        var missingAccounts = existingActiveAccounts
            .Where(a => !string.IsNullOrWhiteSpace(a.AccountNo) && !validFetchedAccounts.Contains(a.AccountNo.Trim()))
            .ToList();

        var removedCount = 0;
        if (missingAccounts.Count > 0)
        {
            await _databaseService.ArchiveMissingAccountsAsync(missingAccounts, "missing_from_popup");
            removedCount = missingAccounts.Count;
        }

        // Record update history log
        await _databaseService.RecordUpdateHistoryAsync(
            totalAccounts: validFetchedAccounts.Count,
            newAccounts: newCount,
            updatedAccounts: updatedCount,
            removedAccounts: removedCount,
            activeAmount: (int)normalizedList.Sum(a => a.Amount),
            status: "success");

        return (newCount, updatedCount, removedCount);
    }

    /// <summary>
    /// Parses denomination text like '2,000.00 Cr.' into numeric amount.
    /// </summary>
    public static int ParseDenomination(string? denomination)
    {
        if (string.IsNullOrWhiteSpace(denomination))
        {
            return 0;
        }

        var cleaned = denomination
            .Replace("Cr.", "", StringComparison.OrdinalIgnoreCase)
            .Replace(",", "")
            .Trim();

        if (decimal.TryParse(cleaned, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
        {
            return (int)Math.Round(parsed);
        }

        return 0;
    }

    /// <summary>
    /// Parses month paid count.
    /// </summary>
    public static int ParseMonthPaidNumber(string? monthPaid)
    {
        if (string.IsNullOrWhiteSpace(monthPaid))
        {
            return 0;
        }

        var digitsOnly = Regex.Match(monthPaid, @"\d+");
        if (digitsOnly.Success && int.TryParse(digitsOnly.Value, out var count))
        {
            return count;
        }

        return 0;
    }

    /// <summary>
    /// Parses date string into ISO standard YYYY-MM-DD.
    /// </summary>
    public static string? ParseDueDateIso(string? dateText)
    {
        if (string.IsNullOrWhiteSpace(dateText))
        {
            return null;
        }

        var trimmed = dateText.Trim();
        foreach (var fmt in DateFormats)
        {
            if (DateTime.TryParseExact(trimmed, fmt, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt) ||
                DateTime.TryParseExact(trimmed, fmt, CultureInfo.CurrentCulture, DateTimeStyles.None, out dt))
            {
                return dt.ToString("yyyy-MM-dd");
            }
        }

        if (DateTime.TryParse(trimmed, out var fallbackDt))
        {
            return fallbackDt.ToString("yyyy-MM-dd");
        }

        return null;
    }
}
