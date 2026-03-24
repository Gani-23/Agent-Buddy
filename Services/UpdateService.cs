using System;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace AgentBuddy.Services;

public sealed record UpdateCheckResult(
    bool IsUpdateAvailable,
    string CurrentVersion,
    string LatestVersion,
    string? ReleaseUrl,
    bool Notified);

public sealed class UpdateService
{
    private const string LatestReleaseUrl = "https://api.github.com/repos/Gani-23/Agent-Buddy/releases/latest";
    private const string LastCheckKey = "update_last_check_utc";
    private const string LastNotifiedKey = "update_last_notified_version";
    private static readonly TimeSpan MinCheckInterval = TimeSpan.FromHours(6);

    private static readonly HttpClient Client = CreateClient();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly DatabaseService _databaseService;

    public UpdateService(DatabaseService databaseService)
    {
        _databaseService = databaseService;
    }

    public static TimeSpan DefaultInterval => MinCheckInterval;

    public async Task<UpdateCheckResult?> CheckForUpdatesAsync(bool force = false, CancellationToken cancellationToken = default)
    {
        var currentVersion = GetCurrentVersion();

        if (!force)
        {
            var lastCheckValue = await _databaseService.GetAppSettingAsync(LastCheckKey);
            if (DateTime.TryParse(lastCheckValue, out var lastCheckUtc))
            {
                var elapsed = DateTime.UtcNow - lastCheckUtc.ToUniversalTime();
                if (elapsed < MinCheckInterval)
                {
                    return new UpdateCheckResult(false, currentVersion, currentVersion, null, false);
                }
            }
        }

        await _databaseService.SaveAppSettingAsync(LastCheckKey, DateTime.UtcNow.ToString("O"));

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, LatestReleaseUrl);
            using var response = await Client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new UpdateCheckResult(false, currentVersion, currentVersion, null, false);
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var release = JsonSerializer.Deserialize<GitHubRelease>(json, JsonOptions);
            if (release == null || release.Draft || release.Prerelease)
            {
                return new UpdateCheckResult(false, currentVersion, currentVersion, null, false);
            }

            var latestVersion = NormalizeVersion(release.TagName);
            if (!TryParseVersion(currentVersion, out var current) || !TryParseVersion(latestVersion, out var latest))
            {
                return new UpdateCheckResult(false, currentVersion, latestVersion, release.HtmlUrl, false);
            }

            var isUpdateAvailable = latest > current;
            var notified = false;

            if (isUpdateAvailable)
            {
                var lastNotified = await _databaseService.GetAppSettingAsync(LastNotifiedKey);
                if (!string.Equals(lastNotified, latestVersion, StringComparison.OrdinalIgnoreCase))
                {
                    await _databaseService.SaveAppSettingAsync(LastNotifiedKey, latestVersion);
                    notified = true;
                }
            }

            return new UpdateCheckResult(isUpdateAvailable, currentVersion, latestVersion, release.HtmlUrl, notified);
        }
        catch
        {
            return new UpdateCheckResult(false, currentVersion, currentVersion, null, false);
        }
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        client.DefaultRequestHeaders.UserAgent.ParseAdd("AgentBuddy/1.0");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private static string GetCurrentVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            return informational.Trim();
        }

        var version = assembly.GetName().Version;
        return version?.ToString() ?? "0.0.0";
    }

    private static string NormalizeVersion(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return "0.0.0";
        }

        var match = Regex.Match(tag, @"\d+(?:\.\d+){0,3}");
        return match.Success ? match.Value : tag.Trim();
    }

    private static bool TryParseVersion(string? value, out Version version)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            version = new Version(0, 0, 0);
            return false;
        }

        var normalized = NormalizeVersion(value);
        return Version.TryParse(normalized, out version);
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; init; }

        [JsonPropertyName("html_url")]
        public string? HtmlUrl { get; init; }

        [JsonPropertyName("draft")]
        public bool Draft { get; init; }

        [JsonPropertyName("prerelease")]
        public bool Prerelease { get; init; }
    }
}
