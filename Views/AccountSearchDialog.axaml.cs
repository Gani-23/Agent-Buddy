using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Styling;
using AgentBuddy.ViewModels;

namespace AgentBuddy.Views;

public partial class AccountSearchDialog : Window
{
    private AccountSearchViewModel? ViewModel => DataContext as AccountSearchViewModel;

    public AccountSearchDialog()
    {
        InitializeComponent();
    }

    public AccountSearchDialog(AccountSearchViewModel viewModel) : this()
    {
        DataContext = viewModel;
        RequestedThemeVariant = viewModel.IsDarkTheme ? ThemeVariant.Dark : ThemeVariant.Light;
    }

    private void AddSelected_Click(object? sender, RoutedEventArgs e)
    {
        if (ViewModel != null)
        {
            ViewModel.AddSelectedAccounts = true;
        }
        Close();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
