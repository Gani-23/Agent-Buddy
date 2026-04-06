using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Styling;
using AgentBuddy.Models;
using AgentBuddy.ViewModels;

namespace AgentBuddy.Views;

public partial class SegmentAccountsWindow : Window
{
    private readonly bool _focusSearchOnOpen;
    private readonly bool _isDarkTheme;

    public SegmentAccountsWindow()
    {
        InitializeComponent();
        Opened += (_, _) =>
        {
            if (_focusSearchOnOpen)
            {
                SearchBox.Focus();
                SearchBox.SelectAll();
            }
        };
    }

    public SegmentAccountsWindow(string title, IEnumerable<RDAccount> accounts, bool isDarkTheme = false, bool focusSearchOnOpen = false) : this()
    {
        DataContext = new SegmentAccountsWindowViewModel(title, accounts);
        RequestedThemeVariant = isDarkTheme ? ThemeVariant.Dark : ThemeVariant.Light;
        _focusSearchOnOpen = focusSearchOnOpen;
        _isDarkTheme = isDarkTheme;
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void OpenProfile_DoubleTapped(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control control || control.DataContext is not RDAccount account)
        {
            return;
        }

        var dialog = new AccountDetailsWindow(account, _isDarkTheme);
        await dialog.ShowDialog(this);
    }
}
