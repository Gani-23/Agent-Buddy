using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace AgentBuddy.Services;

public sealed class TelemetryService
{
    private const string DefaultServerUrl = "https://oauth4-0.onrender.com";
    private readonly LicenseService _licenseService;
    private readonly HttpClient _httpClient;

    public TelemetryService(LicenseService licenseService, HttpClient? httpClient = null)
    {
        _licenseService = licenseService;
        _httpClient = httpClient ?? new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10),
        };
    }

    private async Task<string> ResolveServerUrlAsync()
    {
        var settings = await _licenseService.GetLicenseSettingsAsync();
        return !string.IsNullOrWhiteSpace(settings.ServerUrl)
            ? settings.ServerUrl.TrimEnd('/')
            : DefaultServerUrl;
    }

    /// <summary>
    /// Silently ping server with heartbeat metadata (fire-and-forget, never throws)
    /// </summary>
    public async Task SendHeartbeatAsync(string? user = null)
    {
        try
        {
            var serverUrl = await ResolveServerUrlAsync();
            var settings = await _licenseService.GetLicenseSettingsAsync();

            var payload = new
            {
                appId = !string.IsNullOrWhiteSpace(settings.AppId) ? settings.AppId : "agentbuddy",
                os = $"{RuntimeInformation.OSDescription} ({RuntimeInformation.ProcessArchitecture})",
                version = "1.0.0",
                user = !string.IsNullOrWhiteSpace(user) ? user : settings.Subject,
            };

            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(6));

            await _httpClient.PostAsync($"{serverUrl}/api/telemetry/heartbeat", content, cts.Token);
        }
        catch
        {
            // Telemetry must never crash or block the application
        }
    }

    /// <summary>
    /// Silently submit field crash report to trigger instant Telegram / Discord alert
    /// </summary>
    public async Task SendCrashReportAsync(string context, Exception? ex)
    {
        try
        {
            var serverUrl = await ResolveServerUrlAsync();
            var settings = await _licenseService.GetLicenseSettingsAsync();

            var payload = new
            {
                appId = !string.IsNullOrWhiteSpace(settings.AppId) ? settings.AppId : "agentbuddy",
                error = $"{context}: {ex?.Message ?? "Unknown error"}",
                stack = ex?.ToString(),
                os = $"{RuntimeInformation.OSDescription} ({RuntimeInformation.ProcessArchitecture})",
                version = "1.0.0",
                user = settings.Subject,
            };

            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));

            await _httpClient.PostAsync($"{serverUrl}/api/telemetry/crash", content, cts.Token);
        }
        catch
        {
            // Silent error logging fallback
        }
    }

    /// <summary>
    /// Request a license renewal directly from inside AgentBuddy, notifying Telegram admin
    /// </summary>
    public async Task<(bool Success, string Message, string? RequestId)> RequestRenewalAsync(string? reason = null)
    {
        try
        {
            var serverUrl = await ResolveServerUrlAsync();
            var settings = await _licenseService.GetLicenseSettingsAsync();

            if (string.IsNullOrWhiteSpace(settings.Token))
            {
                return (false, "No active license token configured to renew.", null);
            }

            var payload = new
            {
                appId = !string.IsNullOrWhiteSpace(settings.AppId) ? settings.AppId : "agentbuddy",
                token = settings.Token,
                reason = !string.IsNullOrWhiteSpace(reason) ? reason : "In-app renewal request from AgentBuddy desktop",
            };

            var json = JsonSerializer.Serialize(payload);
            using var content = new StringContent(json, Encoding.UTF8, "application/json");
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

            var response = await _httpClient.PostAsync($"{serverUrl}/api/licenses/request-renewal", content, cts.Token);
            var responseBody = await response.Content.ReadAsStringAsync(cts.Token);

            if (!response.IsSuccessStatusCode)
            {
                return (false, $"Server returned error {response.StatusCode}: {responseBody}", null);
            }

            using var doc = JsonDocument.Parse(responseBody);
            var root = doc.RootElement;
            var requestId = root.TryGetProperty("requestId", out var reqIdProp) ? reqIdProp.GetString() : null;
            var message = root.TryGetProperty("message", out var msgProp) ? msgProp.GetString() : "Renewal request sent!";

            return (true, message ?? "Renewal request submitted to administrator.", requestId);
        }
        catch (Exception ex)
        {
            return (false, $"Network error requesting renewal: {ex.Message}", null);
        }
    }

    /// <summary>
    /// Check if admin approved renewal remotely; if yes, auto-apply the new license!
    /// </summary>
    public async Task<(bool Renewed, string Message)> CheckAutoRenewalAsync(string? requestId = null)
    {
        try
        {
            var serverUrl = await ResolveServerUrlAsync();
            var settings = await _licenseService.GetLicenseSettingsAsync();

            if (string.IsNullOrWhiteSpace(settings.Token))
            {
                return (false, "No license token found.");
            }

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(12));

            // Strategy 1: Check by requestId if provided
            if (!string.IsNullOrWhiteSpace(requestId))
            {
                var reqUrl = $"{serverUrl}/api/licenses/check-renewal?requestId={Uri.EscapeDataString(requestId)}";
                var response = await _httpClient.GetAsync(reqUrl, cts.Token);
                if (response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(cts.Token);
                    using var doc = JsonDocument.Parse(body);
                    var status = doc.RootElement.TryGetProperty("status", out var s) ? s.GetString() : null;
                    if (status == "approved" && doc.RootElement.TryGetProperty("newLicenseToken", out var newTokenProp))
                    {
                        var newToken = newTokenProp.GetString();
                        if (!string.IsNullOrWhiteSpace(newToken))
                        {
                            var applyResult = await _licenseService.ApplyNewTokenAsync(newToken, cts.Token);
                            return (applyResult.Success, applyResult.Message);
                        }
                    }
                }
            }

            // Strategy 2: Check by active token
            var checkUrl = $"{serverUrl}/api/licenses/check-active?token={Uri.EscapeDataString(settings.Token)}";
            var activeRes = await _httpClient.GetAsync(checkUrl, cts.Token);
            if (activeRes.IsSuccessStatusCode)
            {
                var body = await activeRes.Content.ReadAsStringAsync(cts.Token);
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty("hasNewLicense", out var hasProp) && hasProp.GetBoolean())
                {
                    var newToken = doc.RootElement.TryGetProperty("newLicenseToken", out var tokenProp) ? tokenProp.GetString() : null;
                    if (!string.IsNullOrWhiteSpace(newToken))
                    {
                        var applyResult = await _licenseService.ApplyNewTokenAsync(newToken, cts.Token);
                        return (applyResult.Success, $"Auto-renewal applied! {applyResult.Message}");
                    }
                }
            }

            return (false, "No pending renewals approved yet.");
        }
        catch (Exception ex)
        {
            return (false, $"Error checking auto-renewal: {ex.Message}");
        }
    }
}
