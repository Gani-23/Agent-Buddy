using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ReactiveUI;
using AgentBuddy.Models;
using Avalonia.Input.Platform;

namespace AgentBuddy.ViewModels;

public class SegmentAccountsWindowViewModel : ViewModelBase
{
    private string _searchQuery = string.Empty;

    public SegmentAccountsWindowViewModel(DashboardViewModel dashboardViewModel, string? initialSegmentKey = null)
    {
        DashboardContext = dashboardViewModel;
        
        if (!string.IsNullOrEmpty(initialSegmentKey))
        {
            var matchingOption = DashboardContext.DefaultSummaryOptions.FirstOrDefault(o => o.SegmentKey == initialSegmentKey);
            if (matchingOption != null)
            {
                DashboardContext.SelectedDefaultSummaryOption = matchingOption;
            }
        }

        Accounts = new ObservableCollection<RDAccount>();
        FilteredAccounts = new ObservableCollection<RDAccount>();

        RefreshAccounts();
        
        // Listen to changes in DashboardContext to refresh list dynamically
        DashboardContext.PropertyChanged += (s, e) => {
            if (e.PropertyName == nameof(DashboardViewModel.DefaultSummaryTitle) || 
                e.PropertyName == nameof(DashboardViewModel.SelectedDefaultSummaryOption) || 
                e.PropertyName == nameof(DashboardViewModel.SelectedDefaultSummarySortOption))
            {
                RefreshAccounts();
            }
        };
        
        SendWhatsAppCommand = ReactiveCommand.Create<RDAccount>(SendWhatsAppReminder);
        SendBulkWhatsAppCommand = ReactiveCommand.CreateFromTask(SendBulkWhatsAppReminder);
    }
    
    public DashboardViewModel DashboardContext { get; }
    
    private void RefreshAccounts()
    {
        var list = DashboardContext.GetDefaultSummaryAccounts();
        Accounts.Clear();
        foreach (var acc in list) Accounts.Add(acc);
        ApplyFilter();
        this.RaisePropertyChanged(nameof(Title));
        this.RaisePropertyChanged(nameof(SegmentKey));
        this.RaisePropertyChanged(nameof(IsNewAccountsSegment));
        this.RaisePropertyChanged(nameof(SegmentHint));
    }

    public ReactiveCommand<RDAccount, System.Reactive.Unit> SendWhatsAppCommand { get; }
    public ReactiveCommand<System.Reactive.Unit, System.Reactive.Unit> SendBulkWhatsAppCommand { get; }

    private async System.Threading.Tasks.Task SendBulkWhatsAppReminder()
    {
        if (FilteredAccounts == null || !FilteredAccounts.Any()) return;
        
        var messageBuilder = new System.Text.StringBuilder();
        messageBuilder.AppendLine($"*{Title} Summary*");
        messageBuilder.AppendLine($"Total: {Count} accounts (Rs. {TotalAmount:N0})");
        messageBuilder.AppendLine();

        int i = 1;
        // Don't limit to 50 anymore, take all of them!
        foreach (var account in FilteredAccounts) 
        {
            var date = account.GetNextInstallmentDate()?.ToString("dd-MMM") ?? account.NextInstallmentDate;
            messageBuilder.AppendLine($"{i}. {account.AccountName} - Rs.{account.Denomination} (Due: {date})");
            i++;
        }

        var fullMessage = messageBuilder.ToString();
        var encodedMessage = System.Uri.EscapeDataString(fullMessage);
        
        if (encodedMessage.Length > 1500)
        {
            if (Avalonia.Application.Current?.ApplicationLifetime is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                var clipboard = desktop.MainWindow?.Clipboard;
                if (clipboard != null)
                {
                    await clipboard.SetTextAsync(fullMessage);
                }
            }
            encodedMessage = System.Uri.EscapeDataString($"*{Title} Summary*\n\n(The list is too long for a single link. The full list has been copied to your clipboard. Please Paste it here!)");
        }

        var phoneQuery = string.IsNullOrWhiteSpace(DashboardContext.AgentPhoneNumber) ? "" : $"phone={DashboardContext.AgentPhoneNumber}&";
        var url = $"https://api.whatsapp.com/send?{phoneQuery}text={encodedMessage}";
        
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (System.Exception)
        {
        }
    }

    private void SendWhatsAppReminder(RDAccount account)
    {
        if (account == null) return;
        var accountMasked = account.AccountNo.Length >= 4 
            ? "***" + account.AccountNo.Substring(account.AccountNo.Length - 4) 
            : account.AccountNo;
        var date = account.GetNextInstallmentDate()?.ToString("dd-MMM-yyyy") ?? account.NextInstallmentDate;
        var message = $"Dear {account.AccountName}, your Post Office RD Account {accountMasked} installment of Rs. {account.Denomination} is due on {date}. Please deposit soon to avoid penalty.";
        
        var encodedMessage = System.Uri.EscapeDataString(message);
        var phoneQuery = string.IsNullOrWhiteSpace(DashboardContext.AgentPhoneNumber) ? "" : $"phone={DashboardContext.AgentPhoneNumber}&";
        var url = $"https://api.whatsapp.com/send?{phoneQuery}text={encodedMessage}";
        
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch (System.Exception)
        {
        }
    }

    public string Title => DashboardContext.DefaultSummaryTitle;
    public string SegmentKey => DashboardContext.SelectedDefaultSummaryKey;
    public bool IsNewAccountsSegment => string.Equals(SegmentKey, "new-accounts", System.StringComparison.OrdinalIgnoreCase);
    public string SegmentHint => IsNewAccountsSegment
        ? "Latest additions are shown first."
        : string.Empty;
    public int Count { get; private set; }
    public decimal TotalAmount { get; private set; }
    public ObservableCollection<RDAccount> Accounts { get; }
    public ObservableCollection<RDAccount> FilteredAccounts { get; }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            this.RaiseAndSetIfChanged(ref _searchQuery, value);
            ApplyFilter();
        }
    }

    private void ApplyFilter()
    {
        var query = (SearchQuery ?? string.Empty).Trim();
        var filtered = string.IsNullOrWhiteSpace(query)
            ? Accounts.ToList()
            : Accounts
                .Where(a =>
                    (!string.IsNullOrWhiteSpace(a.AccountNo) &&
                     a.AccountNo.Contains(query, System.StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrWhiteSpace(a.AccountName) &&
                     a.AccountName.Contains(query, System.StringComparison.OrdinalIgnoreCase)))
                .ToList();

        FilteredAccounts.Clear();
        foreach (var account in filtered)
        {
            FilteredAccounts.Add(account);
        }

        Count = FilteredAccounts.Count;
        TotalAmount = FilteredAccounts.Sum(a => a.GetAmount());
        this.RaisePropertyChanged(nameof(Count));
        this.RaisePropertyChanged(nameof(TotalAmount));
    }
}
