using System;
using Avalonia.Controls;
using AgentBuddy.ViewModels;

namespace AgentBuddy.Views;

/// <summary>
/// First-run setup wizard. Closes with <c>true</c> when the user asked to sync
/// accounts right away, <c>false</c> otherwise (including closing the window early).
/// </summary>
public partial class OnboardingWindow : Window
{
    public OnboardingWindow()
    {
        InitializeComponent();
    }

    public OnboardingWindow(OnboardingViewModel viewModel) : this()
    {
        DataContext = viewModel;
        viewModel.Completed += OnCompleted;
        Closed += (_, _) => viewModel.Completed -= OnCompleted;
    }

    private void OnCompleted(object? sender, bool syncNow) => Close(syncNow);
}
