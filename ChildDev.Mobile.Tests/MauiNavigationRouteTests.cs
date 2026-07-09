using LevelUp.Services;
using Xunit;

namespace LevelUp.Tests;

// Regression guard for the Google Play "crashes after opening" fix: navigating to a STANDALONE
// global route (reminders, settings) with the "///" absolute prefix throws in MAUI Shell
// ("Global routes currently cannot be the only page on the stack") and hard-crashes the app.
// Those must resolve to a RELATIVE route; tab-prefixed sub-routes still need the absolute prefix.
public class MauiNavigationRouteTests
{
    [Theory]
    // Standalone global routes -> RELATIVE (no prefix). This is the crash fix.
    [InlineData("reminders", "reminders")]
    [InlineData("settings", "settings")]
    // Tab-prefixed sub-routes -> absolute "///" (the tab page sits beneath them).
    [InlineData("journal/entry", "///journal/entry")]
    [InlineData("goals/entry", "///goals/entry")]
    [InlineData("todos/entry", "///todos/entry")]
    [InlineData("goals/entry?guid=abc", "///goals/entry?guid=abc")]
    // Tab routes and back-nav pass through unchanged.
    [InlineData("//goals", "//goals")]
    [InlineData("//journal", "//journal")]
    [InlineData("..", "..")]
    public void AbsoluteRoute_MapsRouteCorrectly(string input, string expected) =>
        Assert.Equal(expected, MauiNavigationService.AbsoluteRoute(input));
}
