using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
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

        var window = new SegmentAccountsWindow(title, accounts, ViewModel.IsDarkTheme, focusSearch);
        var owner = this.GetVisualRoot() as Window;
        if (owner != null)
        {
            await window.ShowDialog(owner);
        }
        else
        {
            window.Show();
        }
    }
}
