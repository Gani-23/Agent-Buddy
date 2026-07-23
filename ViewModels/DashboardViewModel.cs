using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Globalization;
using ReactiveUI;
using AgentBuddy.Models;
using AgentBuddy.Services;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.Painting.Effects;
using SkiaSharp;

namespace AgentBuddy.ViewModels;

/// <summary>
/// Dashboard ViewModel - displays metrics and account summaries with charts
/// </summary>
public class DashboardViewModel : ViewModelBase
{
    private const int MaturedInstallmentThreshold = 120;
    private const int AboutToMatureInstallmentThreshold = 108;
    private const int RecentAccountDaysWindow = 30;
    private const string RdCertificateRenewalDateKey = "rd_certificate_renewal_date";

    private readonly DatabaseService _databaseService;
    private readonly MetricsCalculator _metricsCalculator;
    private readonly PythonService _pythonService;
    private readonly MobileSyncService _mobileSyncService;
    private readonly NotificationService? _notificationService;

    private bool _isDarkTheme;
    private bool _isLoading;
    private bool _isUpdating;
    private bool _isUpdatingAslaas;
    private bool _isSyncingToMobile;
    private string _updateStatus = string.Empty;
    private string _halfMonthTitleSuffix = string.Empty;
    private string _globalAccountSearchQuery = string.Empty;
    private DateTime? _rdCertificateRenewalDate;
    private string _rdCertificateRenewalHeadline = "Renewal date not set";
    private string _rdCertificateRenewalMessage = "Set the next postal RD certificate renewal date in Settings.";
    private string _rdCertificateRenewalCountdown = "No renewal date";
    private string _rdCertificateRenewalTag = "RenewalMissing";
    private int _totalAccounts;
    private decimal _totalAmount;
    private DateTime? _lastUpdated;
    
    // Chart data
    private ISeries[] _categorySeries = Array.Empty<ISeries>();
    private ISeries[] _revenueSeries = Array.Empty<ISeries>();
    private Axis[] _revenueXAxes = Array.Empty<Axis>();
    private Axis[] _revenueYAxes = Array.Empty<Axis>();
    private SolidColorPaint _chartLegendTextPaint = new(SKColor.Parse("#2F3437"));
    
    // Summary metrics
    private int _firstHalfPending;
    private decimal _firstHalfPendingAmount;
    private int _firstHalfDeposited;
    private decimal _firstHalfDepositedAmount;
    private int _secondHalfPending;
    private decimal _secondHalfPendingAmount;
    private int _secondHalfDeposited;
    private decimal _secondHalfDepositedAmount;

    // Actionable segments
    private readonly Dictionary<string, List<RDAccount>> _segmentAccounts = new(StringComparer.OrdinalIgnoreCase);
    private int _pendingThisMonthCount;
    private decimal _pendingThisMonthAmount;
    private int _nextMonthCollectionCount;
    private decimal _nextMonthCollectionAmount;
    private int _advancedPaidCount;
    private decimal _advancedPaidAmount;
    private int _newAccounts30DaysCount;
    private decimal _newAccounts30DaysAmount;
    private int _newAccountsMissingAslaasCount;
    private int _aboutToFreezeCount;
    private decimal _aboutToFreezeAmount;
    private int _aboutToMatureCount;
    private decimal _aboutToMatureAmount;
    private int _maturedCount;
    private decimal _maturedAmount;
    private int _extendedAccountsCount;
    private decimal _extendedAccountsAmount;
    private int _closedAccountsCount;
    private decimal _closedAccountsAmount;
    private int _firstHalfPendingWindowCount;
    private decimal _firstHalfPendingWindowAmount;
    private int _secondHalfPendingWindowCount;
    private decimal _secondHalfPendingWindowAmount;
    private int _firstHalfDepositedWindowCount;
    private decimal _firstHalfDepositedWindowAmount;
    private int _secondHalfDepositedWindowCount;
    private decimal _secondHalfDepositedWindowAmount;
    private bool _isUpdateAvailable;
    private string _latestVersion = string.Empty;
    private string? _updateReleaseUrl;
    private string _updateButtonText = "Download Update";

    public DashboardViewModel(
        DatabaseService databaseService,
        MetricsCalculator metricsCalculator,
        PythonService pythonService,
        MobileSyncService mobileSyncService,
        NotificationService? notificationService = null)
    {
        _databaseService = databaseService;
        _metricsCalculator = metricsCalculator;
        _pythonService = pythonService;
        _mobileSyncService = mobileSyncService;
        _notificationService = notificationService;

        _databaseService.DatabaseChanged += OnDatabaseChanged;

        CategoryData = new ObservableCollection<CategoryData>();
        MonthlyRevenues = new ObservableCollection<MonthlyRevenue>();
        AccountsDueSoon = new ObservableCollection<RDAccount>();

        RefreshCommand = ReactiveCommand.CreateFromTask(LoadDataAsync);
        UpdateDatabaseCommand = ReactiveCommand.CreateFromTask(UpdateDatabaseAsync);
        UpdateNewAccountAslaasCommand = ReactiveCommand.CreateFromTask(UpdateNewAccountAslaasAsync);
        SyncToMobileCommand = ReactiveCommand.CreateFromTask(SyncToMobileAsync);
        ViewAccountDetailsCommand = ReactiveCommand.Create<RDAccount>(ViewAccountDetails);
        OpenUpdateLinkCommand = ReactiveCommand.Create(OpenUpdateLink);

        // Load data on initialization
        HalfMonthTitleSuffix = BuildHalfMonthTitleSuffix(DateTime.Today);
        _ = LoadDataAsync();
    }

    public bool IsUpdateAvailable
    {
        get => _isUpdateAvailable;
        set
        {
            this.RaiseAndSetIfChanged(ref _isUpdateAvailable, value);
            this.RaisePropertyChanged(nameof(UpdateButtonText));
        }
    }

    public string LatestVersion
    {
        get => _latestVersion;
        set
        {
            this.RaiseAndSetIfChanged(ref _latestVersion, value);
            this.RaisePropertyChanged(nameof(UpdateButtonText));
        }
    }

    public string? UpdateReleaseUrl
    {
        get => _updateReleaseUrl;
        set => this.RaiseAndSetIfChanged(ref _updateReleaseUrl, value);
    }

