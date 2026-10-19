using Avalonia.Data;
using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace LabelPlus_Next.Converters;

/// <summary>
/// Returns true when the bound string equals the converter parameter. Used for
/// lightweight selected-state styling (for example the 框内 / 框外 chips).
/// </summary>
public sealed class StringEqualsConverter : IValueConverter
{
    public static readonly StringEqualsConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => string.Equals(value as string, parameter as string, StringComparison.Ordinal);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => BindingOperations.DoNothing;
}
