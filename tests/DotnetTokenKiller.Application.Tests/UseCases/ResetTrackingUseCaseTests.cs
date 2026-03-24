using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Domain.Tracking;
using NSubstitute;

namespace DotnetTokenKiller.Application.Tests.UseCases;

public class ResetTrackingUseCaseTests
{
    private readonly ResetTrackingUseCase _sut;
    private readonly ITracker _tracker = Substitute.For<ITracker>();

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
