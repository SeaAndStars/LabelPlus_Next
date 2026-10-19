using Antelcat.I18N.Avalonia;

namespace LabelPlus_Next.Services;

/// <summary>Small helper for localizing UI strings produced in code-behind/view models.</summary>
public static class UiText
{
    public static string T(string key, string fallback)
        => I18NExtension.Translate(key) ?? fallback;

    public static string F(string key, string fallback, params object?[] args)
        => string.Format(T(key, fallback), args);
}
