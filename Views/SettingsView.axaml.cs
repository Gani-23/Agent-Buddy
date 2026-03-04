using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using AgentBuddy.ViewModels;

namespace AgentBuddy.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private async void BrowseSourceDb_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm)
        {
            return;
        }

        var path = await PickDatabaseFileAsync(GetStringResource("L_Set_PickSourceDialogTitle", "Select Source Legacy Database"));
        if (!string.IsNullOrWhiteSpace(path))
        {
            vm.SourceDatabasePath = path;
        }
    }

    private async void BrowseTargetDb_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not SettingsViewModel vm)
        {
            return;
        }

        var path = await PickDatabaseFileAsync(GetStringResource("L_Set_PickTargetDialogTitle", "Select Target DOPAgent Database"));
        if (!string.IsNullOrWhiteSpace(path))
        {
            vm.TargetDatabasePath = path;
        }
    }

    private async Task<string?> PickDatabaseFileAsync(string title)
    {
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel?.StorageProvider is null)
        {
            return null;
        }

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("SQLite Database")
                {
                    Patterns = new[] { "*.sqlite", "*.db" },
                    MimeTypes = new[] { "application/vnd.sqlite3", "application/octet-stream" }
                },
                FilePickerFileTypes.All
            }
        });

        var selected = files.FirstOrDefault();
        if (selected == null || selected.Path == null)
        {
            return null;
        }

        if (!selected.Path.IsFile)
        {
            return null;
        }

        return Uri.UnescapeDataString(selected.Path.LocalPath);
    }

    private string GetStringResource(string key, string fallback)
    {
        if (Application.Current?.TryGetResource(key, Application.Current.ActualThemeVariant, out var value) == true &&
            value is string text &&
            !string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        return fallback;
    }
}
