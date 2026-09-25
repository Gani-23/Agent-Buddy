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
    private readonly string _initialSearchQuery = string.Empty;

    public SegmentAccountsWindow()
    {
        InitializeComponent();
        Opened += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(_initialSearchQuery) && DataContext is SegmentAccountsWindowViewModel viewModel)
            {
                viewModel.SearchQuery = _initialSearchQuery;
            }

            if (_focusSearchOnOpen)
            {
                SearchBox.Focus();
                if (!string.IsNullOrWhiteSpace(SearchBox.Text))
                {
                    SearchBox.CaretIndex = SearchBox.Text.Length;
                }
                else
                {
                    SearchBox.SelectAll();
                }
            }
        };
    }

    public SegmentAccountsWindow(
        DashboardViewModel dashboardViewModel,
        bool isDarkTheme = false,
        bool focusSearchOnOpen = false,
        string? initialSearchQuery = null,
        string? segmentKey = null) : this()
    {
        DataContext = new SegmentAccountsWindowViewModel(dashboardViewModel, segmentKey);
        RequestedThemeVariant = isDarkTheme ? ThemeVariant.Dark : ThemeVariant.Light;
        _focusSearchOnOpen = focusSearchOnOpen;
        _isDarkTheme = isDarkTheme;
        _initialSearchQuery = initialSearchQuery?.Trim() ?? string.Empty;
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

    private async void DeleteAccount_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SegmentAccountsWindowViewModel viewModel ||
            sender is not Button button ||
            button.DataContext is not RDAccount account)
        {
            return;
        }

        var request = new ConfirmDialogRequest(
            "Delete closed account",
            $"Remove {account.AccountNo} from closed accounts?",
            "Delete",
            "Cancel");

        var dialog = new ConfirmDialogWindow(request)
        {
            RequestedThemeVariant = _isDarkTheme ? ThemeVariant.Dark : ThemeVariant.Light
        };

        var confirmed = await dialog.ShowDialog<bool>(this);
        if (!confirmed)
        {
            return;
        }

        await viewModel.DeleteAccountAsync(account);
    }
}
