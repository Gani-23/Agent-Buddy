using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AgentBuddy.Models;
using AgentBuddy.ViewModels;
using ReactiveUI;
using System;
using System.Linq;

namespace AgentBuddy.Views;

public partial class ListManagementView : UserControl
{
    private IDisposable? _aslaasPromptHandler;
    private IDisposable? _dopChequePromptHandler;

    public ListManagementView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        DetachedFromVisualTree += (_, _) => DisposePromptHandler();
    }

    private void RemoveAccount_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.DataContext is not ListItem item)
        {
            return;
        }

        var listOwner = button
            .GetVisualAncestors()
            .OfType<ItemsControl>()
            .FirstOrDefault(control => control.DataContext is ListPanelViewModel)?
            .DataContext as ListPanelViewModel;

        listOwner?.RemoveAccount(item);
    }

    private async void PendingField_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox sourceTextBox)
        {
            return;
        }

        e.Handled = true;
        var wasAdded = false;

        if (sourceTextBox.DataContext is ListPanelViewModel listVm)
        {
            wasAdded = await listVm.SubmitPendingAsync();
        }

        if (!wasAdded)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            var inlineGrid = sourceTextBox
                .GetVisualAncestors()
                .OfType<Grid>()
                .FirstOrDefault();

            var accountTextBox = inlineGrid?
                .Children
                .OfType<TextBox>()
                .FirstOrDefault(tb => (tb.Tag as string) == "AccountInput");

            accountTextBox?.Focus();
            accountTextBox?.SelectAll();
        }, DispatcherPriority.Background);
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        DisposePromptHandler();

        if (DataContext is not ListManagementViewModel vm)
        {
            return;
        }

        _aslaasPromptHandler = vm.AslaasPrompt.RegisterHandler(async interaction =>
        {
            var owner = TopLevel.GetTopLevel(this) as Window;
            if (owner == null)
            {
                interaction.SetOutput(null);
                return;
            }

            var dialog = new AslaasPromptWindow(interaction.Input);
            var result = await dialog.ShowDialog<string?>(owner);
            interaction.SetOutput(result);
        });

        _dopChequePromptHandler = vm.DopChequePrompt.RegisterHandler(async interaction =>
        {
            var owner = TopLevel.GetTopLevel(this) as Window;
            if (owner == null)
            {
                interaction.SetOutput(null);
                return;
            }

            var dialog = new DopChequePromptWindow(interaction.Input);
            var result = await dialog.ShowDialog<DopChequePromptResult?>(owner);
            interaction.SetOutput(result);
        });
    }

    private void DisposePromptHandler()
    {
        _aslaasPromptHandler?.Dispose();
        _aslaasPromptHandler = null;
        _dopChequePromptHandler?.Dispose();
        _dopChequePromptHandler = null;
    }
}
