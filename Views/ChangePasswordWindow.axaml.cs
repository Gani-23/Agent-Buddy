using Avalonia.Controls;
using AgentBuddy.Models;
using AgentBuddy.ViewModels;

namespace AgentBuddy.Views;

public partial class ChangePasswordWindow : Window
{
    public ChangePasswordWindow()
    {
        InitializeComponent();
        DataContext = new ChangePasswordViewModel(string.Empty);
    }

    public ChangePasswordWindow(string agentId)
    {
        InitializeComponent();
        DataContext = new ChangePasswordViewModel(agentId);
    }

    private void Cancel_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        Close(null);
    }

    private void Save_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is not ChangePasswordViewModel vm)
        {
            Close(null);
            return;
        }

        if (!vm.Validate())
        {
            return;
        }

        var result = new PasswordChangeRequest(
            vm.AgentId,
            vm.CurrentPassword,
            vm.NewPassword);

        Close(result);
    }
}
