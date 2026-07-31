using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using AgentBuddy.Models;
using AgentBuddy.Services;
using ReactiveUI;

namespace AgentBuddy.ViewModels;

public sealed class OverdueBalanceWindowViewModel : ViewModelBase
{
    private const string AllMonthsOption = "All months";

    private readonly DatabaseService _databaseService;
    private string _searchQuery = string.Empty;
    private string _selectedMonthFilter = AllMonthsOption;
    private string _selectedSortOption = "Lowest balance first";
    private string _statusMessage = "Loading overdue accounts...";
    private bool _isLoading;

    public OverdueBalanceWindowViewModel(DatabaseService databaseService)
    {
        _databaseService = databaseService;
        Accounts = new ObservableCollection<OverdueBalanceAccount>();
        FilteredAccounts = new ObservableCollection<OverdueBalanceAccount>();
        MonthFilterOptions = new ObservableCollection<string> { AllMonthsOption };
        SortOptions = new ObservableCollection<string>
        {
            "Lowest balance first",
            "Highest balance first",
            "Account number",
            "Name",
            "Next due oldest first",
            "Amount high to low"
        };

        RefreshCommand = ReactiveCommand.CreateFromTask(LoadAsync);
    }

    public ObservableCollection<OverdueBalanceAccount> Accounts { get; }
    public ObservableCollection<OverdueBalanceAccount> FilteredAccounts { get; }
    public ObservableCollection<string> MonthFilterOptions { get; }
    public ObservableCollection<string> SortOptions { get; }
    public ReactiveCommand<Unit, Unit> RefreshCommand { get; }

    public int Count => FilteredAccounts.Count;
    public int MaxBalanceMonths => Accounts.Count == 0 ? 0 : Accounts.Max(a => a.BalanceMonths);
    public decimal TotalBalanceAmount => FilteredAccounts.Sum(a => a.BalanceAmount);
    public bool HasAccounts => Accounts.Count > 0;
    public string SummaryText => HasAccounts
        ? $"{Accounts.Count} account(s), highest balance {MaxBalanceMonths} month(s)"
        : "No overdue balance accounts found.";

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            this.RaiseAndSetIfChanged(ref _searchQuery, value);
            ApplyFilterAndSort();
        }
    }

    public string SelectedMonthFilter
    {
        get => _selectedMonthFilter;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedMonthFilter, string.IsNullOrWhiteSpace(value) ? AllMonthsOption : value);
            ApplyFilterAndSort();
        }
    }

    public string SelectedSortOption
    {
        get => _selectedSortOption;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedSortOption, value);
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
        StatusMessage = "Loading overdue accounts...";

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
                ? $"Ready. Showing lowest overdue balances first. Highest balance is {MaxBalanceMonths} month(s)."
                : "No overdue balance accounts found.";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Could not load overdue accounts: {ex.Message}";
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
        MonthFilterOptions.Add(AllMonthsOption);

        var max = MaxBalanceMonths;
        for (var month = 1; month <= max; month++)
        {
            MonthFilterOptions.Add(month == 1 ? "1 month" : $"{month} months");
        }

        _selectedMonthFilter = MonthFilterOptions.Contains(previous) ? previous : AllMonthsOption;
        this.RaisePropertyChanged(nameof(SelectedMonthFilter));
        this.RaisePropertyChanged(nameof(MaxBalanceMonths));
        this.RaisePropertyChanged(nameof(HasAccounts));
        this.RaisePropertyChanged(nameof(SummaryText));
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

        var selectedMonth = ParseSelectedMonth();
        if (selectedMonth > 0)
        {
            query = query.Where(a => a.BalanceMonths == selectedMonth);
        }

        query = SelectedSortOption switch
        {
            "Lowest balance first" => query.OrderBy(a => a.BalanceMonths).ThenBy(a => a.AccountNo),
            "Account number" => query.OrderBy(a => a.AccountNo),
            "Name" => query.OrderBy(a => a.AccountName).ThenBy(a => a.AccountNo),
            "Next due oldest first" => query.OrderBy(a => a.Account.GetNextInstallmentDate() ?? DateTime.MaxValue).ThenBy(a => a.AccountNo),
            "Amount high to low" => query.OrderByDescending(a => a.Amount).ThenByDescending(a => a.BalanceMonths),
            _ => query.OrderBy(a => a.BalanceMonths).ThenBy(a => a.AccountNo)
        };

        FilteredAccounts.Clear();
        foreach (var item in query)
        {
            FilteredAccounts.Add(item);
        }

        RaiseFilteredSummary();
    }

    private int ParseSelectedMonth()
    {
        var selected = (SelectedMonthFilter ?? string.Empty).Trim();
        if (selected.Equals(AllMonthsOption, StringComparison.OrdinalIgnoreCase))
        {
            return 0;
        }

        var first = selected.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
        return int.TryParse(first, out var month) ? month : 0;
    }

    private void RaiseFilteredSummary()
    {
        this.RaisePropertyChanged(nameof(Count));
        this.RaisePropertyChanged(nameof(TotalBalanceAmount));
    }
}
