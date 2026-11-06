using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using AgentBuddy.Services;
using AgentBuddy.ViewModels;

namespace AgentBuddy.Views;

public partial class DueRemindersWindow : Window
{
    public DueRemindersWindow()
    {
        InitializeComponent();
    }

    public DueRemindersWindow(DueRemindersViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OpenWhatsApp_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string url } && !string.IsNullOrWhiteSpace(url))
        {
            WhatsAppReminderService.LaunchWhatsAppUrl(url);
        }
    }

    private async void CopySingleMessage_Click(object? sender, RoutedEventArgs e)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (sender is Button { Tag: string msg } && !string.IsNullOrWhiteSpace(msg) && clipboard != null)
        {
            await clipboard.SetTextAsync(msg);
            SetStatus("✅ Reminder message copied!");
        }
    }

    private async void CopyAllSummary_Click(object? sender, RoutedEventArgs e)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (DataContext is DueRemindersViewModel vm && clipboard != null)
        {
            var summary = vm.GetAllRemindersSummary();
            await clipboard.SetTextAsync(summary);
            SetStatus("✅ All reminders summary copied to clipboard!");
        }
    }

    private void SetStatus(string text)
    {
        var block = this.FindControl<TextBlock>("FooterStatusText");
        if (block != null)
        {
            block.Text = text;
        }
    }
}
