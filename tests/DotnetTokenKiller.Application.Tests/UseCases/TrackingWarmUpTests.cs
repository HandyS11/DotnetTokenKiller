using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Domain.Configuration;
using DotnetTokenKiller.Domain.Tracking;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace DotnetTokenKiller.Application.Tests.UseCases;

public class TrackingWarmUpTests
{
    private readonly ITracker _tracker = Substitute.For<ITracker>();

    [Fact]
    public async Task Start_WarmsTheTrackerExactlyOnce()
    {
        await TrackingWarmUp.Start(_tracker, tokenizer: null, CancellationToken.None).WhenReadyAsync();

        await _tracker.Received(1).WarmUpAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task WhenReadyAsync_TrackerWarmUpFaults_CompletesWithoutThrowing()
    {
        _tracker.WarmUpAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("db locked"));

        var act = () => TrackingWarmUp.Start(_tracker, TokenizerModel.Cl100kBase, CancellationToken.None)
            .WhenReadyAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task WhenReadyAsync_TrackerWarmUpThrowsSynchronously_CompletesWithoutThrowing()
    {
        _tracker.WarmUpAsync(Arg.Any<CancellationToken>()).Throws(new InvalidOperationException("db locked"));

        var act = () => TrackingWarmUp.Start(_tracker, tokenizer: null, CancellationToken.None).WhenReadyAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task WhenReadyAsync_TokenAlreadyCancelled_CompletesWithoutThrowing()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        _tracker.WarmUpAsync(Arg.Any<CancellationToken>())
            .Returns(call => Task.FromCanceled(call.Arg<CancellationToken>()));

        var act = () => TrackingWarmUp.Start(_tracker, TokenizerModel.Cl100kBase, cts.Token).WhenReadyAsync();

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public void None_IsAlreadyComplete()
    {
        TrackingWarmUp.None.WhenReadyAsync().IsCompletedSuccessfully.Should().BeTrue();
    }
}
