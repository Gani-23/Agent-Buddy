using Android.App;
using Android.Content.PM;
using Android.OS;
using Avalonia.Android;
using AgentBuddy.Android.Services;
using AgentBuddy.Services;

namespace AgentBuddy.Android;

[Activity(
    Label = "Agent Buddy",
    Theme = "@style/MyTheme.NoActionBar",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        PortalAutomationProvider.Factory = db => new AndroidPortalAutomationService(this, db);
        base.OnCreate(savedInstanceState);
    }
}
