using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using AgentBuddy.ViewModels;

namespace AgentBuddy.Views;

public partial class DopChequePromptWindow : Window
{
    public DopChequePromptWindow()
    {
        InitializeComponent();
    }

    public DopChequePromptWindow(DopChequePromptRequest request) : this()
    {
        DataContext = new DopChequePromptViewModel(request);
        Opened += (_, _) =>
        {
            ChequeNoTextBox.Focus();
            ChequeNoTextBox.SelectAll();
        };
        KeyDown += OnKeyDown;
    }

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not DopChequePromptViewModel vm)
        {
            Close(null);
            return;
        }

        var chequeNo = (vm.ChequeNo ?? string.Empty).Trim();
        var paymentAccountNo = (vm.PaymentAccountNo ?? string.Empty).Trim();

        if (string.IsNullOrWhiteSpace(chequeNo))
        {
            ChequeNoTextBox.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(paymentAccountNo))
        {
            PaymentAccountTextBox.Focus();
            return;
        }

        Close(new DopChequePromptResult
        {
            ChequeNo = chequeNo,
            PaymentAccountNo = paymentAccountNo
        });
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close(null);
            return;
        }

        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            Save_Click(sender, new RoutedEventArgs());
        }
    }
}
