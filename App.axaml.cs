using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using AgentBuddy.ViewModels;
using AgentBuddy.Views;

namespace AgentBuddy;

public partial class App : Application
{
    public override void Initialize()
    {
        try
        {
            SQLitePCL.Batteries_V2.Init();
        }
        catch { }

        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        try
        {
            SQLitePCL.Batteries_V2.Init();
        }
        catch { }
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow
            {
                DataContext = new MainWindowViewModel(),
            };
        }
        else if (ApplicationLifetime is ISingleViewApplicationLifetime singleView)
        {
            singleView.MainView = new MainView
            {
                DataContext = new MainWindowViewModel(),
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
