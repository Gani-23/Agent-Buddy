using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Styling;
using AgentBuddy.Models;
using AgentBuddy.ViewModels;

namespace AgentBuddy.Views;

public partial class SegmentAccountsWindow : Window
{
    public SegmentAccountsWindow()
    {
        InitializeComponent();
    }

    public SegmentAccountsWindow(string title, IEnumerable<RDAccount> accounts, bool isDarkTheme = false) : this()
    {
        DataContext = new SegmentAccountsWindowViewModel(title, accounts);
        RequestedThemeVariant = isDarkTheme ? ThemeVariant.Dark : ThemeVariant.Light;
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
