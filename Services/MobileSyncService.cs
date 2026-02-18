using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using AgentBuddy.Models;

namespace AgentBuddy.Services;

/// <summary>
/// Service for syncing local data to mobile dashboard API.
/// </summary>
public sealed class MobileSyncService
{
    private readonly HttpClient _httpClient;

    public MobileSyncService()
    {
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(5)
        };
        _httpClient.DefaultRequestHeaders.Accept.Clear();
        _httpClient.DefaultRequestHeaders.Accept.Add(
            new MediaTypeWithQualityHeaderValue("application/json"));
    }

    public async Task<(bool success, string message)> RunSyncAsync(
        string apiBaseUrl,
        string? apiKey,
        bool fullSync = true,
        CancellationToken cancellationToken = default)
    {
        var normalizedBaseUrl = NormalizeBaseUrl(apiBaseUrl);
        if (string.IsNullOrWhiteSpace(normalizedBaseUrl))
        {
            return (false, "Mobile sync API URL is empty. Configure it in Settings.");
        }

        if (!Uri.TryCreate(normalizedBaseUrl, UriKind.Absolute, out var baseUri))
        {
            return (false, "Mobile sync API URL is invalid.");
        }

        var endpoint = new Uri(baseUri, "sync/run");
        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = JsonContent.Create(new { fullSync })
        };

        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            request.Headers.TryAddWithoutValidation("x-api-key", apiKey.Trim());
        }

        try
        {
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorText = ExtractMessage(body, "error");
                if (string.IsNullOrWhiteSpace(errorText))
                {
                    errorText = $"HTTP {(int)response.StatusCode} ({response.ReasonPhrase})";
                }
                return (false, $"Mobile sync failed: {errorText}");
            }

            var rdCount = ExtractInt(body, "rd_count");
            var detailCount = ExtractInt(body, "account_detail_count");
            var rdUpserts = ExtractInt(body, "rd_upserts");
            var rdUpdates = ExtractInt(body, "rd_updates");

            var summary = $"Mobile sync complete. rd_accounts={rdCount}, account_detail={detailCount}, upserts={rdUpserts}, updates={rdUpdates}.";
            return (true, summary);
        }
        catch (TaskCanceledException)
        {
            return (false, "Mobile sync timed out.");
        }
        catch (Exception ex)
        {
            return (false, $"Mobile sync error: {ex.Message}");
        }
    }

    public async Task<(bool success, string message)> PushRdAccountsAsync(
        string apiBaseUrl,
        string? apiKey,
        IReadOnlyCollection<RDAccount> accounts,
        Action<string>? progressCallback = null,
        int batchSize = 400,
        CancellationToken cancellationToken = default)
    {
        if (accounts == null || accounts.Count == 0)
        {
            return (false, "No accounts available for mobile sync.");
        }

        var normalizedBaseUrl = NormalizeBaseUrl(apiBaseUrl);
        if (string.IsNullOrWhiteSpace(normalizedBaseUrl))
        {
            return (false, "Mobile sync API URL is empty. Configure it in Settings.");
        }

        if (!Uri.TryCreate(normalizedBaseUrl, UriKind.Absolute, out var baseUri))
        {
            return (false, "Mobile sync API URL is invalid.");
        }

        if (batchSize < 50)
        {
            batchSize = 50;
        }

        var rows = accounts
            .Where(a => !string.IsNullOrWhiteSpace(a.AccountNo))
            .Select(MapRdAccount)
            .ToList();

        if (rows.Count == 0)
        {
            return (false, "No valid account rows available for mobile sync.");
        }

        var endpoint = new Uri(baseUri, "sync/push");
        var totalBatches = (int)Math.Ceiling(rows.Count / (double)batchSize);
        var totalProcessed = 0;
        var totalUpserts = 0;
        var totalUpdates = 0;

        for (var batchIndex = 0; batchIndex < totalBatches; batchIndex++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var skip = batchIndex * batchSize;
            var batchRows = rows.Skip(skip).Take(batchSize).ToList();
            var displayIndex = batchIndex + 1;
            progressCallback?.Invoke($"Sync to mobile: batch {displayIndex}/{totalBatches} ({batchRows.Count} accounts)...");

            var payload = new
            {
                source = "agentbuddy-desktop",
                batch_index = displayIndex,
                batch_total = totalBatches,
                total_accounts = rows.Count,
                rd_accounts = batchRows
            };

            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint)
            {
                Content = JsonContent.Create(payload)
            };

            if (!string.IsNullOrWhiteSpace(apiKey))
            {
                request.Headers.TryAddWithoutValidation("x-api-key", apiKey.Trim());
            }

            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorText = ExtractMessage(body, "error");
                if (string.IsNullOrWhiteSpace(errorText))
                {
                    errorText = $"HTTP {(int)response.StatusCode} ({response.ReasonPhrase})";
                }
                return (false, $"Mobile sync failed at batch {displayIndex}/{totalBatches}: {errorText}");
            }

            totalProcessed += ExtractInt(body, "rd_count");
            totalUpserts += ExtractInt(body, "rd_upserts");
            totalUpdates += ExtractInt(body, "rd_updates");
        }

        var summary = $"Mobile sync complete. Accounts={rows.Count}, processed={totalProcessed}, upserts={totalUpserts}, updates={totalUpdates}.";
        return (true, summary);
    }

    private static string NormalizeBaseUrl(string? apiBaseUrl)
    {
        var value = (apiBaseUrl ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        if (!value.EndsWith("/", StringComparison.Ordinal))
        {
            value += "/";
        }

        return value;
    }

    private static string? ExtractMessage(string body, string key)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty(key, out var element))
            {
                return element.GetString();
            }
        }
        catch
        {
            // Ignore parsing failures and return null.
        }

        return null;
    }

    private static int ExtractInt(string body, string key)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);

            if (doc.RootElement.TryGetProperty("result", out var result) &&
                result.ValueKind == JsonValueKind.Object &&
                result.TryGetProperty(key, out var valueFromResult))
            {
                return valueFromResult.TryGetInt32(out var parsed) ? parsed : 0;
            }

            if (doc.RootElement.TryGetProperty(key, out var value))
            {
                return value.TryGetInt32(out var parsed) ? parsed : 0;
            }
        }
        catch
        {
            // Ignore parsing failures and return 0.
        }

        return 0;
    }

    private static PushRdAccountDto MapRdAccount(RDAccount account)
    {
        var aslaas = string.IsNullOrWhiteSpace(account.AslaasNo) ? null : account.AslaasNo.Trim();

        return new PushRdAccountDto
        {
            AccountNo = (account.AccountNo ?? string.Empty).Trim(),
            AccountName = account.AccountName ?? string.Empty,
            Denomination = account.Denomination ?? string.Empty,
            MonthPaidUpto = account.MonthPaidUpto ?? string.Empty,
            MonthPaidUptoNum = account.GetMonthPaidNumber(),
            NextInstallmentDate = account.NextInstallmentDate ?? string.Empty,
            NextDueDateIso = account.NextDueDateIso ?? string.Empty,
            Amount = account.GetAmount(),
            TotalDeposit = account.TotalDeposit > 0 ? account.TotalDeposit : account.GetAmount() * account.GetMonthPaidNumber(),
            Status = account.Status ?? string.Empty,
            IsActive = account.IsActive,
            AslaasNo = aslaas,
            FirstSeen = account.FirstSeen == default ? null : account.FirstSeen.ToString("yyyy-MM-dd HH:mm:ss"),
            LastUpdated = account.LastUpdated == default ? null : account.LastUpdated.ToString("yyyy-MM-dd HH:mm:ss")
        };
    }

    private sealed class PushRdAccountDto
    {
        [JsonPropertyName("account_no")]
        public string AccountNo { get; init; } = string.Empty;

        [JsonPropertyName("account_name")]
        public string AccountName { get; init; } = string.Empty;

        [JsonPropertyName("denomination")]
        public string Denomination { get; init; } = string.Empty;

        [JsonPropertyName("month_paid_upto")]
        public string MonthPaidUpto { get; init; } = string.Empty;

        [JsonPropertyName("month_paid_upto_num")]
        public int MonthPaidUptoNum { get; init; }

        [JsonPropertyName("next_installment_date")]
        public string NextInstallmentDate { get; init; } = string.Empty;

        [JsonPropertyName("next_due_date_iso")]
        public string NextDueDateIso { get; init; } = string.Empty;

        [JsonPropertyName("amount")]
        public decimal Amount { get; init; }

        [JsonPropertyName("total_deposit")]
        public decimal TotalDeposit { get; init; }

        [JsonPropertyName("status")]
        public string Status { get; init; } = string.Empty;

        [JsonPropertyName("is_active")]
        public bool IsActive { get; init; }

        [JsonPropertyName("aslaas_no")]
        public string? AslaasNo { get; init; }

        [JsonPropertyName("first_seen")]
        public string? FirstSeen { get; init; }

        [JsonPropertyName("last_updated")]
        public string? LastUpdated { get; init; }
    }
}
