using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
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
    private readonly DatabaseService _databaseService;
    private readonly MetricsCalculator _metricsCalculator;
    private readonly PythonService _pythonService;
    private readonly MobileSyncService _mobileSyncService;
    private readonly NotificationService? _notificationService;

    private bool _isDarkTheme;
    private bool _isLoading;
    private bool _isUpdating;
    private bool _isSyncingToMobile;
    private string _updateStatus = string.Empty;
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
    private int _aboutToFreezeCount;
    private decimal _aboutToFreezeAmount;
    private int _maturedCount;
    private decimal _maturedAmount;
    private int _firstHalfPendingWindowCount;
    private decimal _firstHalfPendingWindowAmount;
    private int _secondHalfPendingWindowCount;
    private decimal _secondHalfPendingWindowAmount;
    private int _firstHalfDepositedWindowCount;
    private decimal _firstHalfDepositedWindowAmount;
    private int _secondHalfDepositedWindowCount;
    private decimal _secondHalfDepositedWindowAmount;

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

        CategoryData = new ObservableCollection<CategoryData>();
        MonthlyRevenues = new ObservableCollection<MonthlyRevenue>();
        AccountsDueSoon = new ObservableCollection<RDAccount>();

        RefreshCommand = ReactiveCommand.CreateFromTask(LoadDataAsync);
        UpdateDatabaseCommand = ReactiveCommand.CreateFromTask(UpdateDatabaseAsync);
        SyncToMobileCommand = ReactiveCommand.CreateFromTask(SyncToMobileAsync);
        ViewAccountDetailsCommand = ReactiveCommand.Create<RDAccount>(ViewAccountDetails);

        // Load data on initialization
        _ = LoadDataAsync();
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

    public bool IsBusy => IsUpdating || IsSyncingToMobile;

    public string UpdateStatus
    {
        get => _updateStatus;
        set => this.RaiseAndSetIfChanged(ref _updateStatus, value);
    }

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
    public ReactiveCommand<Unit, Unit> SyncToMobileCommand { get; }
    public ReactiveCommand<RDAccount, Unit> ViewAccountDetailsCommand { get; }

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
            BuildActionableSegments(accounts);

            // Load metrics
            var metrics = await _metricsCalculator.CalculateMetricsAsync();
            
            TotalAccounts = metrics.TotalAccounts;
            TotalAmount = metrics.TotalAmount;
            LastUpdated = await _databaseService.GetLastUpdateTimeAsync();

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

    private void BuildActionableSegments(List<RDAccount> accounts)
    {
        var today = DateTime.Today;
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
            .Where(a => a.GetMonthPaidNumber() == 1)
            .OrderBy(a => a.AccountNo)
            .ToList();

        var aboutToFreeze = accounts
            .Where(a =>
            {
                var due = a.GetNextInstallmentDate();
                return due.HasValue && due.Value.Date < today.AddDays(-90);
            })
            .OrderBy(a => a.GetNextInstallmentDate())
            .ToList();

        var matured = accounts
            .Where(a => a.GetMonthPaidNumber() >= 60)
            .OrderByDescending(a => a.GetMonthPaidNumber())
            .ThenBy(a => a.AccountNo)
            .ToList();

        _segmentAccounts["pending-month"] = pendingThisMonth;
        _segmentAccounts["next-month"] = nextMonthCollection;
        _segmentAccounts["advanced-paid"] = advancedPaid;
        _segmentAccounts["new-accounts"] = newAccounts30Days;
        _segmentAccounts["freeze-risk"] = aboutToFreeze;
        _segmentAccounts["matured"] = matured;
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
        AboutToFreezeCount = aboutToFreeze.Count;
        AboutToFreezeAmount = aboutToFreeze.Sum(a => a.GetAmount());
        MaturedCount = matured.Count;
        MaturedAmount = matured.Sum(a => a.GetAmount());
        FirstHalfPendingWindowCount = firstHalfPendingWindow.Count;
        FirstHalfPendingWindowAmount = firstHalfPendingWindow.Sum(a => a.GetAmount());
        SecondHalfPendingWindowCount = secondHalfPendingWindow.Count;
        SecondHalfPendingWindowAmount = secondHalfPendingWindow.Sum(a => a.GetAmount());
        FirstHalfDepositedWindowCount = firstHalfDepositedWindow.Count;
        FirstHalfDepositedWindowAmount = firstHalfDepositedWindow.Sum(a => a.GetAmount());
        SecondHalfDepositedWindowCount = secondHalfDepositedWindow.Count;
        SecondHalfDepositedWindowAmount = secondHalfDepositedWindow.Sum(a => a.GetAmount());
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
            "new-accounts" => "New Accounts (Month Paid Upto = 1)",
            "freeze-risk" => "About To Freeze Accounts",
            "matured" => "Matured Accounts",
            "pending-first-half" => "Pending Accounts (1st–15th)",
            "pending-second-half" => "Pending Accounts (16th–End of Month)",
            "deposited-first-half" => "Deposited Accounts (1st–15th)",
            "deposited-second-half" => "Deposited Accounts (16th–End of Month)",
            "due-soon" => "Accounts Due Within 30 Days",
            _ => "Accounts"
        };
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
                await LoadDataAsync();
                UpdateStatus = "Dashboard refreshed!";
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

            UpdateStatus = $"Syncing {accounts.Count} account rows to mobile API...";
            var (success, message) = await _mobileSyncService.PushRdAccountsAsync(
                apiUrl,
                apiKey,
                accounts,
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
}
