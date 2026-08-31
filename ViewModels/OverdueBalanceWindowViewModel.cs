using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia;
using AgentBuddy.Models;
using AgentBuddy.Services;
using ReactiveUI;

namespace AgentBuddy.ViewModels;

public sealed class OverdueBalanceWindowViewModel : ViewModelBase
{
    private const int StarterMonthLimit = 6;
    private const string SortLowestBalance = "lowest-balance";
    private const string SortHighestBalance = "highest-balance";
    private const string SortAccountNumber = "account-number";
    private const string SortName = "name";
    private const string SortNextDueOldest = "next-due-oldest";
    private const string SortAmountHighToLow = "amount-high-low";

    private readonly DatabaseService _databaseService;
    private string _searchQuery = string.Empty;
    private FilterOption? _selectedMonthFilter;
    private SortOption? _selectedSortOption;
    private string _statusMessage = T("L_Balance_Loading", "Loading overdue accounts...");
    private bool _isLoading;

    public OverdueBalanceWindowViewModel(DatabaseService databaseService)
    {
        _databaseService = databaseService;
        Accounts = new ObservableCollection<OverdueBalanceAccount>();
        FilteredAccounts = new ObservableCollection<OverdueBalanceAccount>();
        MonthFilterOptions = new ObservableCollection<FilterOption>();
        SortOptions = new ObservableCollection<SortOption>
        {
            new(SortLowestBalance, T("L_Balance_SortLowest", "Lowest balance first")),
            new(SortHighestBalance, T("L_Balance_SortHighest", "Highest balance first")),
            new(SortAccountNumber, T("L_Balance_SortAccount", "Account number")),
            new(SortName, T("L_Balance_SortName", "Name")),
            new(SortNextDueOldest, T("L_Balance_SortNextDue", "Next due oldest first")),
            new(SortAmountHighToLow, T("L_Balance_SortAmount", "Amount high to low"))
        };
        _selectedSortOption = SortOptions.First();

        RefreshCommand = ReactiveCommand.CreateFromTask(LoadAsync);
    }

    public ObservableCollection<OverdueBalanceAccount> Accounts { get; }
    public ObservableCollection<OverdueBalanceAccount> FilteredAccounts { get; }
    public ObservableCollection<FilterOption> MonthFilterOptions { get; }
    public ObservableCollection<SortOption> SortOptions { get; }
    public ReactiveCommand<Unit, Unit> RefreshCommand { get; }

