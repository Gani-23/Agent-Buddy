using System;
using System.IO;
using System.Text.Json;

namespace AgentBuddy.Services;

public class AppSettings
{
    public string AgentPhoneNumber { get; set; } = string.Empty;

    public static AppSettings Load()
    {
        var path = Path.Combine(AppPaths.BaseDirectory, "settings.json");
        if (File.Exists(path))
        {
            try
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<AppSettings>(json) ?? new AppSettings();
            }
            catch
            {
                return new AppSettings();
            }
        }
        return new AppSettings();
    }

    public void Save()
    {
        var path = Path.Combine(AppPaths.BaseDirectory, "settings.json");
        try
        {
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch { }
    }
}
