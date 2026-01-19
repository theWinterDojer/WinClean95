using System;
using System.Globalization;
using System.Windows.Data;

namespace Cleaner.App.Converters;

public sealed class ProgressToWidthConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values.Length < 3)
        {
            return 0d;
        }

        var width = ToDouble(values[0]);
        var value = ToDouble(values[1]);
        var maximum = ToDouble(values[2]);

        if (width <= 0 || maximum <= 0)
        {
            return 0d;
        }

        var ratio = Math.Clamp(value / maximum, 0d, 1d);
        return width * ratio;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    private static double ToDouble(object? value)
    {
        return value switch
        {
            double number => number,
            float number => number,
            int number => number,
            long number => number,
            _ => double.TryParse(value?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
                ? parsed
                : 0d
        };
    }
}
