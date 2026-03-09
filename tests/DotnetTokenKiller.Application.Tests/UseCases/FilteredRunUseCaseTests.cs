using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Domain.Execution;
using DotnetTokenKiller.Domain.Filters;
using DotnetTokenKiller.Domain.Tee;
using DotnetTokenKiller.Domain.Tracking;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace DotnetTokenKiller.Application.Tests.UseCases;

public class FilteredRunUseCaseTests
{
    private static readonly string[] BuildArgs = ["build"];

    private readonly ICommandRunner _runner = Substitute.For<ICommandRunner>();
    private readonly ITracker _tracker = Substitute.For<ITracker>();
    private readonly ITeeService _teeService = Substitute.For<ITeeService>();
    private readonly IOutputFilter _filter = Substitute.For<IOutputFilter>();
    private readonly FilteredRunUseCase _sut;

    public FilteredRunUseCaseTests()
    {
        _sut = new FilteredRunUseCase(_runner, _tracker, _teeService);
    }

    [Fact]
    public async Task RunAsync_ReturnsExitCodeFromCommand()
    {
        _runner.RunCapturedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("output", "", 42));
        _filter.Apply(Arg.Any<string>()).Returns("filtered");
        _teeService.TeeAndHintAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        var exitCode = await _sut.RunAsync(_filter, "dotnet", BuildArgs, verbosityLevel: 0);

        exitCode.Should().Be(42);
    }

    [Fact]
    public async Task RunAsync_FilterThrows_FallsBackToRawOutput_StillReturnsExitCode()
    {
        _runner.RunCapturedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("raw output", "", 0));
        _filter.Apply(Arg.Any<string>()).Throws(new InvalidOperationException("boom"));
        _teeService.TeeAndHintAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        var act = async () => await _sut.RunAsync(_filter, "dotnet", BuildArgs, verbosityLevel: 0);

        await act.Should().NotThrowAsync();
        var exitCode = await _sut.RunAsync(_filter, "dotnet", BuildArgs, verbosityLevel: 0);
        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task RunAsync_TrackingThrows_DoesNotSurfaceException()
    {
        _runner.RunCapturedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("output", "", 0));
        _filter.Apply(Arg.Any<string>()).Returns("filtered");
        _tracker.RecordAsync(Arg.Any<CommandRecord>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("db error"));
        _teeService.TeeAndHintAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        var act = async () => await _sut.RunAsync(_filter, "dotnet", BuildArgs, verbosityLevel: 0);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RunAsync_TeeThrows_DoesNotSurfaceException()
    {
        _runner.RunCapturedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("output", "", 1));
        _filter.Apply(Arg.Any<string>()).Returns("filtered");
        _teeService.TeeAndHintAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("io error"));

        var act = async () => await _sut.RunAsync(_filter, "dotnet", BuildArgs, verbosityLevel: 0);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RunAsync_CallsFilterWithCombinedStrippedOutput()
    {
        const string stdout = "\x1b[32mHello\x1b[0m\n";
        const string stderr = "\x1b[31mError\x1b[0m\n";
        _runner.RunCapturedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult(stdout, stderr, 0));
        _filter.Apply(Arg.Any<string>()).Returns("ok");
        _teeService.TeeAndHintAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        await _sut.RunAsync(_filter, "dotnet", BuildArgs, verbosityLevel: 0);

        _filter.Received(1).Apply("Hello\nError\n");
    }
}
