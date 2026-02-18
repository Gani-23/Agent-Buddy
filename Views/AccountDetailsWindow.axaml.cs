using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Styling;
using AgentBuddy.Models;

namespace AgentBuddy.Views;

public partial class AccountDetailsWindow : Window
{
    public RDAccount? Account { get; private set; }
    public bool AddToListRequested { get; private set; }

    public AccountDetailsWindow()
    {
        InitializeComponent();
    }

    public AccountDetailsWindow(RDAccount account, bool isDarkTheme = false) : this()
    {
        Account = account;
        DataContext = account;
        RequestedThemeVariant = isDarkTheme ? ThemeVariant.Dark : ThemeVariant.Light;
    }

    private void AddToList_Click(object? sender, RoutedEventArgs e)
    {
        AddToListRequested = true;
        Close();
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
