using Android.App;
using Android.Content.PM;
using Avalonia.Android;

namespace AgentBuddy.Android;

[Activity(
    Label = "Agent Buddy",
    Theme = "@android:style/Theme.Material.Light.NoActionBar",
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode)]
public class MainActivity : AvaloniaMainActivity
{
}
