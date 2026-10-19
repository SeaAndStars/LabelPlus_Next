using Avalonia.Data.Converters;
using Avalonia.Media;

namespace LabelPlus_Next.Converters;

public sealed class StatusToBadgeBackgroundConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        var s = value as string ?? string.Empty;
        return s switch
        {
            "发布" => Brushes.SeaGreen,
            "嵌字" => Brushes.MediumPurple,
            "校对" => Brushes.DarkOrange,
            "翻译" => Brushes.DodgerBlue,
            _ => Brushes.Gray
        };
    }
    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => null;
}

public sealed class StatusToBadgeForegroundConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        // Use white foreground for colored backgrounds
        var s = value as string ?? string.Empty;
        return s switch
        {
            "发布" => Brushes.White,
            "嵌字" => Brushes.White,
            "校对" => Brushes.White,
            "翻译" => Brushes.White,
            _ => Brushes.White
        };
    }
    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) => null;
}
