using System;
using System.Globalization;
using Antelcat.I18N.Avalonia;

namespace LabelPlus_Next.Services;

/// <summary>Small helper for localizing UI strings produced in code-behind/view models.</summary>
public static class UiText
{
    /// <summary>Translate an I18N resource key, falling back to the given text.</summary>
    public static string T(string key, string fallback)
        => I18NExtension.Translate(key) ?? fallback;

    /// <summary>Translate an I18N resource key that contains a composite format string.</summary>
    public static string F(string key, string fallback, params object?[] args)
        => string.Format(T(key, fallback), args);

    /// <summary>
    /// Translate a Chinese UI literal from the code-side catalog. The Chinese text
    /// itself is the catalog key so new messages can be added without resource
    /// designer churn. Missing entries fall back to the original text.
    /// </summary>
    public static string Tr(string chinese, params object?[] args)
    {
        var culture = LocalizationService.CurrentCulture;
        var template = chinese;

        if (IsTraditional(culture) && UiStrings.Hant.TryGetValue(chinese, out var hant))
            template = hant;
        else if (IsEnglish(culture) && UiStrings.En.TryGetValue(chinese, out var english))
            template = english;

        if (args.Length == 0) return template;
        try
        {
            return string.Format(template, args);
        }
        catch (FormatException)
        {
            return template;
        }
    }

    private static bool IsEnglish(CultureInfo culture)
        => string.Equals(culture.TwoLetterISOLanguageName, "en", StringComparison.OrdinalIgnoreCase);

    private static bool IsTraditional(CultureInfo culture)
        => culture.Name.StartsWith("zh-Hant", StringComparison.OrdinalIgnoreCase)
           || culture.Name.StartsWith("zh-TW", StringComparison.OrdinalIgnoreCase)
           || culture.Name.StartsWith("zh-HK", StringComparison.OrdinalIgnoreCase)
           || culture.Name.StartsWith("zh-MO", StringComparison.OrdinalIgnoreCase);
}
