using LevelUp.Models;

namespace LevelUp.Tests;

public class GoalModelTests
{
    [Fact]
    public void ShowNoNotesYet_NoProgressNoCompletion_True()
    {
        var g = new Goal();
        Assert.True(g.ShowNoNotesYet);
    }

    [Fact]
    public void ShowNoNotesYet_HasProgress_False()
    {
        var g = new Goal { LatestProgressAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
        Assert.False(g.ShowNoNotesYet);
    }

    [Fact]
    public void ShowNoNotesYet_HasCompletion_False()
    {
        var g = new Goal { CompletionDate = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
        Assert.False(g.ShowNoNotesYet);
    }

    [Theory]
    [InlineData(0, 0.0)]
    [InlineData(50, 0.5)]
    [InlineData(100, 1.0)]
    [InlineData(75, 0.75)]
    public void ProgressBarValue_ReturnsCorrectFraction(int percent, double expected)
    {
        var g = new Goal { ProgressPercent = percent };
        Assert.Equal(expected, g.ProgressBarValue, 2);
    }

    [Fact]
    public void ProgressBarValue_NullPercent_ReturnsZero()
    {
        var g = new Goal { ProgressPercent = null };
        Assert.Equal(0.0, g.ProgressBarValue);
    }

    [Theory]
    [InlineData(0, "")]
    [InlineData(1, "")]
    [InlineData(4, "")]
    [InlineData(5, "🌱 Beginner")]
    [InlineData(14, "🌱 Beginner")]
    [InlineData(15, "🚀 Apprentice")]
    [InlineData(29, "🚀 Apprentice")]
    [InlineData(30, "⭐ Skilled")]
    [InlineData(59, "⭐ Skilled")]
    [InlineData(60, "💎 Expert")]
    [InlineData(99, "💎 Expert")]
    [InlineData(100, "🏆 Master")]
    [InlineData(199, "🏆 Master")]
    [InlineData(200, "🌟 Legend")]
    [InlineData(500, "🌟 Legend")]
    public void TierLabel_ReturnsCorrectTier(int noteCount, string expected)
    {
        var g = new Goal { ProgressNotesCount = noteCount };
        Assert.Equal(expected, g.TierLabel);
    }
}
