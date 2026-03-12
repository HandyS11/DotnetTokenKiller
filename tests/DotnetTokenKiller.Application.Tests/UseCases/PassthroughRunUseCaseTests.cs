using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Domain.Execution;
using DotnetTokenKiller.Domain.Tracking;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace DotnetTokenKiller.Application.Tests.UseCases;

public class PassthroughRunUseCaseTests
{
    private readonly ICommandRunner _commandRunner = Substitute.For<ICommandRunner>();
    private readonly ITracker _tracker = Substitute.For<ITracker>();
    private readonly PassthroughRunUseCase _sut;

    public PassthroughRunUseCaseTests()
    {
        _sut = new PassthroughRunUseCase(_commandRunner, _tracker);
    }

    [Fact]
    public async Task RunAsync_ReturnsCommandRunnerExitCode()
    {
        _commandRunner.RunPassthroughAsync("dotnet", Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(42);

        var result = await _sut.RunAsync("dotnet", ["new"], CancellationToken.None);

        result.Should().Be(42);
    }

    [Fact]
    public async Task RunAsync_ForwardsArgsToCommandRunner()
    {
        string[] args = ["new", "console", "-n", "MyApp"];
        _commandRunner.RunPassthroughAsync("dotnet", Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(0);

        await _sut.RunAsync("dotnet", args, CancellationToken.None);

        await _commandRunner.Received(1).RunPassthroughAsync("dotnet", args, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_CallsTrackerWithZeroTokenFields()
    {
        _commandRunner.RunPassthroughAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>())
            .Returns(0);

        await _sut.RunAsync("dotnet", ["watch"], CancellationToken.None);

        await _tracker.Received(1).RecordAsync(
            Arg.Is<CommandRecord>(r =>
                r.InputTokens == 0 &&
                r.OutputTokens == 0 &&
                r.SavedTokens == 0 &&
                Math.Abs(r.SavingsPercentage) < 1e-10 &&
                r.Command == "watch"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_TrackerThrows_DoesNotPropagateException()
    {
        _commandRunner.RunPassthroughAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<CancellationToken>())
            .Returns(0);
        _tracker.RecordAsync(Arg.Any<CommandRecord>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("db error"));

        var act = async () => await _sut.RunAsync("dotnet", ["new"], CancellationToken.None);

        await act.Should().NotThrowAsync();
    }
}
