namespace LevelUp.Services;

public static class SnoozeHelper
{
    public static async Task<TimeSpan?> PickAsync(INavigationService nav)
    {
        var choice = await nav.DisplayActionSheetAsync(
            "Remind me in...", "Cancel", null,
            "1 hour", "8 hours", "1 day", "3 days", "Custom...");

        return choice switch
        {
            "1 hour" => TimeSpan.FromHours(1),
            "8 hours" => TimeSpan.FromHours(8),
            "1 day" => TimeSpan.FromDays(1),
            "3 days" => TimeSpan.FromDays(3),
            "Custom..." => await PickCustomAsync(nav),
            _ => null
        };
    }

    private static async Task<TimeSpan?> PickCustomAsync(INavigationService nav)
    {
        var amountStr = await nav.DisplayPromptAsync(
            "Custom Reminder", "How many?", "OK", "Cancel", "e.g. 2", 4);
        if (amountStr is null || !int.TryParse(amountStr, out int amount) || amount <= 0)
            return null;

        var unit = await nav.DisplayActionSheetAsync(
            "Choose unit", "Cancel", null, "Hours", "Days", "Weeks", "Months");

        return unit switch
        {
            "Hours" => TimeSpan.FromHours(amount),
            "Days" => TimeSpan.FromDays(amount),
            "Weeks" => TimeSpan.FromDays(amount * 7),
            "Months" => TimeSpan.FromDays(amount * 30),
            _ => null
        };
    }
}
