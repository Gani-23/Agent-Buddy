using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using AgentBuddy.ViewModels;

namespace AgentBuddy.Views;

public partial class AslaasPromptWindow : Window
{
    public AslaasPromptWindow()
    {
        InitializeComponent();
    }

    public AslaasPromptWindow(AslaasPromptRequest request) : this()
    {
        DataContext = new AslaasPromptViewModel(request);
        Opened += (_, _) =>
        {
            AslaasTextBox.Focus();
            AslaasTextBox.SelectAll();
        };
        KeyDown += OnKeyDown;
    }

    private void Save_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AslaasPromptViewModel vm)
        {
            Close(null);
            return;
        }

        var value = (vm.AslaasNo ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(value))
        {
            AslaasTextBox.Focus();
            return;
        }

        Close(value);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close(null);
            return;
        }

        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            Save_Click(sender, new RoutedEventArgs());
        }
    }
}
