using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Input;
using AgentBuddy.ViewModels;

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
        if (sender is not Button button)
        {
            return;
        }

        await OpenSegmentAsync(button.Tag?.ToString(), focusSearch: false);
    }

    private async void SearchSegment_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button)
        {
            return;
        }

        await OpenSegmentAsync(button.Tag?.ToString(), focusSearch: true);
    }

    private async void PrintSegment_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || ViewModel is null)
        {
            return;
        }

        var (success, message) = await ViewModel.PrintSegmentAsync(button.Tag?.ToString());
        ViewModel.UpdateStatus = message;
        if (!success)
        {
            return;
        }
    }

    private async System.Threading.Tasks.Task OpenSegmentAsync(string? segmentKey, bool focusSearch)
    {
        if (ViewModel is null)
        {
            return;
        }

        var accounts = ViewModel.GetAccountsForSegment(segmentKey);
        var title = ViewModel.GetSegmentTitle(segmentKey);
        var initialSearchQuery = string.Equals(segmentKey, "all-accounts", System.StringComparison.OrdinalIgnoreCase)
            ? ViewModel.GlobalAccountSearchQuery
            : null;

        var window = new SegmentAccountsWindow(title, accounts, ViewModel.IsDarkTheme, focusSearch, initialSearchQuery, segmentKey);
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
}
