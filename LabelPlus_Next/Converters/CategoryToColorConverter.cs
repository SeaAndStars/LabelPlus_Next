using Avalonia.Data.Converters;
using Avalonia.Media;
using System.Globalization;

namespace LabelPlus_Next.Converters;

public class CategoryToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var category = value as string;
        return category switch
        {
            // Same hues as the overlay markers, readable on both light and dark themes
            "框内" => Brushes.OrangeRed,
            "框外" => Brushes.DodgerBlue,
            _ => Avalonia.AvaloniaProperty.UnsetValue
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
}
