using Avalonia.Styling;

namespace LabelPlus_Next.Models;

/// <summary>
/// Persisted theme preference values and their mapping to Avalonia theme variants.
/// Keep this file UI-framework free apart from the <see cref="ThemeVariant"/> values
/// so it can be covered by unit tests without an Avalonia application instance.
/// </summary>
public static class ThemePreference
{
    public const string Default = "default";
    public const string Light = "light";
    public const string Dark = "dark";

    public static ThemeVariant Parse(string? value)
    {
        var normalized = value?.Trim();
        if (string.Equals(normalized, Light, StringComparison.OrdinalIgnoreCase)) return ThemeVariant.Light;
        if (string.Equals(normalized, Dark, StringComparison.OrdinalIgnoreCase)) return ThemeVariant.Dark;
        return ThemeVariant.Default;
    }

    public static string Format(ThemeVariant? value)
    {
        if (value == ThemeVariant.Light) return Light;
        if (value == ThemeVariant.Dark) return Dark;
        return Default;
    }
}
