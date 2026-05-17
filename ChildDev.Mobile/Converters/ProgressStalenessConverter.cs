using System.Globalization;
using Microsoft.Maui.Controls;

namespace ChildDev.Mobile.Converters;

public class ProgressStalenessConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not long ms) return null;
        var updatedAt = DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime;
        var days = (int)(DateTime.Now - updatedAt).TotalDays;
        if (days == 0) return "Updated today";
        if (days == 1) return "Updated yesterday";
        if (days < 14) return $"Updated {days}d ago";
        return $"No update in {days}d";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

public class ProgressStalenessColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not long ms) return Colors.Gray;
        var days = (int)(DateTime.Now - DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime).TotalDays;
        return days >= 14 ? Colors.Orange : Colors.Gray;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}
