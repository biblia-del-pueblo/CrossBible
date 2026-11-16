using System;
using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

namespace CrossBible.Library;

/// <summary>
/// The colour of the text of the Bible: black in the light theme and white in the dark one, at the opacity (in percent) that the reader chose with a
/// slider in the settings. The colour follows the theme by itself, so the opacity keeps its meaning (how strong the text is) when the theme changes.
/// Until the reader chooses, it is fully black in the light theme and 83% white (a light grey) in the dark one.
/// </summary>
public static class TextColor
{
    public const double Minimum = 30, Maximum = 100, LightDefault = 100, DarkDefault = 83;

    /// <summary>Whether the dark theme is shown now (also when the settings say "System").</summary>
    public static bool IsDark => Application.Current?.ActualThemeVariant == ThemeVariant.Dark;

    /// <summary>The opacity in use, in percent: the chosen one, or the default of the theme.</summary>
    public static double Opacity(Settings settings) => Math.Clamp(settings.TextOpacity ?? (IsDark ? DarkDefault : LightDefault), Minimum, Maximum);

    public static Color ColorOf(double opacity)
    {
        byte alpha = (byte)Math.Round(Math.Clamp(opacity, Minimum, Maximum) * 2.55);
        return IsDark ? Color.FromArgb(alpha, 255, 255, 255) : Color.FromArgb(alpha, 0, 0, 0);
    }

    public static IBrush Brush(Settings settings) => new SolidColorBrush(ColorOf(Opacity(settings)));
}
