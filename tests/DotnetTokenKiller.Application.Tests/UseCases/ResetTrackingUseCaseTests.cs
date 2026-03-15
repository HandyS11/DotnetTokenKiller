using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Domain.Tracking;
using NSubstitute;
using Xunit;

namespace DotnetTokenKiller.Application.Tests.UseCases;

public class ResetTrackingUseCaseTests
{
    private readonly ITracker _tracker = Substitute.For<ITracker>();
    private readonly ResetTrackingUseCase _sut;

    public ResetTrackingUseCaseTests()
    {
        _sut = new ResetTrackingUseCase(_tracker);
    }

    [Fact]
    public async Task ResetAsync_DelegatesTo_Tracker()
    {
        await _sut.ResetAsync();

        await _tracker.Received(1).ResetAsync(Arg.Any<CancellationToken>());
    }
}
