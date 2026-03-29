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

    [Fact]
    public void CommandGainDetail_DefaultSubDetails_AreNull()
    {
        var detail = new CommandGainDetail(5, 2500, 400, 2100, 84.0);

        detail.SuccessDetail.Should().BeNull();
        detail.FailureDetail.Should().BeNull();
    }

    [Fact]
    public void CommandGainDetail_StoresSuccessAndFailureDetail()
    {
        var successDetail = new CommandGainDetail(3, 1500, 250, 1250, 83.3);
        var failureDetail = new CommandGainDetail(2, 1000, 150, 850, 85.0);

        var detail = new CommandGainDetail(5, 2500, 400, 2100, 84.0, successDetail, failureDetail);

        detail.SuccessDetail.Should().Be(successDetail);
        detail.FailureDetail.Should().Be(failureDetail);
    }
}
