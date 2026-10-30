using System;
using System.Globalization;
using System.Reactive;
using System.Reactive.Threading.Tasks;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Styling;
using Avalonia.Media;
using ReactiveUI;
using AgentBuddy.Services;
using AgentBuddy.Models;

namespace AgentBuddy.ViewModels;

public enum ThemeMode
{
    NeumorphicLight = 0,
    NeumorphicDark = 1,
    ClassicNotion = 2
}

/// <summary>
/// Main window ViewModel - handles navigation and theme switching
/// </summary>
public class MainWindowViewModel : ViewModelBase
{
    private const string DailyGreetingDateKey = "daily_greeting_date";
    private ViewModelBase? _currentView;
    private bool _isDarkTheme;
    private ThemeMode _currentThemeMode = ThemeMode.NeumorphicLight;
    private bool _isSidebarExpanded = true;
    private string _currentViewName = "Dashboard";
    private bool _isLicenseActive;
    private string _licenseStatusText = "Checking license...";

    // Services
    private readonly DatabaseService _databaseService;
    private readonly LicenseService _licenseService;
    private readonly PythonService _pythonService;
    private readonly MetricsCalculator _metricsCalculator;
    private readonly ValidationService _validationService;
    private readonly ReportsService _reportsService;
    private readonly NotificationService _notificationService;
    private readonly MobileSyncService _mobileSyncService;
    private readonly LocalizationService _localizationService;
    private readonly UpdateService _updateService;
    private readonly IPortalAutomationService _portalAutomationService;
    private readonly CancellationTokenSource _updateCts = new();
    private string _latestVersion = string.Empty;
    private bool _isUpdateAvailable;
    private string? _updateReleaseUrl;

    // View Models
    public DashboardViewModel DashboardViewModel { get; }
    public ListManagementViewModel ListManagementViewModel { get; }
    public FixedDepositViewModel FixedDepositViewModel { get; }
    public ReportsViewModel ReportsViewModel { get; }
    public SupportViewModel SupportViewModel { get; }
    public SettingsViewModel SettingsViewModel { get; }
    public NotificationService NotificationService => _notificationService;
    public DatabaseService DatabaseService => _databaseService;
    public LocalizationService LocalizationService => _localizationService;
    public event Action<UpdateCheckResult>? UpdateAvailable;

    public MainWindowViewModel()
    {
        var settings = AppSettings.Load();
        _currentThemeMode = (ThemeMode)Math.Clamp(settings.ThemeMode, 0, 2);
        _isDarkTheme = _currentThemeMode == ThemeMode.NeumorphicDark;

        if (Application.Current is { } app)
        {
            app.RequestedThemeVariant = _isDarkTheme ? ThemeVariant.Dark : ThemeVariant.Light;
        }

        // Initialize services
        _databaseService = new DatabaseService();
        _licenseService = new LicenseService(_databaseService);
        _pythonService = new PythonService(_databaseService);
        _metricsCalculator = new MetricsCalculator(_databaseService);
        _validationService = new ValidationService(_databaseService);
        _reportsService = new ReportsService();
        _notificationService = new NotificationService();
        _mobileSyncService = new MobileSyncService();
        _localizationService = new LocalizationService();
        _updateService = new UpdateService(_databaseService);
        _portalAutomationService = PortalAutomationProvider.Factory?.Invoke(_databaseService) ?? new DesktopPythonPortalAutomationService(_pythonService);

        // Initialize view models
        DashboardViewModel = new DashboardViewModel(_databaseService, _metricsCalculator, _pythonService, _mobileSyncService, _notificationService, _portalAutomationService);
        ListManagementViewModel = new ListManagementViewModel(_databaseService, _validationService, _pythonService, _reportsService, _notificationService, _portalAutomationService);
        FixedDepositViewModel = new FixedDepositViewModel(_databaseService, _notificationService);
        ReportsViewModel = new ReportsViewModel(_reportsService, _pythonService, _notificationService);
        SupportViewModel = new SupportViewModel(_reportsService, _notificationService);
        SettingsViewModel = new SettingsViewModel(_databaseService, _pythonService, _localizationService, _reportsService, _licenseService, _updateService, _notificationService);
        SettingsViewModel.LicenseStateChanged += OnLicenseStateChanged;
        SettingsViewModel.RdCertificateRenewalChanged += OnRdCertificateRenewalChanged;
        SettingsViewModel.ThemeModeChanged += OnThemeModeChanged;

        // Set default view
        _currentView = DashboardViewModel;

        // Setup commands
        NavigateCommand = ReactiveCommand.Create<string>(Navigate);
        ToggleThemeCommand = ReactiveCommand.Create(ToggleTheme);
        ToggleSidebarCommand = ReactiveCommand.Create(ToggleSidebar);

        // Propagate initial theme state.
        DashboardViewModel.IsDarkTheme = IsDarkTheme;
        ListManagementViewModel.IsDarkTheme = IsDarkTheme;
        FixedDepositViewModel.IsDarkTheme = IsDarkTheme;
        ReportsViewModel.IsDarkTheme = IsDarkTheme;
        SupportViewModel.IsDarkTheme = IsDarkTheme;
        SettingsViewModel.IsDarkTheme = IsDarkTheme;
        ApplyThemeResources(_currentThemeMode);
        _ = InitializeLocalizationAsync();
        _ = InitializeLicenseAsync();
        _ = InitializeUpdateChecksAsync();
    }

