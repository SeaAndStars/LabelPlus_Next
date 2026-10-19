using Avalonia.Data;
using Avalonia.Data.Converters;
using System.Globalization;

namespace LabelPlus_Next.Converters;

/// <summary>
/// Two-way bridge between an enum property and a group of radio buttons:
/// IsChecked is true when the value's name equals the ConverterParameter, and checking a button writes that member back.
/// </summary>
public sealed class EnumEqualsConverter : IValueConverter
{
    public static readonly EnumEqualsConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is Enum e && parameter is string name && string.Equals(e.ToString(), name, StringComparison.Ordinal);

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is true && parameter is string name)
        {
            var enumType = Nullable.GetUnderlyingType(targetType) ?? targetType;
            if (enumType.IsEnum && Enum.TryParse(enumType, name, out var result)) return result;
        }
        // Unchecking one radio button must not reset the mode; the newly checked one sets it
        return BindingOperations.DoNothing;
    }
}
