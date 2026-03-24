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

/// <summary>
/// Main window ViewModel - handles navigation and theme switching
/// </summary>
public class MainWindowViewModel : ViewModelBase
{
    private const string DailyGreetingDateKey = "daily_greeting_date";
    private ViewModelBase? _currentView;
    private bool _isDarkTheme;
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
    private readonly CancellationTokenSource _updateCts = new();

    // View Models
    public DashboardViewModel DashboardViewModel { get; }
    public ListManagementViewModel ListManagementViewModel { get; }
    public ReportsViewModel ReportsViewModel { get; }
    public SupportViewModel SupportViewModel { get; }
    public SettingsViewModel SettingsViewModel { get; }
    public NotificationService NotificationService => _notificationService;

    public MainWindowViewModel()
    {
        if (Application.Current is { } app)
        {
            _isDarkTheme = app.ActualThemeVariant == ThemeVariant.Dark;
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

        // Initialize view models
        DashboardViewModel = new DashboardViewModel(_databaseService, _metricsCalculator, _pythonService, _mobileSyncService, _notificationService);
        ListManagementViewModel = new ListManagementViewModel(_databaseService, _validationService, _pythonService, _reportsService, _notificationService);
        ReportsViewModel = new ReportsViewModel(_reportsService, _pythonService, _notificationService);
        SupportViewModel = new SupportViewModel(_reportsService, _notificationService);
        SettingsViewModel = new SettingsViewModel(_databaseService, _pythonService, _localizationService, _reportsService, _licenseService);
        SettingsViewModel.LicenseStateChanged += OnLicenseStateChanged;

        // Set default view
        _currentView = DashboardViewModel;

        // Setup commands
        NavigateCommand = ReactiveCommand.Create<string>(Navigate);
        ToggleThemeCommand = ReactiveCommand.Create(ToggleTheme);
        ToggleSidebarCommand = ReactiveCommand.Create(ToggleSidebar);

        // Propagate initial theme state.
        DashboardViewModel.IsDarkTheme = IsDarkTheme;
        ListManagementViewModel.IsDarkTheme = IsDarkTheme;
        ReportsViewModel.IsDarkTheme = IsDarkTheme;
        SupportViewModel.IsDarkTheme = IsDarkTheme;
        SettingsViewModel.IsDarkTheme = IsDarkTheme;
        ApplyThemeResources(IsDarkTheme);
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

    public bool IsLicenseLocked => !IsLicenseActive;

    public string LicenseStatusText
    {
        get => _licenseStatusText;
        private set => this.RaiseAndSetIfChanged(ref _licenseStatusText, value);
    }

    /// <summary>
    /// Command to navigate between views
    /// </summary>
    public ReactiveCommand<string, Unit> NavigateCommand { get; }

    /// <summary>
    /// Command to toggle theme
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

        CurrentView = viewName switch
        {
            "Dashboard" => DashboardViewModel,
            "Lists" => ListManagementViewModel,
            "Reports" => ReportsViewModel,
            "Support" => SupportViewModel,
            "Settings" => SettingsViewModel,
            _ => DashboardViewModel
        };

        CurrentViewName = viewName;
    }

    /// <summary>
    /// Toggle between light and dark theme
    /// </summary>
    private void ToggleTheme()
    {
        IsDarkTheme = !IsDarkTheme;

        if (Application.Current is { } app)
        {
            app.RequestedThemeVariant = IsDarkTheme ? ThemeVariant.Dark : ThemeVariant.Light;
        }

        ApplyThemeResources(IsDarkTheme);
        
        // Apply theme change to all child view models
        DashboardViewModel.IsDarkTheme = IsDarkTheme;
        ListManagementViewModel.IsDarkTheme = IsDarkTheme;
        ReportsViewModel.IsDarkTheme = IsDarkTheme;
        SupportViewModel.IsDarkTheme = IsDarkTheme;
        SettingsViewModel.IsDarkTheme = IsDarkTheme;
    }

    private static void ApplyThemeResources(bool isDark)
    {
        if (Application.Current is not { Resources: { } resources })
        {
            return;
        }

        string background = isDark ? "#171717" : "#F7F6F3";
        string card = isDark ? "#202020" : "#FFFFFF";
        string sidebar = isDark ? "#1B1B1B" : "#FBFAF8";
        string textPrimary = isDark ? "#ECECEC" : "#2F3437";
        string textSecondary = isDark ? "#A3A3A3" : "#6B7280";
        string border = isDark ? "#333333" : "#E7E5E4";
        string hover = isDark ? "#2A2A2A" : "#F1F0EE";
        string pressed = isDark ? "#353535" : "#E8E7E5";

        string successTint = isDark ? "#1E2E26" : "#E7F4EC";
        string warningTint = isDark ? "#332A17" : "#FFF4DC";
        string dangerTint = isDark ? "#342121" : "#FDECEC";
        string infoTint = isDark ? "#1A273B" : "#EAF2FF";

        resources["AppBackgroundBrush"] = new SolidColorBrush(Color.Parse(background));
        resources["AppCardBackgroundBrush"] = new SolidColorBrush(Color.Parse(card));
        resources["AppSidebarBackgroundBrush"] = new SolidColorBrush(Color.Parse(sidebar));
        resources["AppTextPrimaryBrush"] = new SolidColorBrush(Color.Parse(textPrimary));
        resources["AppTextSecondaryBrush"] = new SolidColorBrush(Color.Parse(textSecondary));
        resources["AppBorderBrush"] = new SolidColorBrush(Color.Parse(border));
        resources["AppHoverBrush"] = new SolidColorBrush(Color.Parse(hover));
        resources["AppPressedBrush"] = new SolidColorBrush(Color.Parse(pressed));

        resources["SuccessTintBrush"] = new SolidColorBrush(Color.Parse(successTint));
        resources["WarningTintBrush"] = new SolidColorBrush(Color.Parse(warningTint));
        resources["DangerTintBrush"] = new SolidColorBrush(Color.Parse(dangerTint));
        resources["InfoTintBrush"] = new SolidColorBrush(Color.Parse(infoTint));
    }

    private async Task InitializeUpdateChecksAsync()
    {
        await CheckAndNotifyUpdatesAsync(force: false);
        _ = RunUpdateLoopAsync();
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
        if (result is { Notified: true })
        {
            var message = $"Version {result.LatestVersion} is available. Download from GitHub Releases.";
            _notificationService.Info("Update available", message);
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
        IsLicenseActive = state.IsActive;
        LicenseStatusText = BuildLicenseBadgeText(state);

        if (!state.IsActive)
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