    public int Count => FilteredAccounts.Count;
    public int MaxBalanceMonths => Accounts.Count == 0 ? 0 : Accounts.Max(a => a.BalanceMonths);
    public decimal TotalBalanceAmount => FilteredAccounts.Sum(a => a.BalanceAmount);
    public bool HasAccounts => Accounts.Count > 0;
    public string CountDisplay => string.Format(T("L_Balance_ShownFormat", "{0} shown"), Count);
    public string TotalBalanceDisplay => string.Format(T("L_Balance_TotalFormat", "Balance Rs. {0:N0}"), TotalBalanceAmount);
    public string StarterSummaryTitle => T("L_Balance_DefaultSummary", "Default summary");
    public string StarterSummaryText => string.Format(
        T("L_Balance_DefaultSummaryBody", "Starter view shows accounts up to {0} months. Higher month filters are still available."),
        StarterMonthLimit);
    public string SummaryText => HasAccounts
        ? string.Format(T("L_Balance_SummaryFormat", "{0} account(s), highest balance {1} month(s)"), Accounts.Count, MaxBalanceMonths)
        : T("L_Balance_Empty", "No overdue balance accounts found.");

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            this.RaiseAndSetIfChanged(ref _searchQuery, value);
            ApplyFilterAndSort();
        }
    }

    public FilterOption? SelectedMonthFilter
    {
        get => _selectedMonthFilter;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedMonthFilter, value ?? MonthFilterOptions.FirstOrDefault());
            ApplyFilterAndSort();
        }
    }

    public SortOption? SelectedSortOption
    {
        get => _selectedSortOption;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedSortOption, value ?? SortOptions.FirstOrDefault());
            ApplyFilterAndSort();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
    }

    public bool IsLoading
    {
        get => _isLoading;
        set => this.RaiseAndSetIfChanged(ref _isLoading, value);
    }

    public async Task LoadAsync()
    {
        IsLoading = true;
        StatusMessage = T("L_Balance_Loading", "Loading overdue accounts...");

        try
        {
            var accounts = await _databaseService.GetAllActiveAccountsAsync();
            var overdueAccounts = accounts
                .Select(account => new OverdueBalanceAccount
                {
                    Account = account,
                    BalanceMonths = CalculateBalanceMonths(account, DateTime.Today)
                })
                .Where(item => item.BalanceMonths > 0)
                .ToList();

            Accounts.Clear();
            foreach (var account in overdueAccounts)
            {
                Accounts.Add(account);
            }

            RebuildMonthOptions();
            ApplyFilterAndSort();
            StatusMessage = HasAccounts
                ? string.Format(T("L_Balance_ReadyFormat", "Ready. Showing accounts up to {0} months first. Highest balance is {1} month(s)."), StarterMonthLimit, MaxBalanceMonths)
                : T("L_Balance_Empty", "No overdue balance accounts found.");
        }
        catch (Exception ex)
        {
            StatusMessage = string.Format(T("L_Balance_LoadFailedFormat", "Could not load overdue accounts: {0}"), ex.Message);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private static int CalculateBalanceMonths(RDAccount account, DateTime asOfDate)
    {
        var dueDate = account.GetNextInstallmentDate();
        if (!dueDate.HasValue)
        {
            return 0;
        }

        var dueMonth = new DateTime(dueDate.Value.Year, dueDate.Value.Month, 1);
        var currentMonth = new DateTime(asOfDate.Year, asOfDate.Month, 1);
        if (dueMonth > currentMonth)
        {
            return 0;
        }

        var months = ((currentMonth.Year - dueMonth.Year) * 12) + currentMonth.Month - dueMonth.Month + 1;
        var paid = Math.Max(0, account.GetMonthPaidNumber());
        var remaining = Math.Max(0, 120 - paid);
        if (remaining > 0)
        {
            months = Math.Min(months, remaining);
        }

        return Math.Max(0, months);
    }

    private void RebuildMonthOptions()
    {
        var previous = SelectedMonthFilter;
        MonthFilterOptions.Clear();
        MonthFilterOptions.Add(FilterOption.All(T("L_Balance_FilterAll", "All months")));

        var max = MaxBalanceMonths;
        if (max >= StarterMonthLimit)
        {
            MonthFilterOptions.Add(FilterOption.UpTo(
                StarterMonthLimit,
                string.Format(T("L_Balance_FilterUpToFormat", "Up to {0} months"), StarterMonthLimit)));
        }

        for (var month = 1; month <= max; month++)
        {
            MonthFilterOptions.Add(FilterOption.Exact(
                month,
                month == 1
                    ? T("L_Balance_FilterOneMonth", "1 month")
                    : string.Format(T("L_Balance_FilterMonthsFormat", "{0} months"), month)));
        }

        _selectedMonthFilter = FindEquivalentFilter(previous)
            ?? MonthFilterOptions.FirstOrDefault(option => option.Mode == FilterMode.UpTo && option.Months == StarterMonthLimit)
            ?? MonthFilterOptions.FirstOrDefault();
        this.RaisePropertyChanged(nameof(SelectedMonthFilter));
        this.RaisePropertyChanged(nameof(MaxBalanceMonths));
        this.RaisePropertyChanged(nameof(HasAccounts));
        this.RaisePropertyChanged(nameof(SummaryText));
        this.RaisePropertyChanged(nameof(StarterSummaryTitle));
        this.RaisePropertyChanged(nameof(StarterSummaryText));
    }

    private void ApplyFilterAndSort()
    {
        IEnumerable<OverdueBalanceAccount> query = Accounts;
        var search = (SearchQuery ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(a =>
                a.AccountNo.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                a.AccountName.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        if (SelectedMonthFilter is { Mode: FilterMode.Exact, Months: > 0 } exactFilter)
        {
            query = query.Where(a => a.BalanceMonths == exactFilter.Months);
        }
        else if (SelectedMonthFilter is { Mode: FilterMode.UpTo, Months: > 0 } upToFilter)
        {
            query = query.Where(a => a.BalanceMonths <= upToFilter.Months);
        }

        query = SelectedSortOption?.Key switch
        {
            SortHighestBalance => query.OrderByDescending(a => a.BalanceMonths).ThenBy(a => a.AccountNo),
            SortAccountNumber => query.OrderBy(a => a.AccountNo),
            SortName => query.OrderBy(a => a.AccountName).ThenBy(a => a.AccountNo),
            SortNextDueOldest => query.OrderBy(a => a.Account.GetNextInstallmentDate() ?? DateTime.MaxValue).ThenBy(a => a.AccountNo),
            SortAmountHighToLow => query.OrderByDescending(a => a.Amount).ThenByDescending(a => a.BalanceMonths),
            _ => query.OrderBy(a => a.BalanceMonths).ThenBy(a => a.AccountNo)
        };

        FilteredAccounts.Clear();
        foreach (var item in query)
        {
            FilteredAccounts.Add(item);
        }

        RaiseFilteredSummary();
    }

    private FilterOption? FindEquivalentFilter(FilterOption? previous)
    {
        if (previous == null)
        {
            return null;
        }

        return MonthFilterOptions.FirstOrDefault(option =>
            option.Mode == previous.Mode &&
            option.Months == previous.Months);
    }

    private void RaiseFilteredSummary()
    {
        this.RaisePropertyChanged(nameof(Count));
        this.RaisePropertyChanged(nameof(TotalBalanceAmount));
        this.RaisePropertyChanged(nameof(CountDisplay));
        this.RaisePropertyChanged(nameof(TotalBalanceDisplay));
    }

    private static string T(string key, string fallback)
    {
        if (Application.Current?.TryGetResource(key, Application.Current.ActualThemeVariant, out var value) == true &&
            value is string text &&
            !string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        return fallback;
    }

    public sealed record FilterOption(FilterMode Mode, int Months, string Display)
    {
        public static FilterOption All(string display) => new(FilterMode.All, 0, display);
        public static FilterOption UpTo(int months, string display) => new(FilterMode.UpTo, months, display);
        public static FilterOption Exact(int months, string display) => new(FilterMode.Exact, months, display);
        public override string ToString() => Display;
    }

    public sealed record SortOption(string Key, string Display)
    {
        public override string ToString() => Display;
    }

    public enum FilterMode
    {
        All,
        UpTo,
        Exact
    }
}
