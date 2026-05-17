using System.Globalization;

namespace ChildDev.Mobile.Converters;

public class EntryDateConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not long ms) return null;
        var dt = DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime;
        return dt.Year == DateTime.Today.Year ? dt.ToString("ddd, MMM d") : dt.ToString("ddd, MMM d yyyy");
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

public class NotNullConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}


public class ExpirationDateConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not long ms) return null;
        var dt = DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime;
        return dt.Year == DateTime.Today.Year ? $"Exp: {dt:MMM d}" : $"Exp: {dt:MMM d yyyy}";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

public class ExpirationColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not long ms) return Colors.Gray;
        var dt = DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime.Date;
        var diff = (dt - DateTime.Today).Days;
        return diff switch
        {
            < 0 => Colors.Red,
            <= 30 => Colors.Orange,
            _ => Colors.Gray
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

public class MeetingDateConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not long ms) return null;
        var dt = DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime;
        return dt.Year == DateTime.Today.Year ? $"Meet: {dt:ddd, MMM d}" : $"Meet: {dt:ddd, MMM d yyyy}";
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

public class DueDateLabelConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not long ms) return null;
        var due = DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime.Date;
        var today = DateTime.Today;
        var diff = (due - today).Days;
        return diff switch
        {
            < 0 => $"Overdue {-diff}d",
            0 => "Due today",
            1 => "Due tomorrow",
            <= 6 => $"Due {due:ddd}",
            _ => $"Due {due:MMM d}"
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}

public class DueDateColorConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not long ms) return Colors.Gray;
        var due = DateTimeOffset.FromUnixTimeMilliseconds(ms).LocalDateTime.Date;
        var diff = (due - DateTime.Today).Days;
        return diff switch
        {
            < 0 => Colors.Red,
            0 => Colors.Orange,
            _ => Colors.Gray
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotImplementedException();
}
