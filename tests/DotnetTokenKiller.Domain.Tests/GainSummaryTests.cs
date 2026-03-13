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
            ["test"] = 1200
        };

        var summary = new GainSummary(
            10,
            5000,
            950,
            4050,
            81.0,
            savedByCommand);

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
            0,
            0,
            0,
            0,
            0.0,
            new Dictionary<string, int>());

        summary.TotalCommands.Should().Be(0);
        summary.SavedByCommand.Should().BeEmpty();
    }
}