    /// <summary>
    /// Current view being displayed
    /// </summary>
    public ViewModelBase? CurrentView
    {
        get => _currentView;
        set => this.RaiseAndSetIfChanged(ref _currentView, value);
    }

    /// <summary>
    /// Current theme (true = dark, false = light)
    /// </summary>
    public bool IsDarkTheme
    {
        get => _isDarkTheme;
        set => this.RaiseAndSetIfChanged(ref _isDarkTheme, value);
    }

    public ThemeMode CurrentThemeMode
    {
        get => _currentThemeMode;
        set
        {
            this.RaiseAndSetIfChanged(ref _currentThemeMode, value);
            this.RaisePropertyChanged(nameof(ThemeModeLabel));
            this.RaisePropertyChanged(nameof(ThemeIconKey));
        }
    }

    public string ThemeModeLabel => CurrentThemeMode switch
    {
        ThemeMode.NeumorphicLight => "Modern Light",
        ThemeMode.NeumorphicDark => "Modern Dark",
        ThemeMode.ClassicNotion => "Classic Notion",
        _ => "Modern Light"
    };

    public string ThemeIconKey => CurrentThemeMode switch
    {
        ThemeMode.NeumorphicLight => "Icon_Theme_Sun",
        ThemeMode.NeumorphicDark => "Icon_Theme_Moon",
        ThemeMode.ClassicNotion => "Icon_Theme_Palette",
        _ => "Icon_Theme_Sun"
    };

    /// <summary>
    /// Sidebar expansion state
    /// </summary>
    public bool IsSidebarExpanded
    {
        get => _isSidebarExpanded;
        set => this.RaiseAndSetIfChanged(ref _isSidebarExpanded, value);
    }

    /// <summary>
    /// Current view name for UI indicators
    /// </summary>
    public string CurrentViewName
    {
        get => _currentViewName;
        set => this.RaiseAndSetIfChanged(ref _currentViewName, value);
    }

