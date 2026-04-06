using Avalonia.Controls;
using Avalonia.Interactivity;
using AgentBuddy.Models;

namespace AgentBuddy.Views;

public partial class ConfirmListDialogWindow : Window
{
    public ConfirmListDialogWindow()
    {
        InitializeComponent();
    }

    public ConfirmListDialogWindow(ConfirmListDialogRequest request) : this()
    {
        DataContext = request;
    }

    private void Yes_Click(object? sender, RoutedEventArgs e)
    {
        Close(true);
    }

    private void No_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }
}
