using AgentBuddy.ViewModels;

namespace AgentBuddy.Models;

public class ThemeModeOption
{
    public ThemeMode Mode { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string IconKey { get; set; } = string.Empty;
}
