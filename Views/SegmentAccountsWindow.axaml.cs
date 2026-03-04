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
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