    public string UpdateButtonText => IsUpdateAvailable && !string.IsNullOrWhiteSpace(LatestVersion)
        ? $"Download Update (v{LatestVersion})"
        : _updateButtonText;

    public ReactiveCommand<Unit, Unit> OpenUpdateLinkCommand { get; }

    public string HalfMonthTitleSuffix
    {
        get => _halfMonthTitleSuffix;
        private set => this.RaiseAndSetIfChanged(ref _halfMonthTitleSuffix, value);
    }

    public string GlobalAccountSearchQuery
    {
        get => _globalAccountSearchQuery;
        set => this.RaiseAndSetIfChanged(ref _globalAccountSearchQuery, value);
    }

    public DateTime? RdCertificateRenewalDate
    {
        get => _rdCertificateRenewalDate;
        private set => this.RaiseAndSetIfChanged(ref _rdCertificateRenewalDate, value);
    }

    public string RdCertificateRenewalHeadline
    {
        get => _rdCertificateRenewalHeadline;
        private set => this.RaiseAndSetIfChanged(ref _rdCertificateRenewalHeadline, value);
    }

    public string RdCertificateRenewalMessage
    {
        get => _rdCertificateRenewalMessage;
        private set => this.RaiseAndSetIfChanged(ref _rdCertificateRenewalMessage, value);
    }

    public string RdCertificateRenewalCountdown
    {
        get => _rdCertificateRenewalCountdown;
        private set => this.RaiseAndSetIfChanged(ref _rdCertificateRenewalCountdown, value);
    }

    public string RdCertificateRenewalDateDisplay => RdCertificateRenewalDate.HasValue
        ? $"Renewal date: {RdCertificateRenewalDate:dd-MMM-yyyy}"
        : "Renewal date: not set";

    public string RdCertificateRenewalTag
    {
        get => _rdCertificateRenewalTag;
        private set => this.RaiseAndSetIfChanged(ref _rdCertificateRenewalTag, value);
    }

