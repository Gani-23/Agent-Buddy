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
            if (DataContext is DopChequePromptViewModel vm && vm.RequireChequeNo)
            {
                ChequeNoTextBox.Focus();
                ChequeNoTextBox.SelectAll();
            }
            else
            {
                PaymentAccountTextBox.Focus();
                PaymentAccountTextBox.SelectAll();
            }
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

        vm.ClearValidationMessage();
        var chequeNo = (vm.ChequeNo ?? string.Empty).Trim();
        var paymentAccountNo = (vm.PaymentAccountNo ?? string.Empty).Trim();

        if (vm.RequireChequeNo && string.IsNullOrWhiteSpace(chequeNo))
        {
            vm.SetValidationMessage("Enter cheque number.");
            ChequeNoTextBox.Focus();
            return;
        }

        if (string.IsNullOrWhiteSpace(paymentAccountNo))
        {
            vm.SetValidationMessage("Enter payment account number.");
            PaymentAccountTextBox.Focus();
            return;
        }

        Close(new DopChequePromptResult
        {
            AccountNo = vm.AccountNo,
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
