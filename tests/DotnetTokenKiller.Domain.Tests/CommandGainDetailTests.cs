using DotnetTokenKiller.Domain.Tracking;
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Domain.Tests;

public class CommandGainDetailTests
{
    [Fact]
    public void CommandGainDetail_StoresAllProperties()
    {
        var detail = new CommandGainDetail(5, 2500, 400, 2100, 84.0);

        detail.RunCount.Should().Be(5);
        detail.TotalInputTokens.Should().Be(2500);
        detail.TotalOutputTokens.Should().Be(400);
        detail.TotalSavedTokens.Should().Be(2100);
        detail.AverageSavingsPercentage.Should().Be(84.0);
    }

    [Fact]
    public void CommandGainDetail_RecordEquality()
    {
        var a = new CommandGainDetail(5, 2500, 400, 2100, 84.0);
        var b = new CommandGainDetail(5, 2500, 400, 2100, 84.0);

        a.Should().Be(b);
    }
}
