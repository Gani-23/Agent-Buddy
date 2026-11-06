using System;
using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using AgentBuddy.Services;
using AgentBuddy.ViewModels;

namespace AgentBuddy.Views;

public partial class MobileQrSyncWindow : Window
{
    private readonly MobileQrSyncViewModel? _viewModel;

    public MobileQrSyncWindow()
    {
        InitializeComponent();
    }

    public MobileQrSyncWindow(DatabaseService databaseService, DesktopSyncServer? syncServer = null) : this()
    {
        _viewModel = new MobileQrSyncViewModel(databaseService, syncServer);
        DataContext = _viewModel;

        Closed += OnWindowClosed;
    }

    private void OnWindowClosed(object? sender, EventArgs e)
    {
        _viewModel?.Dispose();
    }

    private async void CopyUrl_Click(object? sender, RoutedEventArgs e)
    {
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard != null && _viewModel != null && !string.IsNullOrEmpty(_viewModel.SyncUrl))
        {
            await clipboard.SetTextAsync(_viewModel.SyncUrl);
            _viewModel.OnCopied();
        }
    }

    private async void RefreshNetwork_Click(object? sender, RoutedEventArgs e)
    {
        if (_viewModel != null)
        {
            await _viewModel.RefreshNetworkAsync();
        }
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
