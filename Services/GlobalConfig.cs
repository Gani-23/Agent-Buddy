using System;
using System.IO;
using System.Text.Json;

namespace AgentBuddy.Services;

public class GlobalConfig
{
    public string BaseDirectoryOverride { get; set; } = string.Empty;

    private static string GetConfigPath()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        return Path.Combine(profile, ".agentbuddy_config.json");
    }

    public static GlobalConfig Load()
    {
        var path = GetConfigPath();
        if (File.Exists(path))
        {
            try
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<GlobalConfig>(json) ?? new GlobalConfig();
            }
            catch { }
        }
        return new GlobalConfig();
    }

    public void Save()
    {
        var path = GetConfigPath();
        try
        {
            var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(path, json);
        }
        catch { }
    }
}
