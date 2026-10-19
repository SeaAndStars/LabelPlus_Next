using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Antelcat.I18N.Avalonia;
using LabelPlus_Next.Models;

namespace LabelPlus_Next.Services;

/// <summary>
/// Applies the selected UI language to Antelcat I18N and exposes the available
/// language options for the Settings page.
/// </summary>
public static class LocalizationService
{
    /// <summary>The culture currently applied to the UI (also used by UiText.Tr).</summary>
    public static CultureInfo CurrentCulture { get; private set; } = CultureInfo.CurrentUICulture;

    /// <summary>Raised after the UI culture changed, for strings built in code.</summary>
    public static event EventHandler? CultureChanged;

    public static IReadOnlyList<LanguageOption> Options { get; } = new[]
    {
        new LanguageOption("default"),
        new LanguageOption("zh-CN", "简体中文"),
        new LanguageOption("en", "English"),
        new LanguageOption("zh-hant-tw", "繁體中文")
    };

    public static LanguageOption Resolve(string? code)
    {
        var normalized = string.IsNullOrWhiteSpace(code) ? "default" : code;
        return Options.FirstOrDefault(o => string.Equals(o.Code, normalized, StringComparison.OrdinalIgnoreCase))
               ?? Options[0];
    }

    public static void Apply(string? code)
    {
        CultureInfo culture;
        try
        {
            culture = IsSystemDefault(code) ? CultureInfo.CurrentUICulture : new CultureInfo(code!);
        }
        catch (CultureNotFoundException)
        {
            culture = CultureInfo.CurrentUICulture;
        }
        catch (Exception)
        {
            culture = CultureInfo.CurrentUICulture;
        }

        CurrentCulture = culture;
        I18NExtension.Culture = culture;
        CultureChanged?.Invoke(null, EventArgs.Empty);
    }

    private static bool IsSystemDefault(string? code)
        => string.IsNullOrWhiteSpace(code) || code.Equals("default", StringComparison.OrdinalIgnoreCase);
}
