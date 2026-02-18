using System;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia.Threading;
using ReactiveUI;
using AgentBuddy.Services;

namespace AgentBuddy.ViewModels;

/// <summary>
/// Settings ViewModel - manages app settings and configuration
/// </summary>
public class SettingsViewModel : ViewModelBase
{
    private readonly DatabaseService _databaseService;
    private readonly PythonService _pythonService;

    private bool _isDarkTheme;
    private string? _pythonVersion;
    private string? _databasePath;
    private string? _scriptsPath;
    private string? _documentsPath;
    private string? _basePath;
    private string? _agentId;
    private string? _sourceDatabasePath;
    private string? _targetDatabasePath;
    private string _legacySyncStatus = string.Empty;
    private string _credentialsStatus = string.Empty;
    private string _mobileSyncStatus = string.Empty;
    private bool _isSyncing;
    private bool _syncDryRun;
    private bool _isSavingCredentials;
    private bool _isSavingMobileSyncSettings;
    private string? _editableAgentId;
    private string? _editablePassword;
    private string? _mobileSyncApiUrl;
    private string? _mobileSyncApiKey;

    public SettingsViewModel(
        DatabaseService databaseService,
        PythonService pythonService)
    {
        _databaseService = databaseService;
        _pythonService = pythonService;

        CheckPythonCommand = ReactiveCommand.Create(CheckPython);
        SyncLegacyDatabaseCommand = ReactiveCommand.CreateFromTask(SyncLegacyDatabaseAsync);
        ForceSyncLegacyDatabaseCommand = ReactiveCommand.CreateFromTask(ForceSyncLegacyDatabaseAsync);
        SaveCredentialsCommand = ReactiveCommand.CreateFromTask(SaveCredentialsAsync);
        SaveMobileSyncSettingsCommand = ReactiveCommand.CreateFromTask(SaveMobileSyncSettingsAsync);

        // Load initial data
        LoadSettings();
    }

    public bool IsDarkTheme
    {
        get => _isDarkTheme;
        set => this.RaiseAndSetIfChanged(ref _isDarkTheme, value);
    }

    public string? PythonVersion
    {
        get => _pythonVersion;
        set => this.RaiseAndSetIfChanged(ref _pythonVersion, value);
    }

    public string? DatabasePath
    {
        get => _databasePath;
        set => this.RaiseAndSetIfChanged(ref _databasePath, value);
    }

    public string? ScriptsPath
    {
        get => _scriptsPath;
        set => this.RaiseAndSetIfChanged(ref _scriptsPath, value);
    }

    public string? DocumentsPath
    {
        get => _documentsPath;
        set => this.RaiseAndSetIfChanged(ref _documentsPath, value);
    }

    public string? BasePath
    {
        get => _basePath;
        set => this.RaiseAndSetIfChanged(ref _basePath, value);
    }

    public string? AgentId
    {
        get => _agentId;
        set => this.RaiseAndSetIfChanged(ref _agentId, value);
    }

    public string? SourceDatabasePath
    {
        get => _sourceDatabasePath;
        set => this.RaiseAndSetIfChanged(ref _sourceDatabasePath, value);
    }

    public string? TargetDatabasePath
    {
        get => _targetDatabasePath;
        set => this.RaiseAndSetIfChanged(ref _targetDatabasePath, value);
    }

    public string LegacySyncStatus
    {
        get => _legacySyncStatus;
        set => this.RaiseAndSetIfChanged(ref _legacySyncStatus, value);
    }

    public string CredentialsStatus
    {
        get => _credentialsStatus;
        set => this.RaiseAndSetIfChanged(ref _credentialsStatus, value);
    }

    public bool IsSyncing
    {
        get => _isSyncing;
        set => this.RaiseAndSetIfChanged(ref _isSyncing, value);
    }

    public bool SyncDryRun
    {
        get => _syncDryRun;
        set => this.RaiseAndSetIfChanged(ref _syncDryRun, value);
    }

    public bool IsSavingCredentials
    {
        get => _isSavingCredentials;
        set => this.RaiseAndSetIfChanged(ref _isSavingCredentials, value);
    }

