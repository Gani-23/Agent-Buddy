using System;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Threading;
using ReactiveUI;
using AgentBuddy.Services;
using AgentBuddy.Models;
using System.Collections.ObjectModel;
using System.Collections.Generic;

namespace AgentBuddy.ViewModels;

/// <summary>
/// Settings ViewModel - manages app settings and configuration
/// </summary>
public class SettingsViewModel : ViewModelBase
{
    private readonly DatabaseService _databaseService;
    private readonly PythonService _pythonService;
    private readonly LocalizationService _localizationService;
    private readonly ReportsService _reportsService;
    private readonly LicenseService _licenseService;
    private readonly UpdateService _updateService;
    private readonly NotificationService? _notificationService;

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
    private string _aslaasUpdateStatus = string.Empty;
    private bool _isSyncing;
    private bool _isUpdatingMissingAslaas;
    private bool _syncDryRun;
    private bool _isSavingCredentials;
    private bool _isSavingMobileSyncSettings;
    private bool _isSavingLanguageSettings;
    private string? _editableAgentId;
    private string? _editablePassword;
    private string? _mobileSyncApiUrl;
    private string? _mobileSyncApiKey;
    private string _languageStatus = string.Empty;
    private LanguageOption? _selectedLanguage;
    private string _printerStatus = string.Empty;
    private bool _isSavingPrinterSettings;
    private string? _selectedDefaultPrinter;
    private const string PreferredBrowserSettingKey = "preferred_browser";
    private string _browserStatus = string.Empty;
    private bool _isSavingBrowserSettings;
    private string? _selectedBrowser;
    private string? _licenseServerUrl;
    private string? _licenseAppId;
    private string? _licenseToken;
    private string _licenseStatus = string.Empty;
    private string _licenseSummary = string.Empty;
    private bool _isProcessingLicense;
    private string _currentVersion = string.Empty;
    private string _updateCheckStatus = string.Empty;
    private bool _isCheckingUpdates;

