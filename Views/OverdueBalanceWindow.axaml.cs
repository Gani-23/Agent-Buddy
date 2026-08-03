using System;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using Avalonia.Styling;
using AgentBuddy.Models;
using AgentBuddy.Services;
using AgentBuddy.ViewModels;

namespace AgentBuddy.Views;

public partial class OverdueBalanceWindow : Window
{
    private readonly bool _isDarkTheme;
    private readonly ListManagementViewModel _listManagementViewModel = null!;
    private readonly NotificationService? _notificationService;

    public OverdueBalanceWindow()
    {
        InitializeComponent();
    }

    public OverdueBalanceWindow(
        DatabaseService databaseService,
        ListManagementViewModel listManagementViewModel,
        bool isDarkTheme,
        LocalizationService localizationService,
        NotificationService? notificationService = null) : this()
    {
        DataContext = new OverdueBalanceWindowViewModel(databaseService);
        _listManagementViewModel = listManagementViewModel;
        _isDarkTheme = isDarkTheme;
        _notificationService = notificationService;
        RequestedThemeVariant = isDarkTheme ? ThemeVariant.Dark : ThemeVariant.Light;

        Opened += async (_, _) =>
        {
            if (DataContext is OverdueBalanceWindowViewModel vm)
            {
                await vm.LoadAsync();
            }

            SearchBox.Focus();
        };
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private async void Copy_Click(object? sender, RoutedEventArgs e)
    {
        if (!TryGetItem(sender, out var item))
        {
            return;
        }

        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard == null)
        {
            _notificationService?.Warning("Copy Failed", "Clipboard is not available.");
            return;
        }

        await clipboard.SetTextAsync(item.AccountNo);
        _notificationService?.Info("Copied", $"{item.AccountNo} copied.");
    }

    private async void Add_Click(object? sender, RoutedEventArgs e)
    {
        if (!TryGetItem(sender, out var item))
        {
            return;
        }

        var added = await _listManagementViewModel.AddOverdueAccountToBestListAsync(
            item.AccountNo,
            item.BalanceMonths);

        if (added)
        {
            _notificationService?.Success("Added to List", $"{item.AccountNo} added with {item.BalanceMonths} installment(s).");
        }
    }

    private async void View_Click(object? sender, RoutedEventArgs e)
    {
        if (!TryGetItem(sender, out var item))
        {
            return;
        }

        var dialog = new AccountDetailsWindow(item.Account, _isDarkTheme);
        await dialog.ShowDialog(this);
    }

    private static bool TryGetItem(object? sender, out OverdueBalanceAccount item)
    {
        item = null!;
        if (sender is not Control control || control.DataContext is not OverdueBalanceAccount overdue)
        {
            return false;
        }

        item = overdue;
        return true;
    }
}
