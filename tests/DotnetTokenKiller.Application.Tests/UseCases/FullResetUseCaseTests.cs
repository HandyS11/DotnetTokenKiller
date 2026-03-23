using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Domain.Configuration;
using DotnetTokenKiller.Domain.Tee;
using DotnetTokenKiller.Domain.Tracking;
using NSubstitute;
using Xunit;

namespace DotnetTokenKiller.Application.Tests.UseCases;

public class FullResetUseCaseTests
{
    private readonly ITracker _tracker = Substitute.For<ITracker>();
    private readonly IConfigProvider _configProvider = Substitute.For<IConfigProvider>();
    private readonly ITeeService _teeService = Substitute.For<ITeeService>();
    private readonly FullResetUseCase _sut;

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
