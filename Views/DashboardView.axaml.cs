using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using Avalonia.Input.Platform;
using AgentBuddy.ViewModels;
using AgentBuddy.Services;
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

    private bool _isOpeningSearch;

    private async System.Threading.Tasks.Task OpenSegmentAsync(string? segmentKey, bool focusSearch, string? initialSearchQuery = null)
    {
        if (ViewModel is null)
            return;

        var window = new SegmentAccountsWindow(ViewModel, ViewModel.IsDarkTheme, focusSearch, initialSearchQuery, segmentKey);
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

    private async System.Threading.Tasks.Task ExecuteDashboardSearchAsync()
    {
        if (_isOpeningSearch)
            return;

        _isOpeningSearch = true;
        try
        {
            var searchBox = this.FindControl<TextBox>("SearchAllAccountsTextBox");
            var query = !string.IsNullOrWhiteSpace(searchBox?.Text)
                ? searchBox.Text.Trim()
                : ViewModel?.GlobalAccountSearchQuery?.Trim();

            await OpenSegmentAsync("all-accounts", focusSearch: string.IsNullOrWhiteSpace(query), query);
        }
        finally
        {
            _isOpeningSearch = false;
        }
    }

    private async void SearchAllAccounts_Click(object? sender, RoutedEventArgs e)
    {
        await ExecuteDashboardSearchAsync();
    }

    private async void SearchAllAccountsBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter && e.Key != Key.Return)
        {
            return;
        }

        e.Handled = true;
        await ExecuteDashboardSearchAsync();
    }
    
    private async void SmartAutomation_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel == null || sender is not Button button || button.Tag is not string segmentKey) return;
        
        var accountsList = ViewModel.GetAccountsForSegment(segmentKey).ToList();
        if (!accountsList.Any()) return;

        var title = ViewModel.GetSegmentTitle(segmentKey);
        var totalCount = accountsList.Count;
        var totalAmount = accountsList.Sum(a => a.GetAmount());
        
        string? agentName = null;
        if (ViewModel.DatabaseService != null)
        {
            agentName = await ViewModel.DatabaseService.GetAgentNameAsync();
        }

        var (_, pdfPath, _) = DueListPdfService.DispatchToWhatsApp(
            title,
            agentName ?? "DOP Agent",
            accountsList,
            ViewModel.AgentPhoneNumber);

        ViewModel.NotificationService?.Success(
            "WhatsApp PDF Ready",
            $"{title} PDF ({totalCount} accounts, Rs.{totalAmount:N0}) copied to clipboard. Press Cmd+V/Ctrl+V in WhatsApp to attach.");
    }

    private async void SyncMobileQr_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel?.DatabaseService is null)
        {
            return;
        }

        var window = new MobileQrSyncWindow(ViewModel.DatabaseService, ViewModel.DesktopSyncServer);
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


    private void NavigateToSettings_Click(object? sender, RoutedEventArgs e)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.DataContext is MainWindowViewModel mainVm)
        {
            mainVm.Navigate("Settings");
        }
    }
}
