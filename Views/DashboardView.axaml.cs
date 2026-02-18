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
        if (sender is not Button button || ViewModel is null)
        {
            return;
        }

        var segmentKey = button.Tag?.ToString();
        var accounts = ViewModel.GetAccountsForSegment(segmentKey);
        var title = ViewModel.GetSegmentTitle(segmentKey);

        var window = new SegmentAccountsWindow(title, accounts, ViewModel.IsDarkTheme);
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
