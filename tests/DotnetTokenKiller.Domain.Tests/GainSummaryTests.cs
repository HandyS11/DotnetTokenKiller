using DotnetTokenKiller.Domain.Tracking;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Domain.Tests;

public class GainSummaryTests
{
    [Fact]
    public void GainSummary_stores_all_fields()
    {
        var savedByCommand = new Dictionary<string, int>
        {
            ["build"] = 850,
            ["test"] = 1200,
        };

        var summary = new GainSummary(
            TotalCommands: 10,
            TotalInputTokens: 5000,
            TotalOutputTokens: 950,
            TotalSavedTokens: 4050,
            AverageSavingsPercentage: 81.0,
            SavedByCommand: savedByCommand);

        summary.TotalCommands.Should().Be(10);
        summary.TotalInputTokens.Should().Be(5000);
        summary.TotalOutputTokens.Should().Be(950);
        summary.TotalSavedTokens.Should().Be(4050);
        summary.AverageSavingsPercentage.Should().Be(81.0);
        summary.SavedByCommand.Should().ContainKey("build").WhoseValue.Should().Be(850);
        summary.SavedByCommand.Should().ContainKey("test").WhoseValue.Should().Be(1200);
    }

    [Fact]
    public void GainSummary_with_empty_saved_by_command()
    {
        var summary = new GainSummary(
            TotalCommands: 0,
            TotalInputTokens: 0,
            TotalOutputTokens: 0,
            TotalSavedTokens: 0,
            AverageSavingsPercentage: 0.0,
            SavedByCommand: new Dictionary<string, int>());

        summary.TotalCommands.Should().Be(0);
        summary.SavedByCommand.Should().BeEmpty();
    }
}
