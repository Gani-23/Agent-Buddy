using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Markup.Xaml.Styling;
using AgentBuddy.Models;

namespace AgentBuddy.Services;

public sealed class LocalizationService
{
    public const string LanguageSettingKey = "ui_language";

    private StyleInclude? _activeLanguageInclude;
    private string _currentLanguageCode = "en";

    public event EventHandler<string>? LanguageChanged;

    public string CurrentLanguageCode => _currentLanguageCode;

    public IReadOnlyList<LanguageOption> AvailableLanguages { get; } =
    [
        new LanguageOption("en", "English"),
        new LanguageOption("hi", "हिन्दी"),
        new LanguageOption("te", "తెలుగు")
    ];

    public async Task InitializeAsync(DatabaseService databaseService)
    {
        if (databaseService == null)
        {
            ApplyLanguage("en");
            return;
        }

        var savedCode = await databaseService.GetAppSettingAsync(LanguageSettingKey);
        ApplyLanguage(savedCode);
    }

    public async Task SetLanguageAsync(string? languageCode, DatabaseService databaseService)
    {
        var normalized = NormalizeLanguageCode(languageCode);
        ApplyLanguage(normalized);
        await databaseService.SaveAppSettingAsync(LanguageSettingKey, normalized);
    }

    public void ApplyLanguage(string? languageCode)
    {
        var normalized = NormalizeLanguageCode(languageCode);

        if (Application.Current is not { Styles: { } styles })
        {
            _currentLanguageCode = normalized;
            LanguageChanged?.Invoke(this, _currentLanguageCode);
            return;
        }

        if (_activeLanguageInclude != null)
        {
            styles.Remove(_activeLanguageInclude);
            _activeLanguageInclude = null;
        }

        if (!string.Equals(normalized, "en", StringComparison.OrdinalIgnoreCase))
        {
            var include = new StyleInclude(new Uri("avares://AgentBuddy/"))
            {
                Source = new Uri($"avares://AgentBuddy/Resources/Strings.{normalized}.axaml")
            };
            styles.Add(include);
            _activeLanguageInclude = include;
        }

        _currentLanguageCode = normalized;
        LanguageChanged?.Invoke(this, _currentLanguageCode);
    }

    public string NormalizeLanguageCode(string? languageCode)
    {
        var code = (languageCode ?? string.Empty).Trim().ToLowerInvariant();
        return code switch
        {
            "hi" => "hi",
            "te" => "te",
            _ => "en"
        };
    }
}
