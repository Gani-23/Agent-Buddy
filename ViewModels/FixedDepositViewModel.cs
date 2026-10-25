using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia.Input.Platform;
using ReactiveUI;
using AgentBuddy.Models;
using AgentBuddy.Services;

namespace AgentBuddy.ViewModels;

public sealed class FixedDepositViewModel : ViewModelBase
{
    private readonly DatabaseService _databaseService;
    private readonly NotificationService? _notificationService;

    private bool _isDarkTheme;
    private string _searchText = string.Empty;
    private string _selectedSchemeFilter = "All";
    private string _selectedStatusFilter = "All";

    // Calculator inputs
    private string _calcSchemeType = "5-Year TD";
    private string _calcDepositAmountText = "100000";
    private string _calcInterestRateText = "7.5";
    private DateTimeOffset _calcStartDate = DateTimeOffset.Now;

    // Calculator outputs
    private string _calcMaturityAmountDisplay = "Rs. 1,44,995";
    private string _calcTotalInterestDisplay = "Rs. 44,995";
    private string _calcPayoutDisplay = "At Maturity (Compounded Quarterly)";
    private string _calcMaturityDateDisplay = DateTime.Today.AddYears(5).ToString("dd-MMM-yyyy");

    // Entry Form
    private bool _isEntryPanelOpen;
    private string _entryAccountNo = string.Empty;
    private string _entryCustomerName = string.Empty;
    private string _entryCustomerPhone = string.Empty;
    private string _entrySchemeType = "5-Year TD";
    private string _entryDepositAmount = "100000";
    private string _entryInterestRate = "7.5";
    private DateTimeOffset _entryDepositDate = DateTimeOffset.Now;
    private string _entryNotes = string.Empty;
    private string _entryValidationMessage = string.Empty;

    public FixedDepositViewModel(DatabaseService databaseService, NotificationService? notificationService = null)
    {
        _databaseService = databaseService;
        _notificationService = notificationService;

        Accounts = new ObservableCollection<FixedDepositAccount>();
        FilteredAccounts = new ObservableCollection<FixedDepositAccount>();

        AddAccountCommand = ReactiveCommand.Create(() => { IsEntryPanelOpen = true; });
        CloseEntryPanelCommand = ReactiveCommand.Create(() => { IsEntryPanelOpen = false; });
        SaveEntryCommand = ReactiveCommand.CreateFromTask(SaveEntryAsync);
        RefreshCommand = ReactiveCommand.CreateFromTask(LoadAccountsAsync);
        DeleteAccountCommand = ReactiveCommand.CreateFromTask<FixedDepositAccount?>(DeleteAccountAsync);
        CopyReminderCommand = ReactiveCommand.CreateFromTask<FixedDepositAccount?>(CopyWhatsAppReminderAsync);

        Recalculate();
        _ = LoadAccountsAsync();
    }

    public bool IsDarkTheme
    {
        get => _isDarkTheme;
        set => this.RaiseAndSetIfChanged(ref _isDarkTheme, value);
    }

    public ObservableCollection<FixedDepositAccount> Accounts { get; }
    public ObservableCollection<FixedDepositAccount> FilteredAccounts { get; }

    public IReadOnlyList<string> SchemeOptions { get; } = new[]
    {
        "1-Year TD",
        "2-Year TD",
        "3-Year TD",
        "5-Year TD",
        "MIS (Monthly Income)",
        "SCSS (Senior Citizen)",
        "NSC (5-Year)",
        "KVP (Doubles)"
    };

    public IReadOnlyList<string> SchemeFilterOptions { get; } = new[]
    {
        "All",
        "1-Year TD",
        "2-Year TD",
        "3-Year TD",
        "5-Year TD",
        "MIS (Monthly Income)",
        "SCSS (Senior Citizen)",
        "NSC (5-Year)",
        "KVP (Doubles)"
    };

    public IReadOnlyList<string> StatusFilterOptions { get; } = new[]
    {
        "All",
        "Active",
        "Maturing Soon",
        "Matured"
    };

