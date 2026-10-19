using System;
using System.Globalization;
using Antelcat.I18N.Avalonia;
using Avalonia.Data.Converters;

namespace LabelPlus_Next.Converters;

/// <summary>
/// Formats a numeric value with a localized pattern identified by ConverterParameter.
/// The pattern resources use {0} as the placeholder, e.g. "{0} projects".
/// </summary>
public sealed class LocalizedCountConverter : IValueConverter
{
    public static readonly LocalizedCountConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (parameter is not string key) return value?.ToString();
        var format = I18NExtension.Translate(key);
        if (string.IsNullOrEmpty(format)) return value?.ToString();
        try
        {
            return string.Format(culture, format, value);
        }
        catch (FormatException)
        {
            return value?.ToString();
        }
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

/// <summary>
/// Shows a localized placeholder when the bound string is null or empty.
/// </summary>
public sealed class LocalizedFallbackConverter : IValueConverter
{
    public static readonly LocalizedFallbackConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string s && !string.IsNullOrWhiteSpace(s)) return s;
        if (parameter is string key)
        {
            var localized = I18NExtension.Translate(key);
            if (!string.IsNullOrEmpty(localized)) return localized;
        }
        return value;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
