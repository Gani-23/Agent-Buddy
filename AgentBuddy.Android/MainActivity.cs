using Android.App;
using Android.Content.PM;
using Android.OS;
using Avalonia;
using Avalonia.Android;
using AgentBuddy.Android.Services;
using AgentBuddy.Services;

using AgentBuddy;

namespace AgentBuddy.Android;

[Activity(
    Label = "Agent Buddy",
    Theme = "@style/MyTheme.NoActionBar",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
    public static MainActivity? Instance { get; private set; }

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        Instance = this;
        PortalAutomationProvider.Factory = db => new AndroidPortalAutomationService(this, db);
        base.OnCreate(savedInstanceState);
    }

    protected override void OnResume()
    {
        Instance = this;
        PortalAutomationProvider.Factory = db => new AndroidPortalAutomationService(this, db);
        base.OnResume();
    }
}
