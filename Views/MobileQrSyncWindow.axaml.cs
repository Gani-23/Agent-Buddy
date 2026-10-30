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
            _viewModel.IsCopied = true;
            if (sender is Button btn && btn.Content is StackPanel sp && sp.Children.Count > 1 && sp.Children[1] is TextBlock tb)
            {
                tb.Text = "Copied!";
            }
        }
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
