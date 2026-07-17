using DotnetTokenKiller.Domain.Tracking;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Domain.Tests;

public class GainSummaryTests
{
    [Fact]
    public void GainSummary_stores_all_fields()
    {
        var commandDetails = new Dictionary<string, CommandGainDetail>
        {
            ["build"] = new(5, 2500, 400, 850, 82.0),
            ["test"] = new(5, 2500, 550, 1200, 80.0)
        };

        var summary = new GainSummary(
            10,
            5000,
            950,
            4050,
            81.0,
            commandDetails,
            TimeSpan.FromSeconds(3));

        summary.TotalCommands.Should().Be(10);
        summary.TotalInputTokens.Should().Be(5000);
        summary.TotalOutputTokens.Should().Be(950);
        summary.TotalSavedTokens.Should().Be(4050);
        summary.AverageSavingsPercentage.Should().Be(81.0);
        summary.CommandDetails.Should().ContainKey("build")
            .WhoseValue.TotalSavedTokens.Should().Be(850);
        summary.CommandDetails.Should().ContainKey("test")
            .WhoseValue.TotalSavedTokens.Should().Be(1200);
        summary.TotalExecutionTime.Should().Be(TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void GainSummary_with_empty_command_details()
    {
        var summary = new GainSummary(
            0,
            0,
            0,
            0,
            0.0,
            new Dictionary<string, CommandGainDetail>());

        summary.TotalCommands.Should().Be(0);
        summary.CommandDetails.Should().BeEmpty();
    }
}