    public bool IsSavingMobileSyncSettings
    {
        get => _isSavingMobileSyncSettings;
        set => this.RaiseAndSetIfChanged(ref _isSavingMobileSyncSettings, value);
    }

    public string? EditableAgentId
    {
        get => _editableAgentId;
        set => this.RaiseAndSetIfChanged(ref _editableAgentId, value);
    }

    public string? EditablePassword
    {
        get => _editablePassword;
        set => this.RaiseAndSetIfChanged(ref _editablePassword, value);
    }

    public string? MobileSyncApiUrl
    {
        get => _mobileSyncApiUrl;
        set => this.RaiseAndSetIfChanged(ref _mobileSyncApiUrl, value);
    }

    public string? MobileSyncApiKey
    {
        get => _mobileSyncApiKey;
        set => this.RaiseAndSetIfChanged(ref _mobileSyncApiKey, value);
    }

    public string MobileSyncStatus
    {
        get => _mobileSyncStatus;
        set => this.RaiseAndSetIfChanged(ref _mobileSyncStatus, value);
    }

    public ReactiveCommand<Unit, Unit> CheckPythonCommand { get; }
    public ReactiveCommand<Unit, Unit> SyncLegacyDatabaseCommand { get; }
    public ReactiveCommand<Unit, Unit> ForceSyncLegacyDatabaseCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveCredentialsCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveMobileSyncSettingsCommand { get; }

    private async void LoadSettings()
    {
        DatabasePath = _databaseService.GetDatabasePath();
        ScriptsPath = _pythonService.GetScriptsPath();
        DocumentsPath = _pythonService.GetDocumentsPath();
        BasePath = AppPaths.BaseDirectory;
        AgentId = await _databaseService.GetAgentIdAsync();
        EditableAgentId = AgentId;
        EditablePassword = string.Empty;

        TargetDatabasePath = DatabasePath;
        SourceDatabasePath = ResolveDefaultLegacySourcePath(DocumentsPath);
        LegacySyncStatus = "Select source and target database, then click Sync or Force Full Sync.";
        CredentialsStatus = "Enter Agent ID and password, then click Save Credentials.";
        MobileSyncStatus = "Set API URL and key for mobile sync.";

        var (isInstalled, version) = await _pythonService.CheckPythonInstalledAsync();
        PythonVersion = isInstalled ? version : "Not installed";

        var (_, hasPassword) = await _databaseService.GetCredentialStatusAsync();
        if (hasPassword)
        {
            CredentialsStatus = "Saved credentials found. Update only when needed.";
        }

        var (apiUrl, apiKey) = await _databaseService.GetMobileSyncSettingsAsync();
        MobileSyncApiUrl = apiUrl;
        MobileSyncApiKey = apiKey;
    }

    private async void CheckPython()
    {
        var (isInstalled, version) = await _pythonService.CheckPythonInstalledAsync();
        PythonVersion = isInstalled ? version : "Not installed";
        DocumentsPath = _pythonService.GetDocumentsPath();
        BasePath = AppPaths.BaseDirectory;
    }

    private async Task SyncLegacyDatabaseAsync()
    {
        await RunLegacySyncAsync(forceFull: false);
    }

    private async Task ForceSyncLegacyDatabaseAsync()
    {
        await RunLegacySyncAsync(forceFull: true);
    }

