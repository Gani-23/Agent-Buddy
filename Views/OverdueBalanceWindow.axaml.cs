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

    private readonly DatabaseService _databaseService;

    public OverdueBalanceWindow()
    {
        InitializeComponent();
        _databaseService = new DatabaseService();
    }

    public OverdueBalanceWindow(
        DatabaseService databaseService,
        ListManagementViewModel listManagementViewModel,
        bool isDarkTheme,
        LocalizationService localizationService,
        NotificationService? notificationService = null) : this()
    {
        _databaseService = databaseService;
        var vm = new OverdueBalanceWindowViewModel(databaseService);
        DataContext = vm;
        _listManagementViewModel = listManagementViewModel;
        _isDarkTheme = isDarkTheme;
        _notificationService = notificationService;

        _databaseService.DatabaseChanged += OnDatabaseChanged;
        Closed += (_, _) => _databaseService.DatabaseChanged -= OnDatabaseChanged;

        Opened += async (_, _) =>
        {
            if (DataContext is OverdueBalanceWindowViewModel overdueVm)
            {
                await overdueVm.LoadAsync();
            }

            SearchBox.Focus();
        };
    }

    private void OnDatabaseChanged(object? sender, EventArgs e)
    {
        if (DataContext is OverdueBalanceWindowViewModel vm)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(async () => await vm.LoadAsync());
        }
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
