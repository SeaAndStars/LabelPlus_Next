using System;
using Avalonia.Data.Converters;

namespace LabelPlus_Next.Converters;

public sealed class StatusToUrsaClassConverter : IValueConverter
{
    public static readonly StatusToUrsaClassConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        var s = (value as string)?.Trim();
        if (string.IsNullOrEmpty(s)) return "Neutral";
        return s switch
        {
            "翻译" => "Primary",
            "校对" => "Info",
            "嵌字" => "Warning",
            "完成" => "Success",
            "错误" => "Danger",
            _ => "Secondary"
        };
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => throw new NotSupportedException();
}
