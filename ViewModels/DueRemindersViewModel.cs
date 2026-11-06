using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Text;
using System.Threading.Tasks;
using ReactiveUI;
using AgentBuddy.Services;

namespace AgentBuddy.ViewModels;

public sealed class DueRemindersViewModel : ViewModelBase
{
    private readonly WhatsAppReminderService _reminderService;
    private ObservableCollection<DueReminderItem> _items = new();
    private List<DueReminderItem> _allReminders = new();
    private string _searchQuery = string.Empty;
    private int _filterDays = 30;
    private bool _isLoading = true;
    private decimal _totalDueAmount;
    private int _totalDueCount;

    public DueRemindersViewModel(WhatsAppReminderService reminderService)
    {
        _reminderService = reminderService;

        RefreshCommand = ReactiveCommand.CreateFromTask(LoadRemindersAsync);
        OpenWhatsAppCommand = ReactiveCommand.Create<DueReminderItem>(OpenWhatsApp);

        _ = LoadRemindersAsync();
    }

    public ObservableCollection<DueReminderItem> Items
    {
        get => _items;
        private set => this.RaiseAndSetIfChanged(ref _items, value);
    }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            this.RaiseAndSetIfChanged(ref _searchQuery, value);
            ApplyFilter();
        }
    }

    public int FilterDays
    {
        get => _filterDays;
        set
        {
            this.RaiseAndSetIfChanged(ref _filterDays, value);
            _ = LoadRemindersAsync();
        }
    }

    public bool IsLoading
    {
        get => _isLoading;
        private set => this.RaiseAndSetIfChanged(ref _isLoading, value);
    }

    public decimal TotalDueAmount
    {
        get => _totalDueAmount;
        private set => this.RaiseAndSetIfChanged(ref _totalDueAmount, value);
    }

    public int TotalDueCount
    {
        get => _totalDueCount;
        private set => this.RaiseAndSetIfChanged(ref _totalDueCount, value);
    }

    public ReactiveCommand<Unit, Unit> RefreshCommand { get; }
    public ReactiveCommand<DueReminderItem, Unit> OpenWhatsAppCommand { get; }

    public async Task LoadRemindersAsync()
    {
        IsLoading = true;
        try
        {
            _allReminders = await _reminderService.GetDueRemindersAsync(_filterDays);
            ApplyFilter();
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void ApplyFilter()
    {
        var filtered = _allReminders.AsEnumerable();
        if (!string.IsNullOrWhiteSpace(SearchQuery))
        {
            var q = SearchQuery.Trim().ToLowerInvariant();
            filtered = filtered.Where(x =>
                x.AccountName.ToLowerInvariant().Contains(q) ||
                x.AccountNo.Contains(q) ||
                x.MonthPaidUpto.ToLowerInvariant().Contains(q));
        }

        var list = filtered.ToList();
        Items = new ObservableCollection<DueReminderItem>(list);
        TotalDueCount = list.Count;
        TotalDueAmount = list.Sum(x => x.Amount);
    }

    public void OpenWhatsApp(DueReminderItem? item)
    {
        if (item == null) return;
        WhatsAppReminderService.LaunchWhatsAppUrl(item.WhatsAppUrl);
    }

    public string GetAllRemindersSummary()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"DUE DEPOSIT REMINDERS - {DateTime.Now:dd-MM-yyyy}");
        sb.AppendLine($"Total Accounts: {TotalDueCount} | Total Due: ₹{TotalDueAmount:N0}");
        sb.AppendLine("--------------------------------------------------");
        foreach (var item in Items)
        {
            sb.AppendLine($"{item.AccountName} (A/C: {item.AccountNo}) - Due: ₹{item.Amount:N0} for {item.MonthPaidUpto}");
        }
        return sb.ToString();
    }
}
