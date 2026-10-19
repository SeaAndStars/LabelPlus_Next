using Antelcat.I18N.Avalonia;
using LabelPlus_Next.Lang;

namespace LabelPlus_Next.Models;

/// <summary>A selectable UI language shown on the Settings page.</summary>
public sealed class LanguageOption
{
    private readonly string? _displayName;

    public LanguageOption(string code, string? displayName = null)
    {
        Code = code;
        _displayName = displayName;
    }

    public string Code { get; }

    /// <summary>
    /// Native language names stay in their own language; the "follow system" entry
    /// is translated dynamically so it follows the current UI culture.
    /// </summary>
    public string DisplayName => _displayName
        ?? I18NExtension.Translate(LangKeys.languageSystemDefault)
        ?? LangKeys.languageSystemDefault;
}
