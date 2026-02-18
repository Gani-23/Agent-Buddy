using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Notifications;
using Avalonia.Threading;
using AgentBuddy.Services;
using AgentBuddy.ViewModels;
using System;
using System.ComponentModel;
using System.Linq;

namespace AgentBuddy.Views;

public partial class MainWindow : Window
{
    private readonly WindowNotificationManager _notificationManager;
    private NotificationService? _notificationService;
    private bool _enforcingMaximized;

    public MainWindow()
    {
        InitializeComponent();
        _notificationManager = new WindowNotificationManager(this)
        {
            Position = NotificationPosition.TopRight,
            MaxItems = 4
        };

        DataContextChanged += OnDataContextChanged;
        Opened += OnWindowOpened;
        Closed += OnWindowClosed;
        PropertyChanged += OnWindowPropertyChanged;
        AttachNotificationService();
    }

    private void OnWindowOpened(object? sender, EventArgs e)
    {
        EnforceMaximized();
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        AttachNotificationService();
        HookMainViewModelChanges();
        UpdateNavigationSelection();
    }

    private void AttachNotificationService()
    {
        var newService = (DataContext as MainWindowViewModel)?.NotificationService;
        if (ReferenceEquals(_notificationService, newService))
        {
            return;
        }

        if (_notificationService != null)
        {
            _notificationService.NotificationRaised -= OnNotificationRaised;
        }

        _notificationService = newService;
        if (_notificationService != null)
        {
            _notificationService.NotificationRaised += OnNotificationRaised;
        }
    }

    private void HookMainViewModelChanges()
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        vm.PropertyChanged -= OnMainViewModelPropertyChanged;
        vm.PropertyChanged += OnMainViewModelPropertyChanged;
    }

    private void OnWindowClosed(object? sender, System.EventArgs e)
    {
        PropertyChanged -= OnWindowPropertyChanged;
        if (DataContext is MainWindowViewModel vm)
        {
            vm.PropertyChanged -= OnMainViewModelPropertyChanged;
        }

        if (_notificationService != null)
        {
            _notificationService.NotificationRaised -= OnNotificationRaised;
            _notificationService = null;
        }
    }

    private void OnNotificationRaised(AppNotification notification)
    {
        Dispatcher.UIThread.Post(() =>
        {
            _notificationManager.Show(new Notification(
                notification.Title,
                notification.Message,
                ToAvaloniaType(notification.Type)));
        });
    }

    private static NotificationType ToAvaloniaType(AppNotificationType type)
    {
        return type switch
        {
            AppNotificationType.Success => NotificationType.Success,
            AppNotificationType.Warning => NotificationType.Warning,
            AppNotificationType.Error => NotificationType.Error,
            _ => NotificationType.Information
        };
    }

    private void OnWindowStateChanged(WindowState state)
    {
        if (_enforcingMaximized)
        {
            return;
        }

        if (state == WindowState.Normal)
        {
            EnforceMaximized();
        }
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == WindowStateProperty && e.NewValue is WindowState state)
        {
            OnWindowStateChanged(state);
        }
    }

    private void EnforceMaximized()
    {
        if (_enforcingMaximized)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (WindowState == WindowState.Minimized)
            {
                return;
            }

            try
            {
                _enforcingMaximized = true;
                WindowState = WindowState.Maximized;
            }
            finally
            {
                _enforcingMaximized = false;
            }
        });
    }

    private void OnMainViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainWindowViewModel.CurrentViewName))
        {
            UpdateNavigationSelection();
        }
    }

    private void UpdateNavigationSelection()
    {
        if (DataContext is not MainWindowViewModel vm)
        {
            return;
        }

        var map = new[]
        {
            (Button: DashboardNavButton, ViewName: "Dashboard"),
            (Button: ListsNavButton, ViewName: "Lists"),
            (Button: ReportsNavButton, ViewName: "Reports"),
            (Button: SupportNavButton, ViewName: "Support"),
            (Button: SettingsNavButton, ViewName: "Settings")
        };

        foreach (var entry in map.Where(entry => entry.Button != null))
        {
            var classes = entry.Button.Classes;
            var isCurrent = string.Equals(vm.CurrentViewName, entry.ViewName, StringComparison.OrdinalIgnoreCase);
            if (isCurrent)
            {
                if (!classes.Contains("Current"))
                {
                    classes.Add("Current");
                }
            }
            else
            {
                classes.Remove("Current");
            }
        }
    }
}
