using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AgentBuddy.Models;

namespace AgentBuddy.Services;

public sealed class LicenseService
{
    private const string LicenseServerUrlKey = "license_server_url";
    private const string LicenseAppIdKey = "license_app_id";
    private const string LicenseTokenKey = "license_token";
    private const string LicenseTokenExpKey = "license_token_exp_utc";
    private const string LicenseValidatedAtKey = "license_validated_at_utc";
    private const string LicenseSubjectKey = "license_subject";

    private readonly DatabaseService _databaseService;
    private readonly HttpClient _httpClient;

    public LicenseService(DatabaseService databaseService, HttpClient? httpClient = null)
    {
        _databaseService = databaseService;
        _httpClient = httpClient ?? new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(15),
        };
    }

    public async Task<LicenseSettings> GetLicenseSettingsAsync()
    {
        var serverUrl = (await _databaseService.GetAppSettingAsync(LicenseServerUrlKey) ?? string.Empty).Trim();
        var appId = (await _databaseService.GetAppSettingAsync(LicenseAppIdKey) ?? string.Empty).Trim().ToLowerInvariant();
        var encodedToken = (await _databaseService.GetAppSettingAsync(LicenseTokenKey) ?? string.Empty).Trim();
        var token = DecodeToken(encodedToken);
        var expRaw = (await _databaseService.GetAppSettingAsync(LicenseTokenExpKey) ?? string.Empty).Trim();
        var validatedRaw = (await _databaseService.GetAppSettingAsync(LicenseValidatedAtKey) ?? string.Empty).Trim();
        var subject = (await _databaseService.GetAppSettingAsync(LicenseSubjectKey) ?? string.Empty).Trim();

        return new LicenseSettings
        {
            ServerUrl = serverUrl,
            AppId = appId,
            Token = token,
            ExpiresAtUtc = ParseDateTimeOffsetOrNull(expRaw),
            LastValidatedAtUtc = ParseDateTimeOffsetOrNull(validatedRaw),
            Subject = subject,
        };
    }

    public async Task<LicenseStatus> GetCurrentStatusAsync(bool validateOnline, CancellationToken cancellationToken = default)
    {
        var settings = await GetLicenseSettingsAsync();
        return await BuildStatusAsync(settings, validateOnline, cancellationToken);
    }

    public async Task<LicenseActivationResult> ActivateLicenseAsync(
        string? serverUrl,
        string? appId,
        string? tokenInput,
        CancellationToken cancellationToken = default)
    {
        var rawServer = (serverUrl ?? string.Empty).Trim();
        var normalizedServer = NormalizeServerUrl(serverUrl);
        var normalizedAppId = NormalizeAppId(appId);
        var token = NormalizeBearerToken(tokenInput);

        if (!string.IsNullOrWhiteSpace(rawServer) && string.IsNullOrWhiteSpace(normalizedServer))
        {
            return Fail("License server URL is invalid.");
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            return Fail("License token is required.");
        }

        var payload = TryReadJwtPayload(token);
        if (payload == null)
        {
            return Fail("Invalid token format. Please paste a valid JWT license token.");
        }

        if (payload.ExpiresAtUtc == null)
        {
            return Fail("License token does not contain an expiry (exp).");
        }

        if (payload.ExpiresAtUtc <= DateTimeOffset.UtcNow)
        {
            return Fail("License token is expired.");
        }

        var tokenAppId = NormalizeAppId(payload.AppId);
        var tokenHasWildcard = tokenAppId == "*";
        var scopedTokenApps = payload.ScopedApps;
        var effectiveAppId = tokenHasWildcard
            ? (string.IsNullOrWhiteSpace(normalizedAppId) ? scopedTokenApps.FirstOrDefault() ?? string.Empty : normalizedAppId)
            : (string.IsNullOrWhiteSpace(tokenAppId) ? normalizedAppId : tokenAppId);
        if (string.IsNullOrWhiteSpace(effectiveAppId))
        {
            return Fail("App ID is required. Enter an App ID or use a token that contains appId.");
        }

        if (!string.IsNullOrWhiteSpace(normalizedAppId) &&
            !string.IsNullOrWhiteSpace(tokenAppId) &&
            !tokenHasWildcard &&
            !string.Equals(normalizedAppId, tokenAppId, StringComparison.OrdinalIgnoreCase))
        {
            return Fail($"Token appId '{tokenAppId}' does not match entered appId '{normalizedAppId}'.");
        }

        if (tokenHasWildcard &&
            scopedTokenApps.Count > 0 &&
            !scopedTokenApps.Contains(effectiveAppId, StringComparer.OrdinalIgnoreCase))
        {
            return Fail($"Selected appId '{effectiveAppId}' is not included in token scope.");
        }

        var settings = new LicenseSettings
        {
            ServerUrl = normalizedServer,
            AppId = effectiveAppId,
            Token = token,
            ExpiresAtUtc = payload.ExpiresAtUtc,
            LastValidatedAtUtc = null,
            Subject = payload.Subject ?? string.Empty,
        };

        if (!string.IsNullOrWhiteSpace(normalizedServer))
        {
            var onlineCheck = await CheckOnlineAccessAsync(settings, cancellationToken);
            if (!onlineCheck.Success)
            {
                return Fail(onlineCheck.Message);
            }

            settings = settings with { LastValidatedAtUtc = DateTimeOffset.UtcNow };
        }

        await SaveLicenseSettingsAsync(settings);
        var status = await BuildStatusAsync(settings, validateOnline: false, cancellationToken);
        return new LicenseActivationResult
        {
            Success = true,
            Message = "License activated successfully.",
            Status = status with { Message = "License activated successfully." },
        };
    }

    public async Task<LicenseActivationResult> ValidateStoredLicenseAsync(CancellationToken cancellationToken = default)
    {
        var settings = await GetLicenseSettingsAsync();
        if (string.IsNullOrWhiteSpace(settings.Token))
        {
            var empty = await BuildStatusAsync(settings, validateOnline: false, cancellationToken);
            return new LicenseActivationResult
            {
                Success = false,
                Message = "No saved license token.",
                Status = empty with { Message = "No saved license token." },
            };
        }

        var status = await BuildStatusAsync(settings, validateOnline: true, cancellationToken);
        if (!status.IsActive)
        {
            return new LicenseActivationResult
            {
                Success = false,
                Message = status.Message,
                Status = status,
            };
        }

        return new LicenseActivationResult
        {
            Success = true,
            Message = "Stored license is valid.",
            Status = status with { Message = "Stored license is valid." },
        };
    }

    public async Task ClearLicenseAsync()
    {
        await _databaseService.SaveAppSettingAsync(LicenseServerUrlKey, string.Empty);
        await _databaseService.SaveAppSettingAsync(LicenseAppIdKey, string.Empty);
        await _databaseService.SaveAppSettingAsync(LicenseTokenKey, string.Empty);
        await _databaseService.SaveAppSettingAsync(LicenseTokenExpKey, string.Empty);
        await _databaseService.SaveAppSettingAsync(LicenseValidatedAtKey, string.Empty);
        await _databaseService.SaveAppSettingAsync(LicenseSubjectKey, string.Empty);
    }

    private async Task SaveLicenseSettingsAsync(LicenseSettings settings)
    {
        await _databaseService.SaveAppSettingAsync(LicenseServerUrlKey, settings.ServerUrl);
        await _databaseService.SaveAppSettingAsync(LicenseAppIdKey, settings.AppId);
        await _databaseService.SaveAppSettingAsync(LicenseTokenKey, EncodeToken(settings.Token));
        await _databaseService.SaveAppSettingAsync(
            LicenseTokenExpKey,
            settings.ExpiresAtUtc?.UtcDateTime.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty);
        await _databaseService.SaveAppSettingAsync(
            LicenseValidatedAtKey,
            settings.LastValidatedAtUtc?.UtcDateTime.ToString("O", CultureInfo.InvariantCulture) ?? string.Empty);
        await _databaseService.SaveAppSettingAsync(LicenseSubjectKey, settings.Subject);
    }

    private async Task<LicenseStatus> BuildStatusAsync(
        LicenseSettings settings,
        bool validateOnline,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.Token))
        {
            return new LicenseStatus
            {
                IsActive = false,
                Message = "License is not configured.",
                ServerUrl = settings.ServerUrl,
                AppId = settings.AppId,
                ExpiresAtUtc = settings.ExpiresAtUtc,
                LastValidatedAtUtc = settings.LastValidatedAtUtc,
                Subject = settings.Subject,
            };
        }

        var payload = TryReadJwtPayload(settings.Token);
        if (payload == null || payload.ExpiresAtUtc == null)
        {
            return new LicenseStatus
            {
                IsActive = false,
                Message = "Saved license token is invalid.",
                ServerUrl = settings.ServerUrl,
                AppId = settings.AppId,
                ExpiresAtUtc = settings.ExpiresAtUtc,
                LastValidatedAtUtc = settings.LastValidatedAtUtc,
                Subject = settings.Subject,
            };
        }

        var expiresAt = payload.ExpiresAtUtc.Value;
        var tokenAppId = NormalizeAppId(payload.AppId);
        var tokenHasWildcard = tokenAppId == "*";
        var scopedTokenApps = payload.ScopedApps;
        var effectiveAppId = string.IsNullOrWhiteSpace(settings.AppId)
            ? (tokenHasWildcard ? (scopedTokenApps.FirstOrDefault() ?? string.Empty) : tokenAppId)
            : NormalizeAppId(settings.AppId);
        if (string.IsNullOrWhiteSpace(effectiveAppId))
        {
            return new LicenseStatus
            {
                IsActive = false,
                Message = "License token is missing appId.",
                ServerUrl = settings.ServerUrl,
                AppId = settings.AppId,
                ExpiresAtUtc = expiresAt,
                LastValidatedAtUtc = settings.LastValidatedAtUtc,
                Subject = payload.Subject ?? settings.Subject,
            };
        }

        if (tokenHasWildcard &&
            scopedTokenApps.Count > 0 &&
            !scopedTokenApps.Contains(effectiveAppId, StringComparer.OrdinalIgnoreCase))
        {
            return new LicenseStatus
            {
                IsActive = false,
                Message = $"Saved appId '{effectiveAppId}' is outside token scope.",
                ServerUrl = settings.ServerUrl,
                AppId = effectiveAppId,
                ExpiresAtUtc = expiresAt,
                LastValidatedAtUtc = settings.LastValidatedAtUtc,
                Subject = payload.Subject ?? settings.Subject,
            };
        }

        if (expiresAt <= DateTimeOffset.UtcNow)
        {
            return new LicenseStatus
            {
                IsActive = false,
                Message = $"License expired on {expiresAt:yyyy-MM-dd HH:mm} UTC.",
                ServerUrl = settings.ServerUrl,
                AppId = effectiveAppId,
                ExpiresAtUtc = expiresAt,
                LastValidatedAtUtc = settings.LastValidatedAtUtc,
                Subject = payload.Subject ?? settings.Subject,
            };
        }

        var updatedSettings = settings with
        {
            AppId = effectiveAppId,
            ExpiresAtUtc = expiresAt,
            Subject = payload.Subject ?? settings.Subject,
        };

        if (validateOnline && !string.IsNullOrWhiteSpace(updatedSettings.ServerUrl))
        {
            var onlineCheck = await CheckOnlineAccessAsync(updatedSettings, cancellationToken);
            if (!onlineCheck.Success)
            {
                return new LicenseStatus
                {
                    IsActive = false,
                    Message = onlineCheck.Message,
                    ServerUrl = updatedSettings.ServerUrl,
                    AppId = updatedSettings.AppId,
                    ExpiresAtUtc = updatedSettings.ExpiresAtUtc,
                    LastValidatedAtUtc = updatedSettings.LastValidatedAtUtc,
                    Subject = updatedSettings.Subject,
                };
            }

            updatedSettings = updatedSettings with
            {
                LastValidatedAtUtc = DateTimeOffset.UtcNow,
            };
            await SaveLicenseSettingsAsync(updatedSettings);
        }

        return new LicenseStatus
        {
            IsActive = true,
            Message = $"License active for app '{updatedSettings.AppId}' until {updatedSettings.ExpiresAtUtc:yyyy-MM-dd HH:mm} UTC.",
            ServerUrl = updatedSettings.ServerUrl,
            AppId = updatedSettings.AppId,
            ExpiresAtUtc = updatedSettings.ExpiresAtUtc,
            LastValidatedAtUtc = updatedSettings.LastValidatedAtUtc,
            Subject = updatedSettings.Subject,
        };
    }

    private async Task<(bool Success, string Message)> CheckOnlineAccessAsync(
        LicenseSettings settings,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(settings.ServerUrl))
        {
            return (true, string.Empty);
        }

        try
        {
            var requestUri = new Uri(new Uri(settings.ServerUrl), "/api/users/apps");
            using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", settings.Token);
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var message = ExtractApiMessage(body);
                return (false, string.IsNullOrWhiteSpace(message)
                    ? $"Online validation failed with status {(int)response.StatusCode}."
                    : message);
            }

            var hasAccess = AppExistsInAppsResponse(body, settings.AppId);
            if (!hasAccess)
            {
                return (false, $"Token is valid but app access '{settings.AppId}' is missing.");
            }

            return (true, "Online validation passed.");
        }
        catch (Exception ex)
        {
            return (false, $"Online validation error: {ex.Message}");
        }
    }

    private static bool AppExistsInAppsResponse(string body, string appId)
    {
        try
        {
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("apps", out var appsElement) || appsElement.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            var normalizedAppId = NormalizeAppId(appId);
            foreach (var app in appsElement.EnumerateArray())
            {
                if (!app.TryGetProperty("appId", out var appIdElement))
                {
                    continue;
                }

                var candidate = NormalizeAppId(appIdElement.GetString());
                if (!string.Equals(candidate, normalizedAppId, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var status = app.TryGetProperty("status", out var statusElement)
                    ? (statusElement.GetString() ?? string.Empty).Trim().ToLowerInvariant()
                    : "active";
                return status != "inactive";
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    private static string ExtractApiMessage(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return string.Empty;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.TryGetProperty("message", out var messageElement))
            {
                return messageElement.GetString() ?? string.Empty;
            }
        }
        catch
        {
            // Ignore malformed JSON and return empty message.
        }

        return string.Empty;
    }

    private static DateTimeOffset? ParseDateTimeOffsetOrNull(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        return DateTimeOffset.TryParse(input, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var value)
            ? value.ToUniversalTime()
            : null;
    }

    private static string NormalizeServerUrl(string? raw)
    {
        var value = (raw ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        if (!value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !value.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            value = $"https://{value}";
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return string.Empty;
        }

        var normalized = uri.GetLeftPart(UriPartial.Authority).TrimEnd('/');
        return normalized;
    }

    private static string NormalizeAppId(string? raw)
    {
        return (raw ?? string.Empty).Trim().ToLowerInvariant();
    }

    private static string NormalizeBearerToken(string? input)
    {
        var value = (input ?? string.Empty).Trim();
        if (value.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            value = value[7..].Trim();
        }

        return value;
    }

    private static JwtPayloadInfo? TryReadJwtPayload(string token)
    {
        var parts = (token ?? string.Empty).Split('.');
        if (parts.Length < 2)
        {
            return null;
        }

        try
        {
            var payloadBytes = Base64UrlDecode(parts[1]);
            var payloadJson = Encoding.UTF8.GetString(payloadBytes);
            using var doc = JsonDocument.Parse(payloadJson);
            var root = doc.RootElement;

            DateTimeOffset? exp = null;
            if (root.TryGetProperty("exp", out var expElement))
            {
                if (expElement.ValueKind == JsonValueKind.Number && expElement.TryGetInt64(out var unixExp))
                {
                    exp = DateTimeOffset.FromUnixTimeSeconds(unixExp);
                }
                else if (expElement.ValueKind == JsonValueKind.String &&
                         long.TryParse(expElement.GetString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var unixStringExp))
                {
                    exp = DateTimeOffset.FromUnixTimeSeconds(unixStringExp);
                }
            }

            var appId = string.Empty;
            if (root.TryGetProperty("appId", out var appIdElement))
            {
                appId = NormalizeAppId(appIdElement.GetString());
            }
            else if (root.TryGetProperty("app_id", out var appIdSnakeElement))
            {
                appId = NormalizeAppId(appIdSnakeElement.GetString());
            }
            else if (root.TryGetProperty("aud", out var audElement))
            {
                appId = ReadAudienceAsAppId(audElement);
            }

            var scopedApps = ReadScopeApps(root);
            if (string.IsNullOrWhiteSpace(appId) && scopedApps.Count == 1)
            {
                appId = scopedApps[0];
            }

            var subject = root.TryGetProperty("sub", out var subElement)
                ? (subElement.GetString() ?? string.Empty).Trim()
                : string.Empty;

            return new JwtPayloadInfo
            {
                ExpiresAtUtc = exp,
                AppId = appId,
                ScopedApps = scopedApps,
                Subject = subject,
            };
        }
        catch
        {
            return null;
        }
    }

    private static string ReadAudienceAsAppId(JsonElement audElement)
    {
        if (audElement.ValueKind == JsonValueKind.String)
        {
            return NormalizeAppId(audElement.GetString());
        }

        if (audElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in audElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var candidate = NormalizeAppId(item.GetString());
                    if (!string.IsNullOrWhiteSpace(candidate))
                    {
                        return candidate;
                    }
                }
            }
        }

        return string.Empty;
    }

    private static IReadOnlyList<string> ReadScopeApps(JsonElement root)
    {
        var items = new List<string>();
        if (root.TryGetProperty("projects", out var projectsElement) && projectsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in projectsElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var appId = NormalizeAppId(item.GetString());
                    if (!string.IsNullOrWhiteSpace(appId))
                    {
                        items.Add(appId);
                    }
                }
            }
        }

        if (root.TryGetProperty("apps", out var appsElement) && appsElement.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in appsElement.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.String)
                {
                    var appId = NormalizeAppId(item.GetString());
                    if (!string.IsNullOrWhiteSpace(appId))
                    {
                        items.Add(appId);
                    }
                }
            }
        }

        return items.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var value = (input ?? string.Empty).Replace('-', '+').Replace('_', '/');
        switch (value.Length % 4)
        {
            case 2:
                value += "==";
                break;
            case 3:
                value += "=";
                break;
        }

        return Convert.FromBase64String(value);
    }

    private static string EncodeToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return string.Empty;
        }

        return Convert.ToBase64String(Encoding.UTF8.GetBytes(token));
    }

    private static string DecodeToken(string encodedToken)
    {
        if (string.IsNullOrWhiteSpace(encodedToken))
        {
            return string.Empty;
        }

        try
        {
            var bytes = Convert.FromBase64String(encodedToken);
            return Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return string.Empty;
        }
    }

    private static LicenseActivationResult Fail(string message)
    {
        return new LicenseActivationResult
        {
            Success = false,
            Message = message,
            Status = new LicenseStatus
            {
                IsActive = false,
                Message = message,
            },
        };
    }

    private sealed class JwtPayloadInfo
    {
        public DateTimeOffset? ExpiresAtUtc { get; init; }
        public string AppId { get; init; } = string.Empty;
        public IReadOnlyList<string> ScopedApps { get; init; } = Array.Empty<string>();
        public string Subject { get; init; } = string.Empty;
    }
}
