using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using AgentBuddy.ViewModels;

namespace AgentBuddy.Converters;

/// <summary>
/// Gives a fixed preview colour for a theme mode so theme cards can show a mini
/// mock-up of each theme regardless of the theme currently applied.
/// ConverterParameter: bg | card | sidebar | text | muted | border.
/// </summary>
public class ThemePreviewBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var mode = value is ThemeMode m ? m : ThemeMode.NeumorphicLight;
        var part = parameter as string ?? "bg";
        var hex = (mode, part) switch
        {
            (ThemeMode.NeumorphicDark, "bg") => "#0D1117",
            (ThemeMode.NeumorphicDark, "card") => "#161B22",
            (ThemeMode.NeumorphicDark, "sidebar") => "#0B0F14",
            (ThemeMode.NeumorphicDark, "text") => "#F0F6FC",
            (ThemeMode.NeumorphicDark, "muted") => "#30363D",
            (ThemeMode.NeumorphicDark, "border") => "#30363D",
            (ThemeMode.ClassicNotion, "bg") => "#F7F6F3",
            (ThemeMode.ClassicNotion, "card") => "#FFFFFF",
            (ThemeMode.ClassicNotion, "sidebar") => "#FBFAF8",
            (ThemeMode.ClassicNotion, "text") => "#37352F",
            (ThemeMode.ClassicNotion, "muted") => "#E7E5E4",
            (ThemeMode.ClassicNotion, "border") => "#E7E5E4",
            (_, "bg") => "#F8F9FA",
            (_, "card") => "#FFFFFF",
            (_, "sidebar") => "#F3F4F6",
            (_, "text") => "#111827",
            (_, "muted") => "#E5E7EB",
            _ => "#E5E7EB",
        };
        return new SolidColorBrush(Color.Parse(hex));
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
