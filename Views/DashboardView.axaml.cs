using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Input.Platform;
using AgentBuddy.ViewModels;
using System.Linq;

namespace AgentBuddy.Views;

public partial class DashboardView : UserControl
{
    public DashboardView()
    {
        InitializeComponent();
    }

    private DashboardViewModel? ViewModel => DataContext as DashboardViewModel;

    private async void OpenSegment_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string segmentKey)
        {
            await OpenSegmentAsync(segmentKey, focusSearch: false);
        }
    }

    private async void SearchSegment_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string segmentKey)
        {
            await OpenSegmentAsync(segmentKey, focusSearch: true);
        }
    }

    private async void PrintSegment_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || ViewModel is null)
        {
            return;
        }

        var (success, message) = await ViewModel.PrintSegmentAsync(button.Tag?.ToString());
        ViewModel.UpdateStatus = message;
    }

    private async void OpenDefaultSummary_Click(object? sender, RoutedEventArgs e)
    {
        await OpenDefaultSummaryAsync(focusSearch: false);
    }

    private async void SearchDefaultSummary_Click(object? sender, RoutedEventArgs e)
    {
        await OpenDefaultSummaryAsync(focusSearch: true);
    }



    private async void PrintDefaultSummary_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel is null)
        {
            return;
        }

        var (success, message) = await ViewModel.PrintDefaultSummaryAsync();
        ViewModel.UpdateStatus = message;
    }

    private async System.Threading.Tasks.Task OpenDefaultSummaryAsync(bool focusSearch)
    {
        if (ViewModel is null)
        {
            return;
        }

        var window = new SegmentAccountsWindow(ViewModel, ViewModel.IsDarkTheme, focusSearch, null, ViewModel.SelectedDefaultSummaryKey);
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner != null)
        {
            await window.ShowDialog(owner);
        }
        else
        {
            window.Show();
        }
    }

    private async System.Threading.Tasks.Task OpenSegmentAsync(string? segmentKey, bool focusSearch)
    {
        if (ViewModel is null)
            return;

        var window = new SegmentAccountsWindow(ViewModel, ViewModel.IsDarkTheme, focusSearch, null, segmentKey);
        var owner = TopLevel.GetTopLevel(this) as Window;
        if (owner != null)
        {
            await window.ShowDialog(owner);
        }
        else
        {
            window.Show();
        }
    }

    private async void SearchAllAccounts_Click(object? sender, RoutedEventArgs e)
    {
        await OpenSegmentAsync("all-accounts", focusSearch: true);
    }

    private async void SearchAllAccountsBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
        {
            return;
        }

        e.Handled = true;
        await OpenSegmentAsync("all-accounts", focusSearch: true);
    }
    
    private async void SmartAutomation_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel == null || sender is not Button button || button.Tag is not string segmentKey) return;
        
        var accountsList = ViewModel.GetAccountsForSegment(segmentKey);
        var accounts = accountsList.Take(50).ToList();
        if (!accounts.Any()) return;

        var title = ViewModel.GetSegmentTitle(segmentKey);
        var totalCount = accountsList.Count();
        var totalAmount = accountsList.Sum(a => a.GetAmount());
        
        var messageBuilder = new System.Text.StringBuilder();
        messageBuilder.AppendLine($"*[Bot Automation] {title} Summary*");
        messageBuilder.AppendLine($"Total: {totalCount} accounts (Rs. {totalAmount:N0})");
        messageBuilder.AppendLine();

        int i = 1;
        foreach (var account in accounts)
        {
            var date = account.GetNextInstallmentDate()?.ToString("dd-MMM") ?? account.NextInstallmentDate;
            messageBuilder.AppendLine($"{i}. {account.AccountName} - Rs.{account.Denomination} (Due: {date})");
            i++;
        }

        if (totalCount > 50)
        {
            messageBuilder.AppendLine($"... and {totalCount - 50} more.");
        }

        var fullMessage = messageBuilder.ToString();
        var encodedMessage = System.Uri.EscapeDataString(fullMessage);
        
        // WhatsApp Web / API URLs typically max out around 2000-2048 characters.
        if (encodedMessage.Length > 1500)
        {
            var topLevel = TopLevel.GetTopLevel(this);
            if (topLevel?.Clipboard != null)
            {
                await topLevel.Clipboard.SetTextAsync(fullMessage);
            }
            encodedMessage = System.Uri.EscapeDataString($"*[Bot Automation] {title} Summary*\n\n(The list is too long for a single link. The full list has been copied to your clipboard. Please Paste it here!)");
        }

        var phoneQuery = string.IsNullOrWhiteSpace(ViewModel.AgentPhoneNumber) ? "" : $"phone={ViewModel.AgentPhoneNumber}&";
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
}
