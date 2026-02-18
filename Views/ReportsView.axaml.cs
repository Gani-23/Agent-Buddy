using Avalonia.Controls;
using Avalonia.Interactivity;
using AgentBuddy.Models;
using AgentBuddy.ViewModels;

namespace AgentBuddy.Views;

public partial class ReportsView : UserControl
{
    public ReportsView()
    {
        InitializeComponent();
    }

    private async void ViewPdf_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ReportsViewModel vm || sender is not Button button || button.DataContext is not DailyListReport report)
        {
            return;
        }

        await vm.ViewReportAsync(report);
    }

    private async void PrintPdf_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ReportsViewModel vm || sender is not Button button || button.DataContext is not DailyListReport report)
        {
            return;
        }

        await vm.PrintReportAsync(report);
    }

    private async void DeleteReport_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ReportsViewModel vm || sender is not Button button || button.DataContext is not DailyListReport report)
        {
            return;
        }

        await vm.DeleteReportAsync(report);
    }
}
