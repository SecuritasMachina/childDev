using LevelUp.Services;

namespace LevelUp.Tests;

public class SnoozeHelperTests : ViewModelTestBase
{
    [Theory]
    [InlineData("1 hour", 1, 0)]
    [InlineData("8 hours", 8, 0)]
    [InlineData("1 day", 0, 1)]
    [InlineData("3 days", 0, 3)]
    public async Task PickAsync_BuiltInChoices_ReturnsCorrectDuration(string choice, int expectedHours, int expectedDays)
    {
        Nav.ActionSheetResult = choice;
        var result = await SnoozeHelper.PickAsync(Nav);

        Assert.NotNull(result);
        var expected = expectedDays > 0 ? TimeSpan.FromDays(expectedDays) : TimeSpan.FromHours(expectedHours);
        Assert.Equal(expected, result.Value);
    }

    [Fact]
    public async Task PickAsync_Cancel_ReturnsNull()
    {
        Nav.ActionSheetResult = null;
        var result = await SnoozeHelper.PickAsync(Nav);
        Assert.Null(result);
    }

    [Fact]
    public async Task PickAsync_CancelString_ReturnsNull()
    {
        Nav.ActionSheetResult = "Cancel";
        var result = await SnoozeHelper.PickAsync(Nav);
        Assert.Null(result);
    }

    [Theory]
    [InlineData("Hours", 3, 3, 0)]
    [InlineData("Days", 2, 0, 2)]
    [InlineData("Weeks", 2, 0, 14)]
    [InlineData("Months", 1, 0, 30)]
    public async Task PickAsync_Custom_ReturnsCorrectDuration(string unit, int amount, int expectedHours, int expectedDays)
    {
        Nav.PromptResult = amount.ToString();
        // First action sheet = "Custom...", second = unit
        var nav = new SequencedNavService([("Custom...", null), (unit, null)], amount.ToString());
        var result = await SnoozeHelper.PickAsync(nav);

        Assert.NotNull(result);
        var expected = expectedDays > 0 ? TimeSpan.FromDays(expectedDays) : TimeSpan.FromHours(expectedHours);
        Assert.Equal(expected, result.Value);
    }

    [Fact]
    public async Task PickAsync_CustomCancelledAtPrompt_ReturnsNull()
    {
        var nav = new SequencedNavService([("Custom...", null)], null);
        var result = await SnoozeHelper.PickAsync(nav);
        Assert.Null(result);
    }

    [Fact]
    public async Task PickAsync_CustomInvalidAmount_ReturnsNull()
    {
        var nav = new SequencedNavService([("Custom...", null)], "abc");
        var result = await SnoozeHelper.PickAsync(nav);
        Assert.Null(result);
    }

    [Fact]
    public async Task PickAsync_CustomZeroAmount_ReturnsNull()
    {
        var nav = new SequencedNavService([("Custom...", null)], "0");
        var result = await SnoozeHelper.PickAsync(nav);
        Assert.Null(result);
    }

    [Fact]
    public async Task PickAsync_CustomUnitCancelled_ReturnsNull()
    {
        var nav = new SequencedNavService([("Custom...", null), (null, null)], "5");
        var result = await SnoozeHelper.PickAsync(nav);
        Assert.Null(result);
    }
}

/// <summary>
/// Navigation fake that returns sequenced action sheet results and a single prompt result.
/// </summary>
public class SequencedNavService(IEnumerable<(string? ActionResult, string? Unused)> actionResults, string? promptResult) : INavigationService
{
    private readonly Queue<string?> _actionResults = new(actionResults.Select(r => r.ActionResult));

    public Task GoToAsync(string route) => Task.CompletedTask;
    public Task<bool> DisplayAlertAsync(string title, string message, string accept, string cancel) => Task.FromResult(true);
    public Task AlertAsync(string title, string message, string cancel) => Task.CompletedTask;
    public Task<string?> DisplayPromptAsync(string title, string message, string accept, string cancel, string placeholder, int maxLength)
        => Task.FromResult(promptResult);
    public Task<string?> DisplayActionSheetAsync(string title, string cancel, string? destruction, params string[] buttons)
        => Task.FromResult(_actionResults.Count > 0 ? _actionResults.Dequeue() : null);
}
