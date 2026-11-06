using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using AgentBuddy.ViewModels;

namespace AgentBuddy.Views;

public partial class CashDenominationWindow : Window
{
    public CashDenominationWindow()
    {
        InitializeComponent();
    }

    public CashDenominationWindow(CashDenominationViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void CopySlip_Click(object? sender, RoutedEventArgs e)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (DataContext is CashDenominationViewModel vm && clipboard != null)
        {
            var text = vm.GenerateSlipText();
            await clipboard.SetTextAsync(text);

            var statusBlock = this.FindControl<TextBlock>("CopyStatusText");
            if (statusBlock != null)
            {
                statusBlock.Text = "✅ Deposit slip copied to clipboard!";
            }
        }
    }
}
