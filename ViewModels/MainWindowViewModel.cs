using System;
using System.Reactive;
using Avalonia;
using Avalonia.Styling;
using Avalonia.Media;
using ReactiveUI;
using AgentBuddy.Services;

namespace AgentBuddy.ViewModels;

/// <summary>
/// Main window ViewModel - handles navigation and theme switching
/// </summary>
public class MainWindowViewModel : ViewModelBase
{
    private ViewModelBase? _currentView;
    private bool _isDarkTheme;
    private bool _isSidebarExpanded = true;
    private string _currentViewName = "Dashboard";

    // Services
    private readonly DatabaseService _databaseService;
    private readonly PythonService _pythonService;
    private readonly MetricsCalculator _metricsCalculator;
    private readonly ValidationService _validationService;
    private readonly ReportsService _reportsService;
    private readonly NotificationService _notificationService;
    private readonly MobileSyncService _mobileSyncService;

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
        _pythonService = new PythonService();
        _metricsCalculator = new MetricsCalculator(_databaseService);
        _validationService = new ValidationService(_databaseService);
        _reportsService = new ReportsService();
        _notificationService = new NotificationService();
        _mobileSyncService = new MobileSyncService();

        // Initialize view models
        DashboardViewModel = new DashboardViewModel(_databaseService, _metricsCalculator, _pythonService, _mobileSyncService, _notificationService);
        ListManagementViewModel = new ListManagementViewModel(_databaseService, _validationService, _pythonService, _notificationService);
        ReportsViewModel = new ReportsViewModel(_reportsService, _notificationService);
        SupportViewModel = new SupportViewModel(_reportsService, _notificationService);
        SettingsViewModel = new SettingsViewModel(_databaseService, _pythonService);

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

    /// <summary>
    /// Toggle sidebar expansion
    /// </summary>
    private void ToggleSidebar()
    {
        IsSidebarExpanded = !IsSidebarExpanded;
    }
}
