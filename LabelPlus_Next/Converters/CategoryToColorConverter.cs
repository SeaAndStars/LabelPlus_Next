using Avalonia.Data.Converters;
using Avalonia.Media;
using System;
using System.Globalization;

namespace LabelPlus_Next.Converters
{
    public class CategoryToColorConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            var category = value as string;
            return category switch
            {
                "¿òÄÚ" => Brushes.Red,
                "¿òÍâ" => Brushes.Blue,
                _ => Brushes.Black
            };
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}
