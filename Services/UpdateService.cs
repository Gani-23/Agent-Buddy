using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
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
    string? AssetDownloadUrl,
    string? AssetName,
    long AssetSize,
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
                    return new UpdateCheckResult(false, currentVersion, currentVersion, null, null, null, 0, false);
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
                return new UpdateCheckResult(false, currentVersion, currentVersion, null, null, null, 0, false);
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            var releases = JsonSerializer.Deserialize<List<GitHubRelease>>(json, JsonOptions);
            if (releases == null || releases.Count == 0)
            {
                return new UpdateCheckResult(false, currentVersion, currentVersion, null, null, null, 0, false);
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
                return new UpdateCheckResult(false, currentVersion, currentVersion, null, null, null, 0, false);
            }

            if (!TryParseVersion(currentVersion, out var current))
            {
                return new UpdateCheckResult(false, currentVersion, currentVersion, highestRelease.HtmlUrl, null, null, 0, false);
            }

            var isUpdateAvailable = highestVersion > current;
            var latestVersion = isUpdateAvailable
                ? NormalizeVersion(highestRelease.TagName)
                : currentVersion;

            // Match asset for current OS and architecture
            var (pattern, fallbackName) = GetTargetAssetPattern();
            GitHubReleaseAsset? matchingAsset = null;

            if (highestRelease.Assets != null)
            {
                foreach (var asset in highestRelease.Assets)
                {
                    if (asset.Name != null && asset.Name.Contains(pattern, StringComparison.OrdinalIgnoreCase))
                    {
                        matchingAsset = asset;
                        break;
                    }
                }
            }

            string? assetDownloadUrl = matchingAsset?.BrowserDownloadUrl;
            string? assetName = matchingAsset?.Name ?? fallbackName;
            long assetSize = matchingAsset?.Size ?? 0;

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

            return new UpdateCheckResult(
                isUpdateAvailable,
                currentVersion,
                latestVersion,
                highestRelease.HtmlUrl,
                assetDownloadUrl,
                assetName,
                assetSize,
                notified);
        }
        catch
        {
            return new UpdateCheckResult(false, currentVersion, currentVersion, null, null, null, 0, false);
        }
    }

    public async Task<string> DownloadUpdateAsync(
        string downloadUrl,
        string fileName,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "AgentBuddyUpdates");
        Directory.CreateDirectory(tempDir);
        var targetFile = Path.Combine(tempDir, fileName);

        using var response = await Client.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;
        await using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        await using var fileStream = new FileStream(targetFile, FileMode.Create, FileAccess.Write, FileShare.None, 81920, true);

        var buffer = new byte[81920];
        long totalRead = 0;
        int bytesRead;

        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
        {
            await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
            totalRead += bytesRead;

            if (totalBytes.HasValue && totalBytes.Value > 0)
            {
                progress?.Report((double)totalRead / totalBytes.Value);
            }
        }

        return targetFile;
    }

    public static void ApplyUpdateAndRestart(string downloadedFilePath)
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            ApplyWindowsUpdate(downloadedFilePath);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            ApplyMacUpdate(downloadedFilePath);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            ApplyLinuxUpdate(downloadedFilePath);
        }
        else
        {
            throw new PlatformNotSupportedException("In-app auto-update is not supported on this platform.");
        }
    }

    private static void ApplyWindowsUpdate(string zipPath)
    {
        var extractDir = Path.Combine(Path.GetTempPath(), $"AgentBuddyUpdate_{Guid.NewGuid():N}");
        Directory.CreateDirectory(extractDir);
        ZipFile.ExtractToDirectory(zipPath, extractDir, overwriteFiles: true);

        var sourceDir = extractDir;
        if (!File.Exists(Path.Combine(sourceDir, "AgentBuddy.exe")))
        {
            var subDirs = Directory.GetDirectories(sourceDir);
            foreach (var sub in subDirs)
            {
                if (File.Exists(Path.Combine(sub, "AgentBuddy.exe")))
                {
                    sourceDir = sub;
                    break;
                }
            }
        }

        var currentProcess = Process.GetCurrentProcess();
        var currentPid = currentProcess.Id;
        var appDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\', '/');
        var exePath = currentProcess.MainModule?.FileName ?? Path.Combine(appDir, "AgentBuddy.exe");
        var exeName = Path.GetFileName(exePath);

        var scriptPath = Path.Combine(Path.GetTempPath(), $"agentbuddy_update_{currentPid}.cmd");
        var scriptContent = $@"@echo off
setlocal enabledelayedexpansion
title AgentBuddy Updater

set ""PID={currentPid}""
set ""TARGET={appDir}""
set ""SOURCE={sourceDir}""
set ""EXE={exeName}""

:WAIT_LOOP
timeout /t 1 /nobreak >nul
tasklist /fi ""PID eq %PID%"" 2>nul | findstr /i ""%PID%"" >nul
if not errorlevel 1 goto WAIT_LOOP

timeout /t 1 /nobreak >nul

xcopy ""%SOURCE%\*"" ""%TARGET%\"" /s /e /y /q >nul 2>&1
if errorlevel 1 (
    timeout /t 2 /nobreak >nul
    xcopy ""%SOURCE%\*"" ""%TARGET%\"" /s /e /y /q >nul 2>&1
)

start """" ""%TARGET%\%EXE%""

rd /s /q ""%SOURCE%"" >nul 2>&1
(goto) 2>nul & del ""%~f0""
exit
";

        File.WriteAllText(scriptPath, scriptContent);

        var psi = new ProcessStartInfo
        {
            FileName = "cmd.exe",
            Arguments = $"/c \"{scriptPath}\"",
            CreateNoWindow = true,
            UseShellExecute = false
        };

        Process.Start(psi);
        Environment.Exit(0);
    }

    private static void ApplyMacUpdate(string zipPath)
    {
        var extractDir = Path.Combine(Path.GetTempPath(), $"AgentBuddyUpdate_{Guid.NewGuid():N}");
        Directory.CreateDirectory(extractDir);
        ZipFile.ExtractToDirectory(zipPath, extractDir, overwriteFiles: true);

        string? newAppPath = null;
        if (Directory.Exists(Path.Combine(extractDir, "AgentBuddy.app")))
        {
            newAppPath = Path.Combine(extractDir, "AgentBuddy.app");
        }
        else
        {
            var apps = Directory.GetDirectories(extractDir, "*.app", SearchOption.AllDirectories);
            if (apps.Length > 0)
            {
                newAppPath = apps[0];
            }
        }

        var currentProcess = Process.GetCurrentProcess();
        var currentPid = currentProcess.Id;

        string targetAppPath = "";
        var dir = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        while (dir != null)
        {
            if (dir.Name.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
            {
                targetAppPath = dir.FullName;
                break;
            }
            dir = dir.Parent;
        }

        if (string.IsNullOrEmpty(targetAppPath))
        {
            targetAppPath = AppDomain.CurrentDomain.BaseDirectory;
        }

        var scriptPath = Path.Combine(Path.GetTempPath(), $"agentbuddy_update_{currentPid}.sh");
        var scriptContent = $@"#!/bin/bash
PID=""{currentPid}""
TARGET=""{targetAppPath}""
SOURCE=""{newAppPath ?? extractDir}""

while kill -0 ""$PID"" 2>/dev/null; do
    sleep 0.5
done
sleep 1

if [[ ""$SOURCE"" == *.app ]] && [[ ""$TARGET"" == *.app ]]; then
    rm -rf ""$TARGET""
    cp -R ""$SOURCE"" ""$TARGET""
    xattr -cr ""$TARGET"" 2>/dev/null || true
    chmod -R +x ""$TARGET/Contents/MacOS"" 2>/dev/null || true
    open ""$TARGET""
else
    cp -R ""$SOURCE/""* ""$TARGET/""
    chmod +x ""$TARGET/AgentBuddy"" 2>/dev/null || true
    open ""$TARGET"" 2>/dev/null || ""$TARGET/AgentBuddy"" &
fi

rm -rf ""{extractDir}""
rm -f ""$0""
";

        File.WriteAllText(scriptPath, scriptContent);
        Process.Start("chmod", $"+x \"{scriptPath}\"")?.WaitForExit(2000);

        var psi = new ProcessStartInfo
        {
            FileName = "/bin/bash",
            Arguments = $"\"{scriptPath}\"",
            CreateNoWindow = true,
            UseShellExecute = false
        };

        Process.Start(psi);
        Environment.Exit(0);
    }

    private static void ApplyLinuxUpdate(string tarPath)
    {
        var extractDir = Path.Combine(Path.GetTempPath(), $"AgentBuddyUpdate_{Guid.NewGuid():N}");
        Directory.CreateDirectory(extractDir);
        Process.Start("tar", $"-xzf \"{tarPath}\" -C \"{extractDir}\"")?.WaitForExit(10000);

        var currentProcess = Process.GetCurrentProcess();
        var currentPid = currentProcess.Id;
        var targetDir = AppDomain.CurrentDomain.BaseDirectory.TrimEnd('/');

        var scriptPath = Path.Combine(Path.GetTempPath(), $"agentbuddy_update_{currentPid}.sh");
        var scriptContent = $@"#!/bin/bash
PID=""{currentPid}""
TARGET=""{targetDir}""
SOURCE=""{extractDir}""

while kill -0 ""$PID"" 2>/dev/null; do
    sleep 0.5
done
sleep 1

cp -R ""$SOURCE/""* ""$TARGET/""
chmod +x ""$TARGET/AgentBuddy"" 2>/dev/null || true

nohup ""$TARGET/AgentBuddy"" >/dev/null 2>&1 &
rm -rf ""$SOURCE""
rm -f ""$0""
";

        File.WriteAllText(scriptPath, scriptContent);
        Process.Start("chmod", $"+x \"{scriptPath}\"")?.WaitForExit(2000);

        var psi = new ProcessStartInfo
        {
            FileName = "/bin/bash",
            Arguments = $"\"{scriptPath}\"",
            CreateNoWindow = true,
            UseShellExecute = false
        };

        Process.Start(psi);
        Environment.Exit(0);
    }

    public static (string Pattern, string FallbackName) GetTargetAssetPattern()
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return RuntimeInformation.ProcessArchitecture == Architecture.X86
                ? ("win-x86", "AgentBuddy-win-x86.zip")
                : ("win-x64", "AgentBuddy-win-x64.zip");
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            return RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                ? ("osx-arm64", "AgentBuddy-macos-osx-arm64.zip")
                : ("osx-x64", "AgentBuddy-macos-osx-x64.zip");
        }
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            return RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                ? ("linux-arm64", "AgentBuddy-linux-arm64.tar.gz")
                : ("linux-x64", "AgentBuddy-linux-x64.tar.gz");
        }
        return ("win-x64", "AgentBuddy-win-x64.zip");
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(5)
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

        [JsonPropertyName("assets")]
        public List<GitHubReleaseAsset>? Assets { get; init; }
    }

    private sealed class GitHubReleaseAsset
    {
        [JsonPropertyName("name")]
        public string? Name { get; init; }

        [JsonPropertyName("browser_download_url")]
        public string? BrowserDownloadUrl { get; init; }

        [JsonPropertyName("size")]
        public long Size { get; init; }
    }
}
