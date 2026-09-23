using Avalonia;
using Avalonia.iOS;
using Foundation;
using ReactiveUI.Avalonia;
using UIKit;

namespace AgentBuddy.iOS;

[Register("AppDelegate")]
public class AppDelegate : AvaloniaAppDelegate<App>
{
    protected override AppBuilder CustomizeAppBuilder(AppBuilder builder)
    {
        return base.CustomizeAppBuilder(builder)
            .WithInterFont()
            .UseReactiveUI(_ => { });
    }
}