    public ReactiveCommand<Unit, Unit> AddAccountCommand { get; }
    public ReactiveCommand<Unit, Unit> CloseEntryPanelCommand { get; }
    public ReactiveCommand<Unit, Unit> SaveEntryCommand { get; }
    public ReactiveCommand<Unit, Unit> RefreshCommand { get; }
    public ReactiveCommand<FixedDepositAccount?, Unit> DeleteAccountCommand { get; }
    public ReactiveCommand<FixedDepositAccount?, Unit> CopyReminderCommand { get; }

    #region Metrics
    public decimal TotalDepositAmount => Accounts.Where(a => a.Status == "Active").Sum(a => a.DepositAmount);
    public decimal TotalMaturityAmount => Accounts.Where(a => a.Status == "Active").Sum(a => a.MaturityAmount);
    public int ActiveAccountCount => Accounts.Count(a => a.Status == "Active");
    public int MaturingSoonCount => Accounts.Count(a => a.IsMaturingSoon);
    public int MaturedCount => Accounts.Count(a => a.IsMatured);

    public string FormattedTotalDeposit => $"Rs. {TotalDepositAmount:N0}";
    public string FormattedTotalMaturity => $"Rs. {TotalMaturityAmount:N0}";
    #endregion

    #region Filters & Search
    public string SearchText
    {
        get => _searchText;
        set
        {
            this.RaiseAndSetIfChanged(ref _searchText, value);
            ApplyFilters();
        }
    }

