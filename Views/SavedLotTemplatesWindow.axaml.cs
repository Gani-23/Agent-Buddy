using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using AgentBuddy.ViewModels;
using AgentBuddy.Models;

namespace AgentBuddy.Views;

public partial class SavedLotTemplatesWindow : Window
{
    public SavedLotTemplatesWindow()
    {
        InitializeComponent();
        KeyDown += OnKeyDown;
    }

    public SavedLotTemplatesWindow(SavedLotTemplatesViewModel viewModel) : this()
    {
        DataContext = viewModel;
        viewModel.CloseRequested += (_, _) => Close();
    }

    private void SaveList_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SavedLotTemplatesViewModel vm)
        {
            vm.RequestSaveCurrentList();
        }
    }

    private void LoadIntoNewList_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SavedLotTemplatesViewModel vm)
        {
            vm.RequestLoadIntoNewList();
            Close();
        }
    }

    private void LoadIntoSelectedList_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SavedLotTemplatesViewModel vm)
        {
            vm.RequestLoadIntoSelectedList();
            Close();
        }
    }

    private void DeleteTemplate_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is SavedLotTemplatesViewModel vm)
        {
            vm.RequestDeleteTemplate();
        }
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }
}
