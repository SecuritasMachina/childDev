using System.Globalization;
using Microsoft.Maui.Controls;

namespace LevelUp.Converters;

public class CategoryChipBgConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var selected = value as string ?? "All";
        var chip = parameter as string ?? string.Empty;
        return selected == chip ? Color.FromArgb("#5C35D9") : Colors.Transparent;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

public class CategoryChipFgConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var selected = value as string ?? "All";
        var chip = parameter as string ?? string.Empty;
        return selected == chip ? Colors.White : Color.FromArgb("#5C35D9");
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}