    public string SelectedSchemeFilter
    {
        get => _selectedSchemeFilter;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedSchemeFilter, value);
            ApplyFilters();
        }
    }

    public string SelectedStatusFilter
    {
        get => _selectedStatusFilter;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedStatusFilter, value);
            ApplyFilters();
        }
    }

    public bool HasFilteredAccounts => FilteredAccounts.Count > 0;
    #endregion

    #region Calculator Properties
    public string CalcSchemeType
    {
        get => _calcSchemeType;
        set
        {
            if (_calcSchemeType != value)
            {
                this.RaiseAndSetIfChanged(ref _calcSchemeType, value);
                CalcInterestRateText = FixedDepositCalculator.GetDefaultRateForScheme(value).ToString("F1");
                Recalculate();
            }
        }
    }

    public string CalcDepositAmountText
    {
        get => _calcDepositAmountText;
        set
        {
            this.RaiseAndSetIfChanged(ref _calcDepositAmountText, value);
            Recalculate();
        }
    }

    public string CalcInterestRateText
    {
        get => _calcInterestRateText;
        set
        {
            this.RaiseAndSetIfChanged(ref _calcInterestRateText, value);
            Recalculate();
        }
    }

    public DateTimeOffset CalcStartDate
    {
        get => _calcStartDate;
        set
        {
            this.RaiseAndSetIfChanged(ref _calcStartDate, value);
            Recalculate();
        }
    }

    public string CalcMaturityAmountDisplay
    {
        get => _calcMaturityAmountDisplay;
        private set => this.RaiseAndSetIfChanged(ref _calcMaturityAmountDisplay, value);
    }

    public string CalcTotalInterestDisplay
    {
        get => _calcTotalInterestDisplay;
        private set => this.RaiseAndSetIfChanged(ref _calcTotalInterestDisplay, value);
    }

    public string CalcPayoutDisplay
    {
        get => _calcPayoutDisplay;
        private set => this.RaiseAndSetIfChanged(ref _calcPayoutDisplay, value);
    }

    public string CalcMaturityDateDisplay
    {
        get => _calcMaturityDateDisplay;
        private set => this.RaiseAndSetIfChanged(ref _calcMaturityDateDisplay, value);
    }
    #endregion

    #region Entry Panel Properties
    public bool IsEntryPanelOpen
    {
        get => _isEntryPanelOpen;
        set => this.RaiseAndSetIfChanged(ref _isEntryPanelOpen, value);
    }

    public string EntryAccountNo
    {
        get => _entryAccountNo;
        set => this.RaiseAndSetIfChanged(ref _entryAccountNo, value);
    }

    public string EntryCustomerName
    {
        get => _entryCustomerName;
        set => this.RaiseAndSetIfChanged(ref _entryCustomerName, value);
    }

    public string EntryCustomerPhone
    {
        get => _entryCustomerPhone;
        set => this.RaiseAndSetIfChanged(ref _entryCustomerPhone, value);
    }

    public string EntrySchemeType
    {
        get => _entrySchemeType;
        set
        {
            if (_entrySchemeType != value)
            {
                this.RaiseAndSetIfChanged(ref _entrySchemeType, value);
                EntryInterestRate = FixedDepositCalculator.GetDefaultRateForScheme(value).ToString("F1");
            }
        }
    }

    public string EntryDepositAmount
    {
        get => _entryDepositAmount;
        set => this.RaiseAndSetIfChanged(ref _entryDepositAmount, value);
    }

    public string EntryInterestRate
    {
        get => _entryInterestRate;
        set => this.RaiseAndSetIfChanged(ref _entryInterestRate, value);
    }

    public DateTimeOffset EntryDepositDate
    {
        get => _entryDepositDate;
        set => this.RaiseAndSetIfChanged(ref _entryDepositDate, value);
    }

    public string EntryNotes
    {
        get => _entryNotes;
        set => this.RaiseAndSetIfChanged(ref _entryNotes, value);
    }

    public string EntryValidationMessage
    {
        get => _entryValidationMessage;
        set => this.RaiseAndSetIfChanged(ref _entryValidationMessage, value);
    }

    public bool HasEntryValidationMessage => !string.IsNullOrWhiteSpace(EntryValidationMessage);
    #endregion

    public async Task LoadAccountsAsync()
    {
        var list = await _databaseService.GetAllFixedDepositsAsync();
        Accounts.Clear();
        foreach (var item in list)
        {
            Accounts.Add(item);
        }
        ApplyFilters();
        RaiseMetrics();
    }

    private void ApplyFilters()
    {
        FilteredAccounts.Clear();
        var query = (SearchText ?? string.Empty).Trim().ToLowerInvariant();

        foreach (var item in Accounts)
        {
            if (SelectedSchemeFilter != "All" && item.SchemeType != SelectedSchemeFilter)
            {
                continue;
            }

            if (SelectedStatusFilter != "All")
            {
                if (SelectedStatusFilter == "Active" && item.Status != "Active") continue;
                if (SelectedStatusFilter == "Maturing Soon" && !item.IsMaturingSoon) continue;
                if (SelectedStatusFilter == "Matured" && !item.IsMatured) continue;
            }

            if (!string.IsNullOrWhiteSpace(query))
            {
                var matchAccount = item.AccountNo.ToLowerInvariant().Contains(query);
                var matchName = item.CustomerName.ToLowerInvariant().Contains(query);
                var matchPhone = (item.CustomerPhone ?? string.Empty).Contains(query);
                if (!matchAccount && !matchName && !matchPhone)
                {
                    continue;
                }
            }

            FilteredAccounts.Add(item);
        }

        this.RaisePropertyChanged(nameof(HasFilteredAccounts));
    }

    private void Recalculate()
    {
        if (!decimal.TryParse(CalcDepositAmountText, out var principal) || principal <= 0)
        {
            principal = 100000;
        }

        if (!decimal.TryParse(CalcInterestRateText, out var rate) || rate <= 0)
        {
            rate = FixedDepositCalculator.GetDefaultRateForScheme(CalcSchemeType);
        }

        var res = FixedDepositCalculator.Calculate(CalcSchemeType, principal, rate, CalcStartDate.DateTime);
        CalcMaturityAmountDisplay = $"Rs. {res.MaturityAmount:N0}";
        CalcTotalInterestDisplay = $"Rs. {res.TotalInterest:N0}";
        CalcPayoutDisplay = $"{res.PayoutFrequency} (Payout: Rs. {res.PeriodicPayout:N0})";
        CalcMaturityDateDisplay = res.MaturityDate.ToString("dd-MMM-yyyy");
    }

    private async Task SaveEntryAsync()
    {
        EntryValidationMessage = string.Empty;

        var accountNo = (EntryAccountNo ?? string.Empty).Trim();
        var customerName = (EntryCustomerName ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(accountNo))
        {
            EntryValidationMessage = "Enter an account number.";
            return;
        }

        if (string.IsNullOrWhiteSpace(customerName))
        {
            EntryValidationMessage = "Enter customer name.";
            return;
        }

        if (!decimal.TryParse(EntryDepositAmount, out var principal) || principal <= 0)
        {
            EntryValidationMessage = "Enter a valid deposit amount.";
            return;
        }

        if (!decimal.TryParse(EntryInterestRate, out var rate) || rate <= 0)
        {
            EntryValidationMessage = "Enter a valid interest rate.";
            return;
        }

        var calc = FixedDepositCalculator.Calculate(EntrySchemeType, principal, rate, EntryDepositDate.DateTime);

        var account = new FixedDepositAccount
        {
            AccountNo = accountNo,
            CustomerName = customerName,
            CustomerPhone = string.IsNullOrWhiteSpace(EntryCustomerPhone) ? null : EntryCustomerPhone.Trim(),
            SchemeType = EntrySchemeType,
            DepositAmount = principal,
            InterestRate = rate,
            DepositDate = EntryDepositDate.DateTime,
            MaturityDate = calc.MaturityDate,
            MaturityAmount = calc.MaturityAmount,
            AnnualInterest = calc.PeriodicPayout,
            Status = "Active",
            Notes = string.IsNullOrWhiteSpace(EntryNotes) ? null : EntryNotes.Trim()
        };

        var saved = await _databaseService.SaveFixedDepositAsync(account);
        if (saved)
        {
            _notificationService?.Success("Fixed Deposit Saved", $"{account.SchemeType} for {customerName} added.");
            IsEntryPanelOpen = false;
            ClearEntryForm();
            await LoadAccountsAsync();
        }
        else
        {
            EntryValidationMessage = "Failed to save record into database.";
        }
    }

    private void ClearEntryForm()
    {
        EntryAccountNo = string.Empty;
        EntryCustomerName = string.Empty;
        EntryCustomerPhone = string.Empty;
        EntrySchemeType = "5-Year TD";
        EntryDepositAmount = "100000";
        EntryInterestRate = "7.5";
        EntryDepositDate = DateTimeOffset.Now;
        EntryNotes = string.Empty;
        EntryValidationMessage = string.Empty;
    }

    private async Task DeleteAccountAsync(FixedDepositAccount? account)
    {
        if (account == null) return;
        var deleted = await _databaseService.DeleteFixedDepositAsync(account.Id);
        if (deleted)
        {
            Accounts.Remove(account);
            FilteredAccounts.Remove(account);
            RaiseMetrics();
            _notificationService?.Info("Account Removed", $"Removed {account.AccountNo} from records.");
        }
    }

    private async Task CopyWhatsAppReminderAsync(FixedDepositAccount? account)
    {
        if (account == null) return;

        var message =
            $"Namaste {account.CustomerName} ji!\n" +
            $"This is regarding your Post Office {account.SchemeType} A/C No: {account.AccountNo}.\n" +
            $"• Deposit Amount: Rs. {account.DepositAmount:N0}\n" +
            $"• Maturity Date: {account.MaturityDate:dd-MMM-yyyy}\n" +
            $"• Maturity / Value: Rs. {account.MaturityAmount:N0}\n\n" +
            $"Your Fixed Deposit is maturing soon. Please contact me for reinvestment, renewal, or interest collection assistance.";

        if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(message);
            _notificationService?.Success("Reminder Copied", "WhatsApp maturity message copied to clipboard!");
        }
    }

    private void RaiseMetrics()
    {
        this.RaisePropertyChanged(nameof(TotalDepositAmount));
        this.RaisePropertyChanged(nameof(TotalMaturityAmount));
        this.RaisePropertyChanged(nameof(ActiveAccountCount));
        this.RaisePropertyChanged(nameof(MaturingSoonCount));
        this.RaisePropertyChanged(nameof(MaturedCount));
        this.RaisePropertyChanged(nameof(FormattedTotalDeposit));
        this.RaisePropertyChanged(nameof(FormattedTotalMaturity));
    }
}
