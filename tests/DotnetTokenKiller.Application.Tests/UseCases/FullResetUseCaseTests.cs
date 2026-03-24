using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Domain.Configuration;
using DotnetTokenKiller.Domain.Tee;
using DotnetTokenKiller.Domain.Tracking;
using NSubstitute;

namespace DotnetTokenKiller.Application.Tests.UseCases;

public class FullResetUseCaseTests
{
    private readonly IConfigProvider _configProvider = Substitute.For<IConfigProvider>();
    private readonly FullResetUseCase _sut;
    private readonly ITeeService _teeService = Substitute.For<ITeeService>();
    private readonly ITracker _tracker = Substitute.For<ITracker>();

    public FullResetUseCaseTests()
    {
        _sut = new FullResetUseCase(_tracker, _configProvider, _teeService);
    }

    [Fact]
    public async Task ResetAsync_ClearsTrackingData()
    {
        await _sut.ResetAsync();

        await _tracker.Received(1).ResetAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResetAsync_DeletesTeeeLogs()
    {
        await _sut.ResetAsync();

        await _teeService.Received(1).DeleteLogsAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResetAsync_DeletesConfigFile()
    {
        await _sut.ResetAsync();

        await _configProvider.Received(1).DeleteAsync(Arg.Any<CancellationToken>());
    }
}