    public bool IsDarkTheme
    {
        get => _isDarkTheme;
        set
        {
            if (this.RaiseAndSetIfChanged(ref _isDarkTheme, value))
            {
                ApplyChartTheme();
            }
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => this.RaiseAndSetIfChanged(ref _isLoading, value);
    }

    public bool IsUpdating
    {
        get => _isUpdating;
        set
        {
            if (this.RaiseAndSetIfChanged(ref _isUpdating, value))
            {
                this.RaisePropertyChanged(nameof(IsBusy));
            }
        }
    }

    public bool IsUpdatingAslaas
    {
        get => _isUpdatingAslaas;
        set
        {
            if (this.RaiseAndSetIfChanged(ref _isUpdatingAslaas, value))
            {
                this.RaisePropertyChanged(nameof(IsBusy));
            }
        }
    }

    public bool IsSyncingToMobile
    {
        get => _isSyncingToMobile;
        set
        {
            if (this.RaiseAndSetIfChanged(ref _isSyncingToMobile, value))
            {
                this.RaisePropertyChanged(nameof(IsBusy));
            }
        }
    }

    public bool IsBusy => IsUpdating || IsUpdatingAslaas || IsSyncingToMobile;

    public string UpdateStatus
    {
        get => _updateStatus;
        set
        {
            var hadStatus = HasUpdateStatus;
            this.RaiseAndSetIfChanged(ref _updateStatus, value);
            if (hadStatus != HasUpdateStatus)
            {
                this.RaisePropertyChanged(nameof(HasUpdateStatus));
            }
        }
    }

    public bool HasUpdateStatus => !string.IsNullOrWhiteSpace(UpdateStatus);

    public int TotalAccounts
    {
        get => _totalAccounts;
        set => this.RaiseAndSetIfChanged(ref _totalAccounts, value);
    }

    public decimal TotalAmount
    {
        get => _totalAmount;
        set => this.RaiseAndSetIfChanged(ref _totalAmount, value);
    }

    public DateTime? LastUpdated
    {
        get => _lastUpdated;
        set => this.RaiseAndSetIfChanged(ref _lastUpdated, value);
    }

    // Chart properties
    public ISeries[] CategorySeries
    {
        get => _categorySeries;
        set => this.RaiseAndSetIfChanged(ref _categorySeries, value);
    }

    public ISeries[] RevenueSeries
    {
        get => _revenueSeries;
        set => this.RaiseAndSetIfChanged(ref _revenueSeries, value);
    }

    public Axis[] RevenueXAxes
    {
        get => _revenueXAxes;
        set => this.RaiseAndSetIfChanged(ref _revenueXAxes, value);
    }

    public Axis[] RevenueYAxes
    {
        get => _revenueYAxes;
        set => this.RaiseAndSetIfChanged(ref _revenueYAxes, value);
    }

    public SolidColorPaint ChartLegendTextPaint
    {
        get => _chartLegendTextPaint;
        set => this.RaiseAndSetIfChanged(ref _chartLegendTextPaint, value);
    }

    // Summary metrics
    public int FirstHalfPending
    {
        get => _firstHalfPending;
        set => this.RaiseAndSetIfChanged(ref _firstHalfPending, value);
    }

    public decimal FirstHalfPendingAmount
    {
        get => _firstHalfPendingAmount;
        set => this.RaiseAndSetIfChanged(ref _firstHalfPendingAmount, value);
    }

    public int FirstHalfDeposited
    {
        get => _firstHalfDeposited;
        set => this.RaiseAndSetIfChanged(ref _firstHalfDeposited, value);
    }

    public decimal FirstHalfDepositedAmount
    {
        get => _firstHalfDepositedAmount;
        set => this.RaiseAndSetIfChanged(ref _firstHalfDepositedAmount, value);
    }

    public int SecondHalfPending
    {
        get => _secondHalfPending;
        set => this.RaiseAndSetIfChanged(ref _secondHalfPending, value);
    }

    public decimal SecondHalfPendingAmount
    {
        get => _secondHalfPendingAmount;
        set => this.RaiseAndSetIfChanged(ref _secondHalfPendingAmount, value);
    }

    public int SecondHalfDeposited
    {
        get => _secondHalfDeposited;
        set => this.RaiseAndSetIfChanged(ref _secondHalfDeposited, value);
    }

    public decimal SecondHalfDepositedAmount
    {
        get => _secondHalfDepositedAmount;
        set => this.RaiseAndSetIfChanged(ref _secondHalfDepositedAmount, value);
    }

    public int PendingThisMonthCount
    {
        get => _pendingThisMonthCount;
        private set => this.RaiseAndSetIfChanged(ref _pendingThisMonthCount, value);
    }

    public decimal PendingThisMonthAmount
    {
        get => _pendingThisMonthAmount;
        private set => this.RaiseAndSetIfChanged(ref _pendingThisMonthAmount, value);
    }

    public int NextMonthCollectionCount
    {
        get => _nextMonthCollectionCount;
        private set => this.RaiseAndSetIfChanged(ref _nextMonthCollectionCount, value);
    }

    public decimal NextMonthCollectionAmount
    {
        get => _nextMonthCollectionAmount;
        private set => this.RaiseAndSetIfChanged(ref _nextMonthCollectionAmount, value);
    }

    public int AdvancedPaidCount
    {
        get => _advancedPaidCount;
        private set => this.RaiseAndSetIfChanged(ref _advancedPaidCount, value);
    }

    public decimal AdvancedPaidAmount
    {
        get => _advancedPaidAmount;
        private set => this.RaiseAndSetIfChanged(ref _advancedPaidAmount, value);
    }

    public int NewAccounts30DaysCount
    {
        get => _newAccounts30DaysCount;
        private set => this.RaiseAndSetIfChanged(ref _newAccounts30DaysCount, value);
    }

    public decimal NewAccounts30DaysAmount
    {
        get => _newAccounts30DaysAmount;
        private set => this.RaiseAndSetIfChanged(ref _newAccounts30DaysAmount, value);
    }

    public bool HasNewAccountsRibbon => NewAccounts30DaysCount > 0;

    public int NewAccountsMissingAslaasCount
    {
        get => _newAccountsMissingAslaasCount;
        private set
        {
            this.RaiseAndSetIfChanged(ref _newAccountsMissingAslaasCount, value);
            this.RaisePropertyChanged(nameof(HasNewAccountsMissingAslaasNotice));
        }
    }

    public bool HasNewAccountsMissingAslaasNotice => NewAccountsMissingAslaasCount > 0;

    public int AboutToFreezeCount
    {
        get => _aboutToFreezeCount;
        private set => this.RaiseAndSetIfChanged(ref _aboutToFreezeCount, value);
    }

    public decimal AboutToFreezeAmount
    {
        get => _aboutToFreezeAmount;
        private set => this.RaiseAndSetIfChanged(ref _aboutToFreezeAmount, value);
    }

    public int MaturedCount
    {
        get => _maturedCount;
        private set => this.RaiseAndSetIfChanged(ref _maturedCount, value);
    }

    public decimal MaturedAmount
    {
        get => _maturedAmount;
        private set => this.RaiseAndSetIfChanged(ref _maturedAmount, value);
    }

    public int ExtendedAccountsCount
    {
        get => _extendedAccountsCount;
        private set => this.RaiseAndSetIfChanged(ref _extendedAccountsCount, value);
    }

    public decimal ExtendedAccountsAmount
    {
        get => _extendedAccountsAmount;
        private set => this.RaiseAndSetIfChanged(ref _extendedAccountsAmount, value);
    }

    public int AboutToMatureCount
    {
        get => _aboutToMatureCount;
        private set => this.RaiseAndSetIfChanged(ref _aboutToMatureCount, value);
    }

    public decimal AboutToMatureAmount
    {
        get => _aboutToMatureAmount;
        private set => this.RaiseAndSetIfChanged(ref _aboutToMatureAmount, value);
    }

    public int ClosedAccountsCount
    {
        get => _closedAccountsCount;
        private set => this.RaiseAndSetIfChanged(ref _closedAccountsCount, value);
    }

    public decimal ClosedAccountsAmount
    {
        get => _closedAccountsAmount;
        private set => this.RaiseAndSetIfChanged(ref _closedAccountsAmount, value);
    }

    public int FirstHalfPendingWindowCount
    {
        get => _firstHalfPendingWindowCount;
        private set => this.RaiseAndSetIfChanged(ref _firstHalfPendingWindowCount, value);
    }

    public decimal FirstHalfPendingWindowAmount
    {
        get => _firstHalfPendingWindowAmount;
        private set => this.RaiseAndSetIfChanged(ref _firstHalfPendingWindowAmount, value);
    }

    public int SecondHalfPendingWindowCount
    {
        get => _secondHalfPendingWindowCount;
        private set => this.RaiseAndSetIfChanged(ref _secondHalfPendingWindowCount, value);
    }

    public decimal SecondHalfPendingWindowAmount
    {
        get => _secondHalfPendingWindowAmount;
        private set => this.RaiseAndSetIfChanged(ref _secondHalfPendingWindowAmount, value);
    }

    public int FirstHalfDepositedWindowCount
    {
        get => _firstHalfDepositedWindowCount;
        private set => this.RaiseAndSetIfChanged(ref _firstHalfDepositedWindowCount, value);
    }

    public decimal FirstHalfDepositedWindowAmount
    {
        get => _firstHalfDepositedWindowAmount;
        private set => this.RaiseAndSetIfChanged(ref _firstHalfDepositedWindowAmount, value);
    }

    public int SecondHalfDepositedWindowCount
    {
        get => _secondHalfDepositedWindowCount;
        private set => this.RaiseAndSetIfChanged(ref _secondHalfDepositedWindowCount, value);
    }

    public decimal SecondHalfDepositedWindowAmount
    {
        get => _secondHalfDepositedWindowAmount;
        private set => this.RaiseAndSetIfChanged(ref _secondHalfDepositedWindowAmount, value);
    }

    public ObservableCollection<CategoryData> CategoryData { get; }
    public ObservableCollection<MonthlyRevenue> MonthlyRevenues { get; }
    public ObservableCollection<RDAccount> AccountsDueSoon { get; }

    public ReactiveCommand<Unit, Unit> RefreshCommand { get; }
    public ReactiveCommand<Unit, Unit> UpdateDatabaseCommand { get; }
    public ReactiveCommand<Unit, Unit> UpdateNewAccountAslaasCommand { get; }
    public ReactiveCommand<Unit, Unit> SyncToMobileCommand { get; }
    public ReactiveCommand<RDAccount, Unit> ViewAccountDetailsCommand { get; }

    public async Task RefreshRdCertificateRenewalAsync()
    {
        await LoadRdCertificateRenewalAsync();
    }

    private void OnDatabaseChanged(object? sender, EventArgs e)
    {
        _ = LoadDataAsync();
    }

    private async Task LoadDataAsync()
    {
        IsLoading = true;

        try
        {
            if (!_databaseService.DatabaseExists())
            {
                // Database not found
                IsLoading = false;
                return;
            }

            var accounts = await _databaseService.GetAllActiveAccountsAsync();
            var closedAccounts = await _databaseService.GetClosedAccountsAsync();
            BuildActionableSegments(accounts, closedAccounts);

            // Load metrics
            var metrics = await _metricsCalculator.CalculateMetricsAsync();
            
            TotalAccounts = metrics.TotalAccounts;
            TotalAmount = metrics.TotalAmount;
            LastUpdated = await _databaseService.GetLastUpdateTimeAsync();
            await LoadRdCertificateRenewalAsync();

            // Update summary metrics
            FirstHalfPending = metrics.FirstHalfPendingCount;
            FirstHalfPendingAmount = metrics.FirstHalfPendingAmount;
            FirstHalfDeposited = metrics.FirstHalfDepositedCount;
            FirstHalfDepositedAmount = metrics.FirstHalfDepositedAmount;
            SecondHalfPending = metrics.SecondHalfPendingCount;
            SecondHalfPendingAmount = metrics.SecondHalfPendingAmount;
            SecondHalfDeposited = metrics.SecondHalfDepositedCount;
            SecondHalfDepositedAmount = metrics.SecondHalfDepositedAmount;

            // Load category data
            CategoryData.Clear();
            var categories = await _metricsCalculator.GetCategoryDataAsync();
            foreach (var category in categories)
            {
                CategoryData.Add(category);
            }

            // Create category pie chart
            CreateCategoryChart(categories);

            // Load monthly revenues
            MonthlyRevenues.Clear();
            foreach (var revenue in metrics.MonthlyRevenues)
            {
                MonthlyRevenues.Add(revenue);
            }

            // Create revenue bar chart
            CreateRevenueChart(metrics.MonthlyRevenues);

            // Load accounts due soon
            AccountsDueSoon.Clear();
            foreach (var account in metrics.AccountsDueSoon)
            {
                AccountsDueSoon.Add(account);
            }
        }
        catch (Exception ex)
        {
            // Handle error
            Console.WriteLine($"Error loading dashboard: {ex.Message}");
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task LoadRdCertificateRenewalAsync()
    {
        var saved = (await _databaseService.GetAppSettingAsync(RdCertificateRenewalDateKey) ?? string.Empty).Trim();
        if (!TryParseRdCertificateRenewalDate(saved, out var renewalDate))
        {
            RdCertificateRenewalDate = null;
            this.RaisePropertyChanged(nameof(RdCertificateRenewalDateDisplay));
            RdCertificateRenewalHeadline = "Renewal date not set";
            RdCertificateRenewalMessage = "Set the next postal RD certificate renewal date in Settings.";
            RdCertificateRenewalCountdown = "No renewal date";
            RdCertificateRenewalTag = "RenewalMissing";
            return;
        }

        RdCertificateRenewalDate = renewalDate.Date;
        this.RaisePropertyChanged(nameof(RdCertificateRenewalDateDisplay));
        var daysRemaining = (renewalDate.Date - DateTime.Today).Days;

        if (daysRemaining < 0)
        {
            RdCertificateRenewalHeadline = "Renew immediately";
            RdCertificateRenewalMessage = $"Postal RD certificate renewal expired on {renewalDate:dd-MMM-yyyy}.";
            RdCertificateRenewalCountdown = $"{-daysRemaining} day(s) overdue";
            RdCertificateRenewalTag = "RenewalUrgent";
            return;
        }

        if (daysRemaining <= 10)
        {
            RdCertificateRenewalHeadline = "Urgent renewal warning";
            RdCertificateRenewalMessage = $"Renew postal RD certificate by {renewalDate:dd-MMM-yyyy}.";
            RdCertificateRenewalCountdown = daysRemaining == 0
                ? "Due today"
                : $"{daysRemaining} day(s) left";
            RdCertificateRenewalTag = "RenewalUrgent";
            return;
        }

        RdCertificateRenewalHeadline = "RD certificate renewal";
        RdCertificateRenewalMessage = $"Next postal certificate renewal date: {renewalDate:dd-MMM-yyyy}.";
        RdCertificateRenewalCountdown = $"{daysRemaining} day(s) remaining";
        RdCertificateRenewalTag = "RenewalNormal";
    }

    private static bool TryParseRdCertificateRenewalDate(string raw, out DateTime renewalDate)
    {
        var formats = new[]
        {
            "yyyy-MM-dd",
            "dd-MMM-yyyy",
            "dd-MM-yyyy",
            "dd/MM/yyyy",
            "yyyy/MM/dd"
        };

        return DateTime.TryParseExact(
                   raw,
                   formats,
                   CultureInfo.InvariantCulture,
                   DateTimeStyles.AllowWhiteSpaces,
                   out renewalDate)
               || DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out renewalDate)
               || DateTime.TryParse(raw, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out renewalDate);
    }

    private void BuildActionableSegments(List<RDAccount> accounts, List<RDAccount> closedAccounts)
    {
        var today = DateTime.Today;
        HalfMonthTitleSuffix = BuildHalfMonthTitleSuffix(today);
        var monthStart = new DateTime(today.Year, today.Month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        var nextMonthStart = monthStart.AddMonths(1);
        var nextMonthEnd = nextMonthStart.AddMonths(1).AddDays(-1);

        var pendingThisMonth = accounts
            .Where(a =>
            {
                var due = a.GetNextInstallmentDate();
                return due.HasValue &&
                       due.Value.Date >= monthStart &&
                       due.Value.Date <= monthEnd;
            })
            .OrderBy(a => a.GetNextInstallmentDate())
            .ToList();

        var firstHalfPendingWindow = pendingThisMonth
            .Where(a =>
            {
                var due = a.GetNextInstallmentDate();
                return due.HasValue && due.Value.Day >= 1 && due.Value.Day <= 15;
            })
            .OrderBy(a => a.GetNextInstallmentDate())
            .ToList();

        var secondHalfPendingWindow = pendingThisMonth
            .Where(a =>
            {
                var due = a.GetNextInstallmentDate();
                return due.HasValue && due.Value.Day >= 16;
            })
            .OrderBy(a => a.GetNextInstallmentDate())
            .ToList();

        var depositedThisMonth = accounts
            .Where(a =>
            {
                var due = a.GetNextInstallmentDate();
                return due.HasValue && due.Value.Date > monthEnd;
            })
            .OrderBy(a => a.GetNextInstallmentDate())
            .ToList();

        var firstHalfDepositedWindow = depositedThisMonth
            .Where(a =>
            {
                var due = a.GetNextInstallmentDate();
                return due.HasValue && due.Value.Day >= 1 && due.Value.Day <= 15;
            })
            .OrderBy(a => a.GetNextInstallmentDate())
            .ToList();

        var secondHalfDepositedWindow = depositedThisMonth
            .Where(a =>
            {
                var due = a.GetNextInstallmentDate();
                return due.HasValue && due.Value.Day >= 16;
            })
            .OrderBy(a => a.GetNextInstallmentDate())
            .ToList();

        var nextMonthCollection = accounts
            .Where(a =>
            {
                var due = a.GetNextInstallmentDate();
                return due.HasValue &&
                       due.Value.Date >= nextMonthStart &&
                       due.Value.Date <= nextMonthEnd;
            })
            .OrderBy(a => a.GetNextInstallmentDate())
            .ToList();

        var advancedPaid = accounts
            .Where(a =>
            {
                var due = a.GetNextInstallmentDate();
                return due.HasValue && due.Value.Date > nextMonthEnd;
            })
            .OrderBy(a => a.GetNextInstallmentDate())
            .ToList();

        var newAccounts30Days = accounts
            .Where(a =>
            {
                return a.FirstSeen.Date >= today.AddDays(-RecentAccountDaysWindow);
            })
            .OrderByDescending(a => a.FirstSeen)
            .ThenByDescending(a => a.LastUpdated)
            .ThenBy(a => a.AccountNo)
            .ToList();

        var newAccountsMissingAslaas = newAccounts30Days
            .Where(IsAslaasMissing)
            .ToList();

        var aboutToFreeze = accounts
            .Where(a =>
            {
                var due = a.GetNextInstallmentDate();
                return due.HasValue && due.Value.Date < today.AddDays(-90);
            })
            .OrderBy(a => a.GetNextInstallmentDate())
            .ToList();

        var aboutToMature = accounts
            .Where(a =>
            {
                var paid = a.GetMonthPaidNumber();
                return paid >= AboutToMatureInstallmentThreshold && paid < MaturedInstallmentThreshold;
            })
            .OrderByDescending(a => a.GetMonthPaidNumber())
            .ThenBy(a => a.AccountNo)
            .ToList();

        var closedLookup = closedAccounts
            .Select(a => (a.AccountNo ?? string.Empty).Trim())
            .Where(accountNo => !string.IsNullOrWhiteSpace(accountNo))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var maturedAll = accounts
            .Where(a => a.GetMonthPaidNumber() >= MaturedInstallmentThreshold)
            .ToList();

        var extendedAccounts = maturedAll
            .Where(a =>
            {
                var accountNo = (a.AccountNo ?? string.Empty).Trim();
                return !string.IsNullOrWhiteSpace(accountNo) && closedLookup.Contains(accountNo);
            })
            .OrderByDescending(a => a.GetMonthPaidNumber())
            .ThenBy(a => a.AccountNo)
            .ToList();

        var matured = maturedAll
            .Where(a =>
            {
                var accountNo = (a.AccountNo ?? string.Empty).Trim();
                return string.IsNullOrWhiteSpace(accountNo) || !closedLookup.Contains(accountNo);
            })
            .OrderByDescending(a => a.GetMonthPaidNumber())
            .ThenBy(a => a.AccountNo)
            .ToList();

        _segmentAccounts["pending-month"] = pendingThisMonth;
        _segmentAccounts["next-month"] = nextMonthCollection;
        _segmentAccounts["advanced-paid"] = advancedPaid;
        _segmentAccounts["all-accounts"] = accounts
            .OrderBy(a => a.AccountNo)
            .ToList();
        _segmentAccounts["new-accounts"] = newAccounts30Days;
        _segmentAccounts["freeze-risk"] = aboutToFreeze;
        _segmentAccounts["about-to-mature"] = aboutToMature;
        _segmentAccounts["matured"] = matured;
        _segmentAccounts["extended-accounts"] = extendedAccounts;
        _segmentAccounts["closed-accounts"] = closedAccounts.ToList();
        _segmentAccounts["pending-first-half"] = firstHalfPendingWindow;
        _segmentAccounts["pending-second-half"] = secondHalfPendingWindow;
        _segmentAccounts["deposited-first-half"] = firstHalfDepositedWindow;
        _segmentAccounts["deposited-second-half"] = secondHalfDepositedWindow;
        _segmentAccounts["due-soon"] = accounts
            .Where(a => a.IsDueWithinDays(30))
            .OrderBy(a => a.GetNextInstallmentDate())
            .ToList();

        PendingThisMonthCount = pendingThisMonth.Count;
        PendingThisMonthAmount = pendingThisMonth.Sum(a => a.GetAmount());
        NextMonthCollectionCount = nextMonthCollection.Count;
        NextMonthCollectionAmount = nextMonthCollection.Sum(a => a.GetAmount());
        AdvancedPaidCount = advancedPaid.Count;
        AdvancedPaidAmount = advancedPaid.Sum(a => a.GetAmount());
        NewAccounts30DaysCount = newAccounts30Days.Count;
        NewAccounts30DaysAmount = newAccounts30Days.Sum(a => a.GetAmount());
        NewAccountsMissingAslaasCount = newAccountsMissingAslaas.Count;
        this.RaisePropertyChanged(nameof(HasNewAccountsRibbon));
        AboutToFreezeCount = aboutToFreeze.Count;
        AboutToFreezeAmount = aboutToFreeze.Sum(a => a.GetAmount());
        AboutToMatureCount = aboutToMature.Count;
        AboutToMatureAmount = aboutToMature.Sum(a => a.GetAmount());
        MaturedCount = matured.Count;
        MaturedAmount = matured.Sum(a => a.GetAmount());
        ExtendedAccountsCount = extendedAccounts.Count;
        ExtendedAccountsAmount = extendedAccounts.Sum(a => a.GetAmount());
        ClosedAccountsCount = closedAccounts.Count;
        ClosedAccountsAmount = closedAccounts.Sum(a => a.GetAmount());
        FirstHalfPendingWindowCount = firstHalfPendingWindow.Count;
        FirstHalfPendingWindowAmount = firstHalfPendingWindow.Sum(a => a.GetAmount());
        SecondHalfPendingWindowCount = secondHalfPendingWindow.Count;
        SecondHalfPendingWindowAmount = secondHalfPendingWindow.Sum(a => a.GetAmount());
        FirstHalfDepositedWindowCount = firstHalfDepositedWindow.Count;
        FirstHalfDepositedWindowAmount = firstHalfDepositedWindow.Sum(a => a.GetAmount());
        SecondHalfDepositedWindowCount = secondHalfDepositedWindow.Count;
        SecondHalfDepositedWindowAmount = secondHalfDepositedWindow.Sum(a => a.GetAmount());
    }

    private static bool IsAslaasMissing(RDAccount account)
    {
        return string.IsNullOrWhiteSpace((account.AslaasNo ?? string.Empty).Trim());
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

    private async Task UpdateNewAccountAslaasAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsUpdatingAslaas = true;
        UpdateStatus = "Checking new accounts with missing ASLAAS...";

        try
        {
            var today = DateTime.Today;
            var accountsToUpdate = (await _databaseService.GetAllActiveAccountsAsync())
                .Where(a => a.FirstSeen.Date >= today.AddDays(-RecentAccountDaysWindow))
                .Where(IsAslaasMissing)
                .OrderByDescending(a => a.FirstSeen)
                .ThenBy(a => a.AccountNo)
                .Select(a => new AslaasUpdateItem
                {
                    AccountNo = (a.AccountNo ?? string.Empty).Trim(),
                    AslaasNo = "APPLIED"
                })
                .Where(a => !string.IsNullOrWhiteSpace(a.AccountNo))
                .ToList();

            if (accountsToUpdate.Count == 0)
            {
                UpdateStatus = "No new accounts are missing ASLAAS.";
                _notificationService?.Info("ASLAAS", "No new accounts are missing ASLAAS.");
                await Task.Delay(2500);
                return;
            }

            var (isPythonInstalled, _) = await _pythonService.CheckPythonInstalledAsync();
            if (!isPythonInstalled)
            {
                UpdateStatus = "Python is not installed. Install Python 3.x and retry.";
                _notificationService?.Error("ASLAAS Update Failed", "Python 3.x was not found.");
                await Task.Delay(4000);
                return;
            }

            UpdateStatus = $"Found {accountsToUpdate.Count} new account(s). Opening portal for ASLAAS update...";
            var (success, output) = await _pythonService.UpdateMissingAslaasAsync(
                accountsToUpdate,
                progress =>
                {
                    var trimmed = (progress ?? string.Empty).Trim();
                    if (!string.IsNullOrWhiteSpace(trimmed))
                    {
                        UpdateStatus = trimmed;
                    }
                });

            if (!success)
            {
                var reason = GetFirstMeaningfulLine(output);
                var savedSome = (output ?? string.Empty).IndexOf(
                    "ASLAAS update submitted:",
                    StringComparison.OrdinalIgnoreCase) >= 0;
                UpdateStatus = savedSome
                    ? $"ASLAAS update failed after saving some account(s): {reason}"
                    : $"ASLAAS update failed: {reason}";
                _notificationService?.Error("ASLAAS Update Failed", reason);
                await Task.Delay(5000);
                return;
            }

            await _databaseService.SaveAslaasUpdatesAsync(accountsToUpdate);

            UpdateStatus = $"Updated {accountsToUpdate.Count} new account(s). Refreshing dashboard...";
            _notificationService?.Success("ASLAAS Updated", "New account ASLAAS updates were saved locally.");
            _databaseService.NotifyDatabaseChanged();
            await LoadDataAsync();
            await Task.Delay(2500);
        }
        catch (Exception ex)
        {
            UpdateStatus = $"ASLAAS update failed: {ex.Message}";
            _notificationService?.Error("ASLAAS Update Failed", ex.Message);
            await Task.Delay(5000);
        }
        finally
        {
            IsUpdatingAslaas = false;
            UpdateStatus = string.Empty;
        }
    }

    public IReadOnlyList<RDAccount> GetAccountsForSegment(string? segmentKey)
    {
        if (string.IsNullOrWhiteSpace(segmentKey))
        {
            return Array.Empty<RDAccount>();
        }

        return _segmentAccounts.TryGetValue(segmentKey, out var list)
            ? list
            : Array.Empty<RDAccount>();
    }

    public string GetSegmentTitle(string? segmentKey)
    {
        return segmentKey switch
        {
            "pending-month" => "Pending - Current Month",
            "next-month" => "Next Month Collection",
            "advanced-paid" => "Advance Paid Accounts",
            "all-accounts" => "All Active Accounts",
            "new-accounts" => "New Accounts (Last 30 Days)",
            "freeze-risk" => "About To Freeze Accounts",
            "about-to-mature" => "About To Mature Accounts",
            "matured" => "Matured Accounts",
            "extended-accounts" => "Extended Accounts",
            "closed-accounts" => "Closed Accounts",
            "pending-first-half" => "Pending Accounts - First Half",
            "pending-second-half" => "Pending Accounts - Second Half",
            "deposited-first-half" => "Deposited Accounts - First Half",
            "deposited-second-half" => "Deposited Accounts - Second Half",
            "due-soon" => "Accounts Due Within 30 Days",
            _ => "Accounts"
        };
    }

    private static string BuildHalfMonthTitleSuffix(DateTime date)
    {
        return $"({date:MMMM yyyy})";
    }

    private void CreateCategoryChart(List<CategoryData> categories)
    {
        var series = new List<ISeries>();

        foreach (var category in categories)
        {
            series.Add(new PieSeries<int>
            {
                Values = new[] { category.Count },
                Name = category.CategoryName,
                DataLabelsPaint = new SolidColorPaint(SKColors.White),
                DataLabelsSize = 14,
                DataLabelsPosition = LiveChartsCore.Measure.PolarLabelsPosition.Middle,
                DataLabelsFormatter = point => $"{category.Count}"
            });
        }

        CategorySeries = series.ToArray();
    }

    private void CreateRevenueChart(List<MonthlyRevenue> revenues)
    {
        // Take last 12 months
        var last12Months = revenues.TakeLast(12).ToList();

        var commissionValues = last12Months.Select(r => (double)r.Commission).ToArray();
        var rebateValues = last12Months.Select(r => (double)r.Rebate).ToArray();
        var labels = last12Months.Select(r => r.MonthName).ToArray();
        var axisLabelColor = IsDarkTheme ? SKColor.Parse("#D4D4D4") : SKColor.Parse("#5F6368");
        var separatorColor = IsDarkTheme ? SKColor.Parse("#424242") : SKColor.Parse("#D9D9D9");

        RevenueSeries = new ISeries[]
        {
            new ColumnSeries<double>
            {
                Name = "Collections",
                Values = commissionValues,
                Fill = new SolidColorPaint(SKColor.Parse("#5DADE2")),
                Stroke = null,
                MaxBarWidth = 40
            },
            new ColumnSeries<double>
            {
                Name = "Adjustments",
                Values = rebateValues,
                Fill = new SolidColorPaint(SKColor.Parse("#85C1E2")),
                Stroke = null,
                MaxBarWidth = 40
            }
        };

        RevenueXAxes = new Axis[]
        {
            new Axis
            {
                Labels = labels,
                LabelsRotation = 0,
                TextSize = 12,
                LabelsPaint = new SolidColorPaint(axisLabelColor),
                SeparatorsPaint = new SolidColorPaint(separatorColor)
                {
                    StrokeThickness = 1,
                    PathEffect = new DashEffect(new float[] { 3, 3 })
                }
            }
        };

        RevenueYAxes = new Axis[]
        {
            new Axis
            {
                TextSize = 12,
                LabelsPaint = new SolidColorPaint(axisLabelColor),
                SeparatorsPaint = new SolidColorPaint(separatorColor)
                {
                    StrokeThickness = 1,
                    PathEffect = new DashEffect(new float[] { 3, 3 })
                }
            }
        };
    }

    private void ApplyChartTheme()
    {
        var legendColor = IsDarkTheme ? SKColor.Parse("#ECECEC") : SKColor.Parse("#2F3437");
        ChartLegendTextPaint = new SolidColorPaint(legendColor);

        if (CategoryData.Count > 0)
        {
            CreateCategoryChart(CategoryData.ToList());
        }

        if (MonthlyRevenues.Count > 0)
        {
            CreateRevenueChart(MonthlyRevenues.ToList());
        }
    }

    private void ViewAccountDetails(RDAccount account)
    {
        // This will be called from the view to show account details
        // The view will handle opening the modal
    }

    public async Task<(bool success, string message)> PrintSegmentAsync(string? segmentKey)
    {
        var accounts = GetAccountsForSegment(segmentKey);
        if (accounts.Count == 0)
        {
            return (false, "No accounts available to print.");
        }

        var preferredPrinter = ((await _databaseService.GetAppSettingAsync("default_printer")) ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(preferredPrinter))
        {
            preferredPrinter = ((await _databaseService.GetAppSettingAsync("reports_default_printer")) ?? string.Empty).Trim();
        }

        var title = GetSegmentTitle(segmentKey);
        var safeTitle = new string(title
            .Select(ch => char.IsLetterOrDigit(ch) ? ch : '_')
            .ToArray())
            .Trim('_');
        if (string.IsNullOrWhiteSpace(safeTitle))
        {
            safeTitle = "accounts";
        }

        var tempFilePath = Path.Combine(
            Path.GetTempPath(),
            $"agentbuddy_{safeTitle}_{DateTime.Now:yyyyMMdd_HHmmss}.txt");

        var reportBuilder = new StringBuilder();
        reportBuilder.AppendLine($"Agent Buddy - {title}");
        reportBuilder.AppendLine($"Generated: {DateTime.Now:dd-MMM-yyyy HH:mm:ss}");
        reportBuilder.AppendLine($"Count: {accounts.Count}");
        reportBuilder.AppendLine($"Total Amount: Rs. {accounts.Sum(a => a.GetAmount()):N0}");
        reportBuilder.AppendLine(new string('-', 110));
        reportBuilder.AppendLine("Account No        Name                                Amount      Paid Upto   Next Due");
        reportBuilder.AppendLine(new string('-', 110));

        foreach (var account in accounts)
        {
            var accountNo = (account.AccountNo ?? string.Empty).PadRight(16);
            var name = (account.AccountName ?? string.Empty);
            if (name.Length > 34)
            {
                name = name[..34];
            }
            name = name.PadRight(34);
            var amount = account.GetAmount().ToString("N0").PadLeft(10);
            var paid = (account.MonthPaidUpto ?? string.Empty).PadLeft(10);
            var due = account.GetNextInstallmentDate()?.ToString("dd-MMM-yyyy") ??
                      account.NextInstallmentDate ??
                      "-";

            reportBuilder.AppendLine($"{accountNo}  {name}  {amount}  {paid}  {due}");
        }

        await File.WriteAllTextAsync(tempFilePath, reportBuilder.ToString());

        try
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                var printInfo = new ProcessStartInfo
                {
                    FileName = "notepad.exe",
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                printInfo.ArgumentList.Add("/p");
                printInfo.ArgumentList.Add(tempFilePath);
                Process.Start(printInfo);
            }
            else
            {
                var lpInfo = new ProcessStartInfo
                {
                    FileName = "lp",
                    UseShellExecute = false,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    CreateNoWindow = true
                };
                if (!string.IsNullOrWhiteSpace(preferredPrinter))
                {
                    lpInfo.ArgumentList.Add("-d");
                    lpInfo.ArgumentList.Add(preferredPrinter);
                }
                lpInfo.ArgumentList.Add(tempFilePath);
                using var process = Process.Start(lpInfo);
                if (process == null)
                {
                    return (false, "Could not start print process.");
                }

                var stderr = await process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync();
                if (process.ExitCode != 0)
                {
                    var message = string.IsNullOrWhiteSpace(stderr)
                        ? "Print command failed."
                        : stderr.Trim();
                    return (false, message);
                }
            }

            return (true, $"Print command sent for {title} ({accounts.Count} account(s)).");
        }
        catch (Exception ex)
        {
            return (false, $"Could not print list: {ex.Message}");
        }
    }

    private async Task UpdateDatabaseAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsUpdating = true;
        UpdateStatus = "Checking Python installation...";

        try
        {
            // Check if Python is installed
            var (isInstalled, version) = await _pythonService.CheckPythonInstalledAsync();
            if (!isInstalled)
            {
                UpdateStatus = "Python not found! Please install Python 3.x";
                _notificationService?.Error("Update Failed", "Python 3.x was not found.");
                await Task.Delay(3000);
                IsUpdating = false;
                UpdateStatus = string.Empty;
                return;
            }

            var (fetchExists, _) = _pythonService.CheckScriptsExist();
            if (!fetchExists)
            {
                UpdateStatus = "Fetch_RDAccounts.py not found in DOPAgent folder.";
                _notificationService?.Error("Update Failed", "Fetch_RDAccounts.py was not found.");
                await Task.Delay(4000);
                return;
            }

            UpdateStatus = "Checking required Python packages...";
            var hasPackages = await _pythonService.CheckRequiredPackagesAsync();
            if (!hasPackages)
            {
                UpdateStatus = "Installing missing Python packages...";
                _notificationService?.Info("Python Setup", "Installing missing packages...");

                var (installed, installOutput) = await _pythonService.InstallRequiredPackagesAsync();
                if (!installed)
                {
                    UpdateStatus = "Package installation failed. Check internet and pip.";
                    var firstLine = string.IsNullOrWhiteSpace(installOutput)
                        ? "Could not install required Python packages."
                        : installOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "Could not install required Python packages.";
                    _notificationService?.Error("Package Install Failed", firstLine);
                    await Task.Delay(5000);
                    return;
                }

                _notificationService?.Success("Python Setup", "Required packages installed.");
            }

            UpdateStatus = $"Python {version} ready. Starting update...";
            await Task.Delay(700);

            // Execute the update script
            UpdateStatus = "Running Fetch_RDAccounts.py...";
            var (success, output) = await _pythonService.UpdateDatabaseAsync(
                progress => UpdateStatus = progress
            );

            if (success)
            {
                UpdateStatus = "Update successful! Refreshing dashboard...";
                await Task.Delay(1000);
                _databaseService.NotifyDatabaseChanged();
                UpdateStatus = "Database refreshed!";
                _notificationService?.Success("Database Updated", "Active account data was refreshed successfully.");
                await Task.Delay(2000);
            }
            else
            {
                UpdateStatus = $"Update failed: {output}";
                _notificationService?.Error("Update Failed", "Fetch_RDAccounts.py did not complete successfully.");
                await Task.Delay(5000);
            }
        }
        catch (Exception ex)
        {
            UpdateStatus = $"Error: {ex.Message}";
            _notificationService?.Error("Update Error", ex.Message);
            await Task.Delay(5000);
        }
        finally
        {
            IsUpdating = false;
            UpdateStatus = string.Empty;
        }
    }

    private async Task SyncToMobileAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsSyncingToMobile = true;
        UpdateStatus = "Preparing mobile sync...";

        try
        {
            var (apiUrl, apiKey) = await _databaseService.GetMobileSyncSettingsAsync();
            if (string.IsNullOrWhiteSpace(apiUrl))
            {
                UpdateStatus = "Mobile sync API URL is not configured. Open Settings.";
                _notificationService?.Error("Mobile Sync", "Configure Mobile Sync API URL in Settings.");
                await Task.Delay(3500);
                return;
            }

            var accounts = await _databaseService.GetAllAccountsAsync();
            if (accounts.Count == 0)
            {
                UpdateStatus = "No local accounts found to sync.";
                _notificationService?.Info("Mobile Sync", "No local account rows available.");
                await Task.Delay(2500);
                return;
            }

            var closedAccounts = await _databaseService.GetClosedAccountsAsync();
            UpdateStatus = $"Syncing {accounts.Count} account rows to mobile API...";
            var (success, message) = await _mobileSyncService.PushRdAccountsAsync(
                apiUrl,
                apiKey,
                accounts,
                closedAccounts,
                progress => UpdateStatus = progress);
            UpdateStatus = message;

            if (success)
            {
                _notificationService?.Success("Mobile Sync", "Data synced to mobile dashboard API.");
                await Task.Delay(2000);
            }
            else
            {
                _notificationService?.Error("Mobile Sync Failed", message);
                await Task.Delay(4500);
            }
        }
        catch (Exception ex)
        {
            UpdateStatus = $"Mobile sync error: {ex.Message}";
            _notificationService?.Error("Mobile Sync Error", ex.Message);
            await Task.Delay(4500);
        }
        finally
        {
            IsSyncingToMobile = false;
            UpdateStatus = string.Empty;
        }
    }

    public void ApplyUpdateInfo(bool isUpdateAvailable, string latestVersion, string? releaseUrl)
    {
        IsUpdateAvailable = isUpdateAvailable;
        LatestVersion = latestVersion ?? string.Empty;
        UpdateReleaseUrl = releaseUrl;
    }

    private void OpenUpdateLink()
    {
        if (!IsUpdateAvailable)
        {
            _notificationService?.Warning("No update", "No update is available right now.");
            return;
        }

        var target = string.IsNullOrWhiteSpace(UpdateReleaseUrl)
            ? UpdateService.LatestReleasePage
            : UpdateReleaseUrl;

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = target,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _notificationService?.Error("Open Failed", ex.Message);
        }
    }
}