    private async Task RunLegacySyncAsync(bool forceFull)
    {
        if (IsSyncing)
        {
            return;
        }

        var source = (SourceDatabasePath ?? string.Empty).Trim();
        var target = (TargetDatabasePath ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(source))
        {
            LegacySyncStatus = "Choose a source database file.";
            return;
        }

        if (string.IsNullOrWhiteSpace(target))
        {
            LegacySyncStatus = "Choose a target database file.";
            return;
        }

        if (!File.Exists(source))
        {
            LegacySyncStatus = $"Source not found: {source}";
            return;
        }

        if (!File.Exists(target))
        {
            LegacySyncStatus = $"Target not found: {target}";
            return;
        }

        IsSyncing = true;
        LegacySyncStatus = forceFull
            ? "Running force full sync..."
            : (SyncDryRun ? "Running sync preview..." : "Running sync...");

        try
        {
            var (success, output) = await _pythonService.SyncLegacyAccountDetailAsync(
                source,
                target,
                dryRun: forceFull ? false : SyncDryRun,
                preferSourceValues: forceFull,
                forceAslaas: forceFull,
                deactivateMissing: forceFull,
                progressCallback: line =>
                {
                    var trimmed = (line ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(trimmed))
                    {
                        return;
                    }

                    Dispatcher.UIThread.Post(() => LegacySyncStatus = trimmed);
                });

            var summary = BuildSyncSummary(output, success);
            LegacySyncStatus = summary;
        }
        catch (Exception ex)
        {
            LegacySyncStatus = $"Sync failed: {ex.Message}";
        }
        finally
        {
            IsSyncing = false;
        }
    }

    private async Task SaveCredentialsAsync()
    {
        if (IsSavingCredentials)
        {
            return;
        }

        var agentId = (EditableAgentId ?? string.Empty).Trim();
        var password = EditablePassword ?? string.Empty;

        if (string.IsNullOrWhiteSpace(agentId))
        {
            CredentialsStatus = "Agent ID is required.";
            return;
        }

        if (string.IsNullOrWhiteSpace(password))
        {
            CredentialsStatus = "Password is required.";
            return;
        }

        IsSavingCredentials = true;
        CredentialsStatus = "Saving credentials...";

        try
        {
            await _databaseService.SaveCredentialsAsync(agentId, password);
            AgentId = agentId;
            EditablePassword = string.Empty;
            CredentialsStatus = "Credentials saved successfully.";
        }
        catch (Exception ex)
        {
            CredentialsStatus = $"Failed to save credentials: {ex.Message}";
        }
        finally
        {
            IsSavingCredentials = false;
        }
    }

    private static string ResolveDefaultLegacySourcePath(string? documentsPath)
    {
        var desktopFile = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            "database.sqlite");
        if (!string.IsNullOrWhiteSpace(desktopFile) && File.Exists(desktopFile))
        {
            return desktopFile;
        }

        if (!string.IsNullOrWhiteSpace(documentsPath))
        {
            return Path.Combine(documentsPath, "database.sqlite");
        }

        return desktopFile;
    }

    private static string BuildSyncSummary(string output, bool success)
    {
        var lines = (output ?? string.Empty)
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToList();

        if (lines.Count == 0)
        {
            return success ? "Sync completed." : "Sync failed.";
        }

        var summaryLines = lines
            .Where(line =>
                line.StartsWith("Sync committed", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("DRY RUN", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Backup created:", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("- account_detail", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("- rd_accounts", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("ERROR", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (summaryLines.Count == 0)
        {
            return lines.Last();
        }

        return string.Join(Environment.NewLine, summaryLines);
    }

    private async Task SaveMobileSyncSettingsAsync()
    {
        if (IsSavingMobileSyncSettings)
        {
            return;
        }

        var apiUrl = (MobileSyncApiUrl ?? string.Empty).Trim();
        var apiKey = (MobileSyncApiKey ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(apiUrl))
        {
            MobileSyncStatus = "Mobile Sync API URL is required.";
            return;
        }

        if (!Uri.TryCreate(apiUrl, UriKind.Absolute, out _))
        {
            MobileSyncStatus = "Enter a valid API URL (example: https://rd-base.vercel.app).";
            return;
        }

        IsSavingMobileSyncSettings = true;
        MobileSyncStatus = "Saving mobile sync settings...";

        try
        {
            await _databaseService.SaveMobileSyncSettingsAsync(apiUrl, apiKey);
            MobileSyncApiUrl = apiUrl;
            MobileSyncApiKey = apiKey;
            MobileSyncStatus = "Mobile sync settings saved.";
        }
        catch (Exception ex)
        {
            MobileSyncStatus = $"Failed to save mobile sync settings: {ex.Message}";
        }
        finally
        {
            IsSavingMobileSyncSettings = false;
        }
    }
}
