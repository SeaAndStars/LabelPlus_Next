using Avalonia.Data.Converters;
using System.Globalization;

namespace LabelPlus_Next.Converters;

public sealed class BoolToDoubleConverter : IValueConverter
{
    public double TrueValue { get; set; } = double.NaN; // Auto
    public double FalseValue { get; set; } = 0d;        // Collapsed

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var b = value is bool v && v;
        return b ? TrueValue : FalseValue;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