    public bool IsLicenseActive
    {
        get => _isLicenseActive;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isLicenseActive, value);
            this.RaisePropertyChanged(nameof(IsLicenseLocked));
        }
    }

    public bool IsLicenseLocked => !(IsLicenseActive || OperatingSystem.IsAndroid() || OperatingSystem.IsIOS());

    public string LicenseStatusText
    {
        get => _licenseStatusText;
        private set => this.RaiseAndSetIfChanged(ref _licenseStatusText, value);
    }

    public string LatestVersion
    {
        get => _latestVersion;
        private set => this.RaiseAndSetIfChanged(ref _latestVersion, value);
    }

    public bool IsUpdateAvailable
    {
        get => _isUpdateAvailable;
        private set => this.RaiseAndSetIfChanged(ref _isUpdateAvailable, value);
    }

    public string? UpdateReleaseUrl
    {
        get => _updateReleaseUrl;
        private set => this.RaiseAndSetIfChanged(ref _updateReleaseUrl, value);
    }

    /// <summary>
    /// Command to navigate between views
    /// </summary>
    public ReactiveCommand<string, Unit> NavigateCommand { get; }

    /// <summary>
    /// Command to toggle theme (cycles Neumorphic Light -> Neumorphic Dark -> Classic Notion)
    /// </summary>
    public ReactiveCommand<Unit, Unit> ToggleThemeCommand { get; }

    /// <summary>
    /// Command to toggle sidebar
    /// </summary>
    public ReactiveCommand<Unit, Unit> ToggleSidebarCommand { get; }

    /// <summary>
    /// Navigate to a specific view
    /// </summary>
    private void Navigate(string viewName)
    {
        if (IsLicenseLocked && !string.Equals(viewName, "Settings", StringComparison.OrdinalIgnoreCase))
        {
            CurrentView = SettingsViewModel;
            CurrentViewName = "Settings";
            _notificationService.Warning("License required", "Activate a valid license in Settings to unlock this section.");
            return;
        }

        if (viewName == "Reports")
        {
            _ = ReportsViewModel.LoadTodayReportsAsync();
        }
        else if (viewName == "Dashboard")
        {
            _ = DashboardViewModel.RefreshRdCertificateRenewalAsync();
        }
        else if (viewName == "FixedDeposits")
        {
            _ = FixedDepositViewModel.LoadAccountsAsync();
        }

        CurrentView = viewName switch
        {
            "Dashboard" => DashboardViewModel,
            "Lists" => ListManagementViewModel,
            "FixedDeposits" => FixedDepositViewModel,
            "Reports" => ReportsViewModel,
            "Support" => SupportViewModel,
            "Settings" => SettingsViewModel,
            _ => DashboardViewModel
        };

        CurrentViewName = viewName;
    }

    /// <summary>
    /// Toggle between Neumorphic Light, Neumorphic Dark, and Classic Notion
    /// </summary>
    private void ToggleTheme()
    {
        CurrentThemeMode = CurrentThemeMode switch
        {
            ThemeMode.NeumorphicLight => ThemeMode.NeumorphicDark,
            ThemeMode.NeumorphicDark => ThemeMode.ClassicNotion,
            ThemeMode.ClassicNotion => ThemeMode.NeumorphicLight,
            _ => ThemeMode.NeumorphicLight
        };

        IsDarkTheme = CurrentThemeMode == ThemeMode.NeumorphicDark;

        if (Application.Current is { } app)
        {
            app.RequestedThemeVariant = IsDarkTheme ? ThemeVariant.Dark : ThemeVariant.Light;
        }

        ApplyThemeResources(CurrentThemeMode);
        
        // Save preferred theme mode
        var settings = AppSettings.Load();
        settings.ThemeMode = (int)CurrentThemeMode;
        settings.Save();

        // Apply theme change to all child view models
        DashboardViewModel.IsDarkTheme = IsDarkTheme;
        ListManagementViewModel.IsDarkTheme = IsDarkTheme;
        FixedDepositViewModel.IsDarkTheme = IsDarkTheme;
        ReportsViewModel.IsDarkTheme = IsDarkTheme;
        SupportViewModel.IsDarkTheme = IsDarkTheme;
        SettingsViewModel.IsDarkTheme = IsDarkTheme;
    }

    private void OnThemeModeChanged(ThemeMode mode)
    {
        CurrentThemeMode = mode;
        IsDarkTheme = mode == ThemeMode.NeumorphicDark;

        if (Application.Current is { } app)
        {
            app.RequestedThemeVariant = IsDarkTheme ? ThemeVariant.Dark : ThemeVariant.Light;
        }

        ApplyThemeResources(mode);

        DashboardViewModel.IsDarkTheme = IsDarkTheme;
        ListManagementViewModel.IsDarkTheme = IsDarkTheme;
        FixedDepositViewModel.IsDarkTheme = IsDarkTheme;
        ReportsViewModel.IsDarkTheme = IsDarkTheme;
        SupportViewModel.IsDarkTheme = IsDarkTheme;
        SettingsViewModel.IsDarkTheme = IsDarkTheme;
    }

    public static void ApplyThemeResources(ThemeMode mode)
    {
        if (Application.Current is not { Resources: { } resources })
        {
            return;
        }

        bool isDark = mode == ThemeMode.NeumorphicDark;

        string background;
        string card;
        string sidebar;
        string textPrimary;
        string textSecondary;
        string border;
        string hover;
        string pressed;

        string cardShadow;
        string cardHoverShadow;
        string insetShadow;
        string buttonShadow;
        string buttonPressedShadow;

        if (mode == ThemeMode.NeumorphicLight)
        {
            background = "#F8F9FA";
            card = "#FFFFFF";
            sidebar = "#F3F4F6";
            textPrimary = "#111827";
            textSecondary = "#4B5563";
            border = "#E5E7EB";
            hover = "#F3F4F6";
            pressed = "#E5E7EB";

            cardShadow = "0 1 3 0 #0000000D, 0 1 2 0 #00000008";
            cardHoverShadow = "0 4 12 0 #00000014, 0 2 4 0 #0000000A";
            insetShadow = "0 0 0 0 Transparent";
            buttonShadow = "0 1 2 0 #0000000A";
            buttonPressedShadow = "0 0 0 0 Transparent";
        }
        else if (mode == ThemeMode.NeumorphicDark)
        {
            background = "#0D1117";
            card = "#161B22";
            sidebar = "#0D1117";
            textPrimary = "#F0F6FC";
            textSecondary = "#8B949E";
            border = "#30363D";
            hover = "#21262D";
            pressed = "#282E37";

            cardShadow = "0 2 8 0 #00000040";
            cardHoverShadow = "0 4 16 0 #00000060";
            insetShadow = "0 0 0 0 Transparent";
            buttonShadow = "0 1 2 0 #00000050";
            buttonPressedShadow = "0 0 0 0 Transparent";
        }
        else // Classic Notion
        {
            background = "#F7F6F3";
            card = "#FFFFFF";
            sidebar = "#FBFAF8";
            textPrimary = "#2F3437";
            textSecondary = "#6B7280";
            border = "#E7E5E4";
            hover = "#F1F0EE";
            pressed = "#E8E7E5";

            cardShadow = "0 1 2 0 #0000000E";
            cardHoverShadow = "0 4 8 0 #00000018";
            insetShadow = "0 0 0 0 Transparent";
            buttonShadow = "0 1 2 0 #0000000E";
            buttonPressedShadow = "0 0 0 0 Transparent";
        }

        string successTint = isDark ? "#1E2E26" : "#D9F0E2";
        string warningTint = isDark ? "#382805" : "#FCECC7";
        string warningColor = isDark ? "#FBBF24" : "#B45309";
        string successColor = isDark ? "#34D399" : "#1F8A62";
        string errorColor = isDark ? "#F87171" : "#D94848";
        string infoColor = isDark ? "#60A5FA" : "#2F76E6";
        string dangerTint = isDark ? "#342121" : "#FCDCDC";
        string infoTint = isDark ? "#1A273B" : "#DBE7FA";
        string tintText = isDark ? "#ECECEC" : "#2D3748";
        string listAdvanceTint = isDark ? "#183153" : "#D2E3FC";
        string listAdvanceBorder = isDark ? "#6EA4FF" : "#145BD6";
        string listCatchUpTint = isDark ? "#3A2B12" : "#FAE8BC";
        string listCatchUpBorder = isDark ? "#F1B63C" : "#C98B00";
        string listMixedTint = isDark ? "#15352F" : "#C9EFE6";
        string listMixedBorder = isDark ? "#46C2A1" : "#0B8A6A";
        string listLongOverdueTint = isDark ? "#3E2814" : "#F6D6BA";
        string listLongOverdueBorder = isDark ? "#F09A42" : "#C56000";
        string listPartialTint = isDark ? "#412220" : "#FCD6D2";
        string listPartialBorder = isDark ? "#F08C7D" : "#E16A5A";
        string listLongOverduePartialTint = isDark ? "#4A1C1C" : "#F7BDB5";
        string listLongOverduePartialBorder = isDark ? "#E57373" : "#A63232";
        string listMissingDueTint = isDark ? "#2A2A2A" : "#E2E7EE";
        string listMissingDueBorder = isDark ? "#7A7A7A" : "#9CA3AF";
        string listDuplicateTint = isDark ? "#331E2A" : "#F9D7E8";
        string listDuplicateBorder = isDark ? "#D86AA0" : "#C44582";
        string suggestionBorder = isDark ? "#E78CBC" : "#C44582";

        resources["AppBackgroundBrush"] = new SolidColorBrush(Color.Parse(background));
        resources["AppCardBackgroundBrush"] = new SolidColorBrush(Color.Parse(card));
        resources["AppSidebarBackgroundBrush"] = new SolidColorBrush(Color.Parse(sidebar));
        resources["AppTextPrimaryBrush"] = new SolidColorBrush(Color.Parse(textPrimary));
        resources["AppTextSecondaryBrush"] = new SolidColorBrush(Color.Parse(textSecondary));
        resources["AppBorderBrush"] = new SolidColorBrush(Color.Parse(border));
        resources["AppHoverBrush"] = new SolidColorBrush(Color.Parse(hover));
        resources["AppPressedBrush"] = new SolidColorBrush(Color.Parse(pressed));

        resources["AppCardShadow"] = BoxShadows.Parse(cardShadow);
        resources["AppCardHoverShadow"] = BoxShadows.Parse(cardHoverShadow);
        resources["AppInsetShadow"] = BoxShadows.Parse(insetShadow);
        resources["AppButtonShadow"] = BoxShadows.Parse(buttonShadow);
        resources["AppButtonPressedShadow"] = BoxShadows.Parse(buttonPressedShadow);

        resources["SuccessTintBrush"] = new SolidColorBrush(Color.Parse(successTint));
        resources["WarningTintBrush"] = new SolidColorBrush(Color.Parse(warningTint));
        resources["WarningBrush"] = new SolidColorBrush(Color.Parse(warningColor));
        resources["SuccessBrush"] = new SolidColorBrush(Color.Parse(successColor));
        resources["ErrorBrush"] = new SolidColorBrush(Color.Parse(errorColor));
        resources["InfoBrush"] = new SolidColorBrush(Color.Parse(infoColor));
        resources["DangerTintBrush"] = new SolidColorBrush(Color.Parse(dangerTint));
        resources["InfoTintBrush"] = new SolidColorBrush(Color.Parse(infoTint));
        resources["TintTextBrush"] = new SolidColorBrush(Color.Parse(tintText));
        resources["ListAdvanceTintBrush"] = new SolidColorBrush(Color.Parse(listAdvanceTint));
        resources["ListAdvanceBorderBrush"] = new SolidColorBrush(Color.Parse(listAdvanceBorder));
        resources["ListCatchUpTintBrush"] = new SolidColorBrush(Color.Parse(listCatchUpTint));
        resources["ListCatchUpBorderBrush"] = new SolidColorBrush(Color.Parse(listCatchUpBorder));
        resources["ListMixedTintBrush"] = new SolidColorBrush(Color.Parse(listMixedTint));
        resources["ListMixedBorderBrush"] = new SolidColorBrush(Color.Parse(listMixedBorder));
        resources["ListLongOverdueTintBrush"] = new SolidColorBrush(Color.Parse(listLongOverdueTint));
        resources["ListLongOverdueBorderBrush"] = new SolidColorBrush(Color.Parse(listLongOverdueBorder));
        resources["ListPartialCatchUpTintBrush"] = new SolidColorBrush(Color.Parse(listPartialTint));
        resources["ListPartialCatchUpBorderBrush"] = new SolidColorBrush(Color.Parse(listPartialBorder));
        resources["ListLongOverduePartialTintBrush"] = new SolidColorBrush(Color.Parse(listLongOverduePartialTint));
        resources["ListLongOverduePartialBorderBrush"] = new SolidColorBrush(Color.Parse(listLongOverduePartialBorder));
        resources["ListMissingDueTintBrush"] = new SolidColorBrush(Color.Parse(listMissingDueTint));
        resources["ListMissingDueBorderBrush"] = new SolidColorBrush(Color.Parse(listMissingDueBorder));
        resources["ListDuplicateTintBrush"] = new SolidColorBrush(Color.Parse(listDuplicateTint));
        resources["ListDuplicateBorderBrush"] = new SolidColorBrush(Color.Parse(listDuplicateBorder));
        resources["SuggestionBorderBrush"] = new SolidColorBrush(Color.Parse(suggestionBorder));
    }

    private async Task InitializeUpdateChecksAsync()
    {
        await CheckAndNotifyUpdatesAsync(force: false);
        _ = RunUpdateLoopAsync();
    }

    private void OnRdCertificateRenewalChanged(object? sender, EventArgs e)
    {
        _ = DashboardViewModel.RefreshRdCertificateRenewalAsync();
    }

    private async Task RunUpdateLoopAsync()
    {
        while (!_updateCts.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(UpdateService.DefaultInterval, _updateCts.Token);
            }
            catch (TaskCanceledException)
            {
                break;
            }

            await CheckAndNotifyUpdatesAsync(force: false);
        }
    }

    private async Task CheckAndNotifyUpdatesAsync(bool force)
    {
        var result = await _updateService.CheckForUpdatesAsync(force, _updateCts.Token);
        if (result is null)
        {
            return;
        }

        LatestVersion = result.LatestVersion;
        IsUpdateAvailable = result.IsUpdateAvailable;
        UpdateReleaseUrl = result.ReleaseUrl;
        DashboardViewModel.ApplyUpdateInfo(result.IsUpdateAvailable, result.LatestVersion, result.ReleaseUrl);
        SettingsViewModel.LatestVersion = result.LatestVersion;
        SettingsViewModel.IsUpdateAvailable = result.IsUpdateAvailable;
        SettingsViewModel.UpdateCheckStatus = result.IsUpdateAvailable
            ? $"Update available: v{result.LatestVersion}."
            : "You're up to date.";

        if (result.Notified)
        {
            UpdateAvailable?.Invoke(result);
        }
    }

    public void StopUpdateChecks()
    {
        if (_updateCts.IsCancellationRequested)
        {
            return;
        }

        _updateCts.Cancel();
    }

    /// <summary>
    /// Toggle sidebar expansion
    /// </summary>
    private void ToggleSidebar()
    {
        IsSidebarExpanded = !IsSidebarExpanded;
    }

    private async Task InitializeLocalizationAsync()
    {
        await _localizationService.InitializeAsync(_databaseService);
        SettingsViewModel.SyncSelectedLanguageFromService();
    }

    private async Task InitializeLicenseAsync()
    {
        var state = await _licenseService.GetCurrentStatusAsync(validateOnline: false);
        ApplyLicenseState(state, notifyWhenLocked: false);
    }

    private void OnLicenseStateChanged(object? sender, LicenseStatus state)
    {
        ApplyLicenseState(state, notifyWhenLocked: false);
    }

    private void ApplyLicenseState(LicenseStatus state, bool notifyWhenLocked)
    {
        IsLicenseActive = state.IsActive || OperatingSystem.IsAndroid() || OperatingSystem.IsIOS();
        LicenseStatusText = BuildLicenseBadgeText(state);

        if (!IsLicenseActive)
        {
            CurrentView = SettingsViewModel;
            CurrentViewName = "Settings";
            if (notifyWhenLocked)
            {
                _notificationService.Warning("License required", "Activate a valid license in Settings.");
            }
        }
    }

    private static string BuildLicenseBadgeText(LicenseStatus state)
    {
        if (state.ExpiresAtUtc.HasValue)
        {
            var localExpiry = state.ExpiresAtUtc.Value.ToLocalTime();
            var label = state.IsActive ? "Next renewal" : "Expired on";
            return $"{label}: {localExpiry:dd MMM yyyy}";
        }

        return string.IsNullOrWhiteSpace(state.Message) ? "License status unavailable." : state.Message;
    }

    public async Task<bool> ShouldShowDailyGreetingAsync()
    {
        var saved = await _databaseService.GetAppSettingAsync(DailyGreetingDateKey);
        var today = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return !string.Equals(saved, today, StringComparison.Ordinal);
    }

    public Task MarkDailyGreetingShownAsync()
    {
        var today = DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return _databaseService.SaveAppSettingAsync(DailyGreetingDateKey, today);
    }

    public string GetDailyGreetingTitle()
    {
        var hour = DateTime.Now.Hour;
        if (hour < 12)
        {
            return "Good Morning!";
        }

        if (hour < 17)
        {
            return "Good Afternoon!";
        }

        return "Good Evening!";
    }

    public string GetDailyGreetingMessage()
    {
        return "Would you like to update the master list now?";
    }

    public async Task RunDatabaseUpdateAsync()
    {
        await DashboardViewModel.UpdateDatabaseCommand.Execute().ToTask();
    }
}
