using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Domain.Tracking;
using FluentAssertions;
using NSubstitute;

namespace DotnetTokenKiller.Application.Tests.UseCases;

public class GainReportUseCaseTests
{
    private readonly ITracker _tracker = Substitute.For<ITracker>();
    private readonly GainReportUseCase _sut;

    public GainReportUseCaseTests()
    {
        _sut = new GainReportUseCase(_tracker);
    }

    [Fact]
    public async Task GetSummaryAsync_DelegatesCorrectDaysToTracker()
    {
        var expected = new GainSummary(0, 0, 0, 0, 0.0, new Dictionary<string, int>());
        _tracker.GetSummaryAsync(7, null, Arg.Any<CancellationToken>()).Returns(expected);

        var result = await _sut.GetSummaryAsync(7, null);

        await _tracker.Received(1).GetSummaryAsync(7, null, Arg.Any<CancellationToken>());
        result.Should().Be(expected);
    }

    [Fact]
    public async Task GetSummaryAsync_PassesProjectPath_WhenProvided()
    {
        var expected = new GainSummary(0, 0, 0, 0, 0.0, new Dictionary<string, int>());
        _tracker.GetSummaryAsync(Arg.Any<int>(), "/my/project", Arg.Any<CancellationToken>()).Returns(expected);

        await _sut.GetSummaryAsync(30, "/my/project");

        await _tracker.Received(1).GetSummaryAsync(Arg.Any<int>(), "/my/project", Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetSummaryAsync_PassesNullProjectPath_WhenNotProvided()
    {
        var expected = new GainSummary(0, 0, 0, 0, 0.0, new Dictionary<string, int>());
        _tracker.GetSummaryAsync(Arg.Any<int>(), null, Arg.Any<CancellationToken>()).Returns(expected);

        await _sut.GetSummaryAsync(30, null);

        await _tracker.Received(1).GetSummaryAsync(Arg.Any<int>(), null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task GetSummaryAsync_ReturnsSummaryFromTracker()
    {
        var expected = new GainSummary(5, 1000, 200, 800, 80.0, new Dictionary<string, int>
        {
            ["build"] = 800
        });
        _tracker.GetSummaryAsync(Arg.Any<int>(), Arg.Any<string?>(), Arg.Any<CancellationToken>()).Returns(expected);

        var result = await _sut.GetSummaryAsync(30, null);

        result.Should().Be(expected);
    }
}