    public SettingsViewModel(
        DatabaseService databaseService,
        PythonService pythonService,
        LocalizationService localizationService,
        ReportsService reportsService,
        LicenseService licenseService,
        UpdateService updateService,
        NotificationService? notificationService = null)
    {
        _databaseService = databaseService;
        _pythonService = pythonService;
        _localizationService = localizationService;
        _reportsService = reportsService;
        _licenseService = licenseService;
        _updateService = updateService;
        _notificationService = notificationService;

        LanguageOptions = new ObservableCollection<LanguageOption>(_localizationService.AvailableLanguages);
        _selectedLanguage = LanguageOptions.FirstOrDefault(option =>
            string.Equals(option.Code, _localizationService.CurrentLanguageCode, StringComparison.OrdinalIgnoreCase))
            ?? LanguageOptions.FirstOrDefault(option => option.Code == "en");

        CheckPythonCommand = ReactiveCommand.Create(CheckPython);
        SyncLegacyDatabaseCommand = ReactiveCommand.CreateFromTask(SyncLegacyDatabaseAsync);
        ForceSyncLegacyDatabaseCommand = ReactiveCommand.CreateFromTask(ForceSyncLegacyDatabaseAsync);
        SaveCredentialsCommand = ReactiveCommand.CreateFromTask(SaveCredentialsAsync);
        SaveMobileSyncSettingsCommand = ReactiveCommand.CreateFromTask(SaveMobileSyncSettingsAsync);
        SaveLanguageCommand = ReactiveCommand.CreateFromTask(SaveLanguageAsync);
        UpdateMissingAslaasAllCommand = ReactiveCommand.CreateFromTask(UpdateMissingAslaasAllAsync);
        ForceUpdateAllAslaasCommand = ReactiveCommand.CreateFromTask(ForceUpdateAllAslaasAsync);
        RefreshPrintersCommand = ReactiveCommand.CreateFromTask(LoadPrintersAsync);
        SaveDefaultPrinterCommand = ReactiveCommand.CreateFromTask(SaveDefaultPrinterAsync);
        SaveBrowserSettingsCommand = ReactiveCommand.CreateFromTask(SaveBrowserSettingsAsync);
        ActivateLicenseCommand = ReactiveCommand.CreateFromTask(ActivateLicenseAsync);
        ValidateStoredLicenseCommand = ReactiveCommand.CreateFromTask(ValidateStoredLicenseAsync);
        ClearLicenseCommand = ReactiveCommand.CreateFromTask(ClearLicenseAsync);
        CheckUpdatesCommand = ReactiveCommand.CreateFromTask(CheckForUpdatesAsync);

        // Load initial data
        LoadSettings();
        CurrentVersion = UpdateService.CurrentVersion;
        UpdateCheckStatus = "Check for updates to see if a newer version is available.";
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

    public bool IsUpdatingMissingAslaas
    {
        get => _isUpdatingMissingAslaas;
        set => this.RaiseAndSetIfChanged(ref _isUpdatingMissingAslaas, value);
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

    public bool IsSavingLanguageSettings
    {
        get => _isSavingLanguageSettings;
        set => this.RaiseAndSetIfChanged(ref _isSavingLanguageSettings, value);
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

    public string AslaasUpdateStatus
    {
        get => _aslaasUpdateStatus;
        set => this.RaiseAndSetIfChanged(ref _aslaasUpdateStatus, value);
    }

    public ObservableCollection<LanguageOption> LanguageOptions { get; }

    public LanguageOption? SelectedLanguage
    {
        get => _selectedLanguage;
        set => this.RaiseAndSetIfChanged(ref _selectedLanguage, value);
    }

    public ObservableCollection<string> PrinterOptions { get; } = new();
    public ObservableCollection<string> BrowserOptions { get; } = new();

    public string? SelectedDefaultPrinter
    {
        get => _selectedDefaultPrinter;
        set => this.RaiseAndSetIfChanged(ref _selectedDefaultPrinter, value);
    }

    public string PrinterStatus
    {
        get => _printerStatus;
        set => this.RaiseAndSetIfChanged(ref _printerStatus, value);
    }

    public bool IsSavingPrinterSettings
    {
        get => _isSavingPrinterSettings;
        set => this.RaiseAndSetIfChanged(ref _isSavingPrinterSettings, value);
    }

    public bool HasPrinterOptions => PrinterOptions.Count > 0;
    public bool HasBrowserOptions => BrowserOptions.Count > 0;

    public string? SelectedBrowser
    {
        get => _selectedBrowser;
        set => this.RaiseAndSetIfChanged(ref _selectedBrowser, value);
    }

    public string BrowserStatus
    {
        get => _browserStatus;
        set => this.RaiseAndSetIfChanged(ref _browserStatus, value);
    }

    public bool IsSavingBrowserSettings
    {
        get => _isSavingBrowserSettings;
        set => this.RaiseAndSetIfChanged(ref _isSavingBrowserSettings, value);
    }

    public string LanguageStatus
    {
        get => _languageStatus;
        set => this.RaiseAndSetIfChanged(ref _languageStatus, value);
    }

    public string? LicenseServerUrl
    {
        get => _licenseServerUrl;
        set => this.RaiseAndSetIfChanged(ref _licenseServerUrl, value);
    }

    public string? LicenseAppId
    {
        get => _licenseAppId;
        set => this.RaiseAndSetIfChanged(ref _licenseAppId, value);
    }

    public string? LicenseToken
    {
        get => _licenseToken;
        set => this.RaiseAndSetIfChanged(ref _licenseToken, value);
    }

    public string LicenseStatus
    {
        get => _licenseStatus;
        set => this.RaiseAndSetIfChanged(ref _licenseStatus, value);
    }

    public string LicenseSummary
    {
        get => _licenseSummary;
        set => this.RaiseAndSetIfChanged(ref _licenseSummary, value);
    }

    public string CurrentVersion
    {
        get => _currentVersion;
        set => this.RaiseAndSetIfChanged(ref _currentVersion, value);
    }

    public string UpdateCheckStatus
    {
        get => _updateCheckStatus;
        set => this.RaiseAndSetIfChanged(ref _updateCheckStatus, value);
    }

    public bool IsCheckingUpdates
    {
        get => _isCheckingUpdates;
        set => this.RaiseAndSetIfChanged(ref _isCheckingUpdates, value);
    }

    public bool IsProcessingLicense
    {
        get => _isProcessingLicense;
        set => this.RaiseAndSetIfChanged(ref _isProcessingLicense, value);
    }

    public ReactiveCommand<Unit, Unit> CheckPythonCommand { get; }
    public ReactiveCommand<Unit, Unit> SyncLegacyDatabaseCommand { get; }
    public ReactiveCommand<Unit, Unit> ForceSyncLegacyDatabaseCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveCredentialsCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveMobileSyncSettingsCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveLanguageCommand { get; }
    public ReactiveCommand<Unit, Unit> UpdateMissingAslaasAllCommand { get; }
    public ReactiveCommand<Unit, Unit> ForceUpdateAllAslaasCommand { get; }
    public ReactiveCommand<Unit, Unit> RefreshPrintersCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveDefaultPrinterCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveBrowserSettingsCommand { get; }
    public ReactiveCommand<Unit, Unit> ActivateLicenseCommand { get; }
    public ReactiveCommand<Unit, Unit> ValidateStoredLicenseCommand { get; }
    public ReactiveCommand<Unit, Unit> ClearLicenseCommand { get; }
    public ReactiveCommand<Unit, Unit> CheckUpdatesCommand { get; }
    public event EventHandler<LicenseStatus>? LicenseStateChanged;

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
        AslaasUpdateStatus = "Update missing ASLAAS numbers, or force update all active accounts.";
        LanguageStatus = "Select your preferred language, then click Apply Language.";
        LicenseStatus = "Enter token and activate license.";
        LicenseSummary = "No active license.";

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

        var savedLanguage = _localizationService.NormalizeLanguageCode(
            await _databaseService.GetAppSettingAsync(LocalizationService.LanguageSettingKey));
        SelectedLanguage = LanguageOptions.FirstOrDefault(option =>
                               string.Equals(option.Code, savedLanguage, StringComparison.OrdinalIgnoreCase))
                           ?? LanguageOptions.FirstOrDefault(option => option.Code == "en");

        await LoadPrintersAsync();
        await LoadBrowserSettingsAsync();
        await LoadLicenseSettingsAsync();
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

    private async Task LoadPrintersAsync()
    {
        try
        {
            PrinterStatus = "Loading printers...";
            var available = await _reportsService.GetAvailablePrintersAsync();
            var effective = (await _reportsService.GetEffectiveDefaultPrinterAsync()).Trim();

            PrinterOptions.Clear();
            foreach (var printer in available)
            {
                if (!string.IsNullOrWhiteSpace(printer))
                {
                    PrinterOptions.Add(printer);
                }
            }

            if (!string.IsNullOrWhiteSpace(effective) &&
                PrinterOptions.All(item => !string.Equals(item, effective, StringComparison.OrdinalIgnoreCase)))
            {
                PrinterOptions.Insert(0, effective);
            }

            if (!string.IsNullOrWhiteSpace(effective))
            {
                SelectedDefaultPrinter = effective;
                PrinterStatus = $"Current default printer: {effective}";
            }
            else if (PrinterOptions.Count > 0)
            {
                SelectedDefaultPrinter = PrinterOptions[0];
                PrinterStatus = "Select a printer and click Save Default Printer.";
            }
            else
            {
                SelectedDefaultPrinter = string.Empty;
                PrinterStatus = "No printers detected on this system.";
            }
        }
        catch (Exception ex)
        {
            PrinterStatus = $"Failed to load printers: {ex.Message}";
        }
        finally
        {
            this.RaisePropertyChanged(nameof(HasPrinterOptions));
        }
    }

    private async Task SaveDefaultPrinterAsync()
    {
        if (IsSavingPrinterSettings)
        {
            return;
        }

        var printer = (SelectedDefaultPrinter ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(printer))
        {
            PrinterStatus = "Select a printer first.";
            return;
        }

        IsSavingPrinterSettings = true;
        PrinterStatus = "Saving default printer...";
        try
        {
            var (success, message) = await _reportsService.SetDefaultPrinterAsync(printer);
            PrinterStatus = message;
            if (success)
            {
                await LoadPrintersAsync();
            }
        }
        catch (Exception ex)
        {
            PrinterStatus = $"Failed to save default printer: {ex.Message}";
        }
        finally
        {
            IsSavingPrinterSettings = false;
        }
    }

    private async Task LoadBrowserSettingsAsync()
    {
        try
        {
            BrowserOptions.Clear();
            foreach (var option in GetSupportedBrowserOptions())
            {
                BrowserOptions.Add(option);
            }

            var savedToken = NormalizeBrowserToken(
                await _databaseService.GetAppSettingAsync(PreferredBrowserSettingKey));
            var desiredLabel = BrowserLabelFromToken(savedToken);

            if (BrowserOptions.Count == 0)
            {
                SelectedBrowser = "Chrome";
                BrowserStatus = "No browser options detected. Chrome will be used.";
            }
            else if (BrowserOptions.Any(option => string.Equals(option, desiredLabel, StringComparison.OrdinalIgnoreCase)))
            {
                SelectedBrowser = desiredLabel;
                BrowserStatus = $"Current automation browser: {desiredLabel}.";
            }
            else
            {
                SelectedBrowser = BrowserOptions[0];
                BrowserStatus = $"Saved browser is not supported on this OS. Using {SelectedBrowser}.";
            }
        }
        catch (Exception ex)
        {
            SelectedBrowser = "Chrome";
            BrowserStatus = $"Failed to load browser setting: {ex.Message}";
        }
        finally
        {
            this.RaisePropertyChanged(nameof(HasBrowserOptions));
        }
    }

    private async Task SaveBrowserSettingsAsync()
    {
        if (IsSavingBrowserSettings)
        {
            return;
        }

        var selectedLabel = (SelectedBrowser ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(selectedLabel))
        {
            BrowserStatus = "Select a browser first.";
            return;
        }

        IsSavingBrowserSettings = true;
        BrowserStatus = "Saving browser setting...";

        try
        {
            var token = NormalizeBrowserToken(selectedLabel);
            await _databaseService.SaveAppSettingAsync(PreferredBrowserSettingKey, token);
            SelectedBrowser = BrowserLabelFromToken(token);
            BrowserStatus = $"Automation browser saved: {SelectedBrowser}.";
        }
        catch (Exception ex)
        {
            BrowserStatus = $"Failed to save browser setting: {ex.Message}";
        }
        finally
        {
            IsSavingBrowserSettings = false;
        }
    }

    private async Task LoadLicenseSettingsAsync()
    {
        try
        {
            var settings = await _licenseService.GetLicenseSettingsAsync();
            LicenseServerUrl = settings.ServerUrl;
            LicenseAppId = settings.AppId;

            var state = await _licenseService.GetCurrentStatusAsync(validateOnline: false);
            LicenseSummary = BuildLicenseSummary(state);
            LicenseStatus = state.Message;
            LicenseStateChanged?.Invoke(this, state);
        }
        catch (Exception ex)
        {
            LicenseStatus = $"Failed to load license settings: {ex.Message}";
            LicenseSummary = "No active license.";
        }
    }

    private async Task ActivateLicenseAsync()
    {
        if (IsProcessingLicense)
        {
            return;
        }

        IsProcessingLicense = true;
        LicenseStatus = "Activating license...";
        try
        {
            var result = await _licenseService.ActivateLicenseAsync(LicenseServerUrl, LicenseAppId, LicenseToken);
            LicenseStatus = result.Message;
            LicenseSummary = BuildLicenseSummary(result.Status);
            if (result.Success)
            {
                LicenseToken = string.Empty;
                LicenseServerUrl = result.Status.ServerUrl;
                LicenseAppId = result.Status.AppId;
            }

            LicenseStateChanged?.Invoke(this, result.Status);
        }
        catch (Exception ex)
        {
            LicenseStatus = $"Failed to activate license: {ex.Message}";
        }
        finally
        {
            IsProcessingLicense = false;
        }
    }

    private async Task ValidateStoredLicenseAsync()
    {
        if (IsProcessingLicense)
        {
            return;
        }

        IsProcessingLicense = true;
        LicenseStatus = "Validating stored license...";
        try
        {
            var result = await _licenseService.ValidateStoredLicenseAsync();
            LicenseStatus = result.Message;
            LicenseSummary = BuildLicenseSummary(result.Status);
            LicenseStateChanged?.Invoke(this, result.Status);
        }
        catch (Exception ex)
        {
            LicenseStatus = $"License validation failed: {ex.Message}";
        }
        finally
        {
            IsProcessingLicense = false;
        }
    }

    private async Task ClearLicenseAsync()
    {
        if (IsProcessingLicense)
        {
            return;
        }

        IsProcessingLicense = true;
        LicenseStatus = "Clearing license...";
        try
        {
            await _licenseService.ClearLicenseAsync();
            LicenseToken = string.Empty;
            LicenseSummary = "No active license.";
            LicenseStatus = "License cleared.";
            LicenseStateChanged?.Invoke(this, new LicenseStatus
            {
                IsActive = false,
                Message = "License cleared.",
            });
        }
        catch (Exception ex)
        {
            LicenseStatus = $"Failed to clear license: {ex.Message}";
        }
        finally
        {
            IsProcessingLicense = false;
        }
    }

    private static string BuildLicenseSummary(LicenseStatus state)
    {
        if (!state.IsActive)
        {
            return string.IsNullOrWhiteSpace(state.Message) ? "No active license." : state.Message;
        }

        var expires = state.ExpiresAtUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm zzz") ?? "unknown";
        var validated = state.LastValidatedAtUtc?.ToLocalTime().ToString("yyyy-MM-dd HH:mm zzz") ?? "not yet";
        var owner = string.IsNullOrWhiteSpace(state.Subject) ? "n/a" : state.Subject;
        return $"App: {state.AppId} | Expires: {expires} | Last check: {validated} | Subject: {owner}";
    }

    private static IReadOnlyList<string> GetSupportedBrowserOptions()
    {
        var options = new List<string> { "Chrome" };

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            options.Add("Edge");
            options.Add("Internet Explorer");
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            options.Add("Safari");
            if (IsMacAppInstalled("Microsoft Edge.app"))
            {
                options.Add("Edge");
            }
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            options.Add("Edge");
        }

        return options;
    }

    private static bool IsMacAppInstalled(string appBundleName)
    {
        var systemPath = Path.Combine("/Applications", appBundleName);
        if (Directory.Exists(systemPath))
        {
            return true;
        }

        var userApps = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Applications",
            appBundleName);
        return Directory.Exists(userApps);
    }

    private static string BrowserLabelFromToken(string token)
    {
        return token switch
        {
            "edge" => "Edge",
            "ie" => "Internet Explorer",
            "safari" => "Safari",
            _ => "Chrome"
        };
    }

    private static string NormalizeBrowserToken(string? raw)
    {
        var value = (raw ?? string.Empty).Trim().ToLowerInvariant();
        return value switch
        {
            "edge" => "edge",
            "internet explorer" => "ie",
            "internet_explorer" => "ie",
            "ie" => "ie",
            "safari" => "safari",
            _ => "chrome"
        };
    }

    public void SyncSelectedLanguageFromService()
    {
        SelectedLanguage = LanguageOptions.FirstOrDefault(option =>
                               string.Equals(option.Code, _localizationService.CurrentLanguageCode, StringComparison.OrdinalIgnoreCase))
                           ?? LanguageOptions.FirstOrDefault(option => option.Code == "en");
    }

    private async Task SaveLanguageAsync()
    {
        if (IsSavingLanguageSettings)
        {
            return;
        }

        var language = SelectedLanguage;
        if (language == null)
        {
            LanguageStatus = "Choose a language first.";
            return;
        }

        IsSavingLanguageSettings = true;
        LanguageStatus = "Applying language...";

        try
        {
            await _localizationService.SetLanguageAsync(language.Code, _databaseService);
            LanguageStatus = "Language updated successfully.";
        }
        catch (Exception ex)
        {
            LanguageStatus = $"Failed to update language: {ex.Message}";
        }
        finally
        {
            IsSavingLanguageSettings = false;
        }
    }

    private async Task UpdateMissingAslaasAllAsync()
    {
        await RunAslaasUpdateAsync(forceAllActiveAccounts: false);
    }

    private async Task ForceUpdateAllAslaasAsync()
    {
        await RunAslaasUpdateAsync(forceAllActiveAccounts: true);
    }

    private async Task RunAslaasUpdateAsync(bool forceAllActiveAccounts)
    {
        if (IsUpdatingMissingAslaas)
        {
            return;
        }

        IsUpdatingMissingAslaas = true;
        AslaasUpdateStatus = forceAllActiveAccounts
            ? "Preparing force update for all active accounts..."
            : "Checking active accounts with missing ASLAAS...";

        try
        {
            var accountsToUpdate = forceAllActiveAccounts
                ? await _databaseService.GetAllActiveAslaasAccountsAsync()
                : await _databaseService.GetMissingAslaasAccountsAsync(activeOnly: true);

            if (accountsToUpdate.Count == 0)
            {
                AslaasUpdateStatus = forceAllActiveAccounts
                    ? "No active accounts found for ASLAAS force update."
                    : "No active accounts are missing ASLAAS.";
                return;
            }

            var (isPythonInstalled, _) = await _pythonService.CheckPythonInstalledAsync();
            if (!isPythonInstalled)
            {
                AslaasUpdateStatus = "Python is not installed. Install Python 3.x and retry.";
                return;
            }

            AslaasUpdateStatus = forceAllActiveAccounts
                ? $"Found {accountsToUpdate.Count} active account(s). Opening portal for forced ASLAAS update..."
                : $"Found {accountsToUpdate.Count} account(s). Opening portal for ASLAAS update...";
            var (success, output) = await _pythonService.UpdateMissingAslaasAsync(
                accountsToUpdate,
                line =>
                {
                    var trimmed = (line ?? string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(trimmed))
                    {
                        return;
                    }

                    Dispatcher.UIThread.Post(() => AslaasUpdateStatus = trimmed);
                });

            if (!success)
            {
                var reason = GetFirstMeaningfulLine(output);
                AslaasUpdateStatus = $"ASLAAS update failed: {reason}";
                return;
            }

            await _databaseService.SaveAslaasUpdatesAsync(accountsToUpdate);
            AslaasUpdateStatus = forceAllActiveAccounts
                ? $"Force-updated {accountsToUpdate.Count} active account(s). Local ASLAAS values refreshed."
                : $"Updated {accountsToUpdate.Count} account(s). Missing ASLAAS list is now cleared locally.";
        }
        catch (Exception ex)
        {
            AslaasUpdateStatus = $"ASLAAS update failed: {ex.Message}";
        }
        finally
        {
            IsUpdatingMissingAslaas = false;
        }
    }

    private async Task CheckForUpdatesAsync()
    {
        if (IsCheckingUpdates)
        {
            return;
        }

        IsCheckingUpdates = true;
        UpdateCheckStatus = "Checking for updates...";

        try
        {
            var result = await _updateService.CheckForUpdatesAsync(force: true);
            if (result is null)
            {
                UpdateCheckStatus = "Update check skipped.";
                return;
            }

            if (result.IsUpdateAvailable)
            {
                UpdateCheckStatus = $"Update available: v{result.LatestVersion}.";
                _notificationService?.Info("Update available", $"Version {result.LatestVersion} is available. Download from GitHub Releases.");
            }
            else
            {
                UpdateCheckStatus = "You're up to date.";
                _notificationService?.Success("Up to date", "You are already on the latest version.");
            }
        }
        catch (Exception ex)
        {
            UpdateCheckStatus = $"Update check failed: {ex.Message}";
            _notificationService?.Error("Update check failed", ex.Message);
        }
        finally
        {
            IsCheckingUpdates = false;
        }
    }

    private static string GetFirstMeaningfulLine(string output)
    {
        if (string.IsNullOrWhiteSpace(output))
        {
            return "Unknown error.";
        }

        var firstLine = output
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .FirstOrDefault(line => !string.IsNullOrWhiteSpace(line));

        return string.IsNullOrWhiteSpace(firstLine) ? "Unknown error." : firstLine;
    }
}
