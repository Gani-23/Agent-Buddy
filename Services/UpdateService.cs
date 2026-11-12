using System;
using System.Collections.Generic;
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
    private const string ReleasesUrl = "https://api.github.com/repos/Gani-23/Agent-Buddy/releases?per_page=10";
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

    public static string CurrentVersion => GetCurrentVersion();

    public static string LatestReleasePage => "https://github.com/Gani-23/Agent-Buddy/releases/latest";

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
            using var request = new HttpRequestMessage(HttpMethod.Get, ReleasesUrl);
            using var response = await Client.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new UpdateCheckResult(false, currentVersion, currentVersion, null, false);
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var releases = JsonSerializer.Deserialize<List<GitHubRelease>>(json, JsonOptions);
            if (releases == null || releases.Count == 0)
            {
                return new UpdateCheckResult(false, currentVersion, currentVersion, null, false);
            }

            GitHubRelease? highestRelease = null;
            Version? highestVersion = null;

            foreach (var release in releases)
            {
                if (release.Draft)
                {
                    continue;
                }

                if (TryParseVersion(release.TagName, out var ver))
                {
                    if (highestVersion == null || ver > highestVersion)
                    {
                        highestVersion = ver;
                        highestRelease = release;
                    }
                }
            }

            if (highestRelease == null || highestVersion == null)
            {
                return new UpdateCheckResult(false, currentVersion, currentVersion, null, false);
            }

            if (!TryParseVersion(currentVersion, out var current))
            {
                return new UpdateCheckResult(false, currentVersion, currentVersion, highestRelease.HtmlUrl, false);
            }

            var isUpdateAvailable = highestVersion > current;
            var latestVersion = isUpdateAvailable
                ? NormalizeVersion(highestRelease.TagName)
                : currentVersion;

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

            return new UpdateCheckResult(isUpdateAvailable, currentVersion, latestVersion, highestRelease.HtmlUrl, notified);
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
            var clean = informational.Split('+')[0].Trim();
            if (!string.IsNullOrWhiteSpace(clean))
            {
                return clean;
            }
        }

        var version = assembly.GetName().Version;
        return version != null ? $"{version.Major}.{version.Minor}.{version.Build}" : "2.8.3";
    }

    private static string NormalizeVersion(string? tag)
    {
        if (string.IsNullOrWhiteSpace(tag))
        {
            return "0.0.0";
        }

        var clean = tag.Split('+')[0].Trim();
        var match = Regex.Match(clean, @"\d+(?:\.\d+){0,3}");
        return match.Success ? match.Value : clean;
    }

    private static bool TryParseVersion(string? value, out Version version)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            version = new Version(0, 0, 0);
            return false;
        }

        var normalized = NormalizeVersion(value);
        var segments = normalized.Split('.');
        if (segments.Length == 1)
        {
            normalized += ".0.0";
        }
        else if (segments.Length == 2)
        {
            normalized += ".0";
        }

        if (Version.TryParse(normalized, out var parsed) && parsed is not null)
        {
            version = parsed;
            return true;
        }

        version = new Version(0, 0, 0);
        return false;
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
