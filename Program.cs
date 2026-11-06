using System;
using System.IO;
using Avalonia;
using ReactiveUI.Avalonia;

namespace AgentBuddy;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
            {
                LogCrash("UnhandledException", e.ExceptionObject as Exception);
            };

            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            LogCrash("MainException", ex);
            throw;
        }
    }

    private static void LogCrash(string context, Exception? ex)
    {
        try
        {
            var logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "crash.log");
            File.AppendAllText(logPath, $"[{DateTime.Now}] {context}: {ex}\n\n");

            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
            if (!string.IsNullOrWhiteSpace(desktop) && Directory.Exists(desktop))
            {
                File.AppendAllText(Path.Combine(desktop, "AgentBuddy_crash.log"), $"[{DateTime.Now}] {context}: {ex}\n\n");
            }

            // Silent Field Crash Dispatcher -> Telegram / Discord Alert
            var payload = System.Text.Json.JsonSerializer.Serialize(new
            {
                appId = "agentbuddy",
                error = $"{context}: {ex?.Message}",
                stack = ex?.ToString(),
                os = $"{System.Runtime.InteropServices.RuntimeInformation.OSDescription} ({System.Runtime.InteropServices.RuntimeInformation.ProcessArchitecture})",
                version = "1.0.0",
            });
            using var client = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            using var content = new System.Net.Http.StringContent(payload, System.Text.Encoding.UTF8, "application/json");
            client.PostAsync("https://oauth4-0.onrender.com/api/telemetry/crash", content).GetAwaiter().GetResult();
        }
        catch { }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UseReactiveUI(_ => { })
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
