using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Domain.Configuration;
using DotnetTokenKiller.Domain.Execution;
using DotnetTokenKiller.Domain.Tracking;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace DotnetTokenKiller.Application.Tests.UseCases;

public sealed class PassthroughRunUseCaseTests : IDisposable
{
    private static readonly string[] PublishArgs = ["publish", "-c", "Release"];
    private static readonly string[] RunArgs = ["run"];
    private readonly StringWriter _stdErr = new();
    private readonly StringWriter _stdOut = new();

    private readonly ICommandRunner _runner = Substitute.For<ICommandRunner>();
    private readonly PassthroughRunUseCase _sut;
    private readonly ITracker _tracker = Substitute.For<ITracker>();

    public PassthroughRunUseCaseTests()
    {
        _sut = new PassthroughRunUseCase(_runner, _tracker, _stdOut, _stdErr);
    }

    public void Dispose()
    {
        _stdOut.Dispose();
        _stdErr.Dispose();
    }

    [Fact]
    public async Task RunAsync_MeasurableSubcommand_StreamsAndRecordsMeasured()
    {
        _runner.RunStreamedAsync("dotnet", Arg.Any<IReadOnlyList<string>>(), Arg.Any<TextWriter>(),
                Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("a good deal of publish output", "", 0));

        var exitCode = await _sut.RunAsync(DtkConfig.Default, "dotnet", PublishArgs);

        exitCode.Should().Be(0);
        await _tracker.Received(1).RecordAsync(
            Arg.Is<CommandRecord>(r =>
                r!.Outcome == RunOutcome.PassthroughMeasured &&
                r.Command == "publish" &&
                r.InputTokens > 0),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_MeasurableSubcommand_RecordsZeroSavings()
    {
        // Nothing was filtered, so every input token also reached the caller.
        _runner.RunStreamedAsync("dotnet", Arg.Any<IReadOnlyList<string>>(), Arg.Any<TextWriter>(),
                Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("some output", "", 0));

        await _sut.RunAsync(DtkConfig.Default, "dotnet", PublishArgs);

        await _tracker.Received(1).RecordAsync(
            Arg.Is<CommandRecord>(r =>
                r!.SavedTokens == 0 &&
                r.SavingsPercentage == 0.0 &&
                r.InputTokens == r.OutputTokens),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_NonMeasurableSubcommand_UsesPassthroughAndRecordsUnmeasured()
    {
        _runner.RunPassthroughAsync("dotnet", Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(0);

        await _sut.RunAsync(DtkConfig.Default, "dotnet", RunArgs);

        await _runner.DidNotReceive().RunStreamedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<TextWriter>(), Arg.Any<TextWriter>(), Arg.Any<CancellationToken>());
        await _tracker.Received(1).RecordAsync(
            Arg.Is<CommandRecord>(r =>
                r!.Outcome == RunOutcome.PassthroughUnmeasured &&
                r.Command == "run" &&
                r.InputTokens == 0),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_TrackingDisabled_NeverCapturesAndNeverRecords()
    {
        // tracking.enabled = false is the escape hatch back to today's inherited-stdio behaviour.
        var config = DtkConfig.Default with { Tracking = new TrackingConfig(Enabled: false) };
        _runner.RunPassthroughAsync("dotnet", Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(0);

        await _sut.RunAsync(config, "dotnet", PublishArgs);

        await _runner.DidNotReceive().RunStreamedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<TextWriter>(), Arg.Any<TextWriter>(), Arg.Any<CancellationToken>());
        await _tracker.DidNotReceive().RecordAsync(Arg.Any<CommandRecord>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_ReturnsTheChildExitCode_WhenMeasured()
    {
        _runner.RunStreamedAsync("dotnet", Arg.Any<IReadOnlyList<string>>(), Arg.Any<TextWriter>(),
                Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("", "error text", 42));

        var exitCode = await _sut.RunAsync(DtkConfig.Default, "dotnet", PublishArgs);

        exitCode.Should().Be(42);
    }

    [Fact]
    public async Task RunAsync_ReturnsTheChildExitCode_WhenUnmeasured()
    {
        _runner.RunPassthroughAsync("dotnet", Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(7);

        var exitCode = await _sut.RunAsync(DtkConfig.Default, "dotnet", RunArgs);

        exitCode.Should().Be(7);
    }

    [Fact]
    public async Task RunAsync_TrackingThrows_DoesNotSurfaceExceptionAndKeepsTheExitCode()
    {
        // A broken tracking database must never change what a dotnet publish returns.
        _runner.RunStreamedAsync("dotnet", Arg.Any<IReadOnlyList<string>>(), Arg.Any<TextWriter>(),
                Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("output", "", 3));
        _tracker.RecordAsync(Arg.Any<CommandRecord>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("db error"));

        var exitCode = await _sut.RunAsync(DtkConfig.Default, "dotnet", PublishArgs);

        exitCode.Should().Be(3);
    }

    [Fact]
    public async Task RunAsync_RecordsQualifiedNameForListPackage()
    {
        _runner.RunStreamedAsync("dotnet", Arg.Any<IReadOnlyList<string>>(), Arg.Any<TextWriter>(),
                Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("output", "", 0));

        await _sut.RunAsync(DtkConfig.Default, "dotnet", ["list", "package", "--outdated"]);

        await _tracker.Received(1).RecordAsync(
            Arg.Is<CommandRecord>(r => r!.Command == "list package"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_CountsStderrTowardTheMeasuredTotal()
    {
        _runner.RunStreamedAsync("dotnet", Arg.Any<IReadOnlyList<string>>(), Arg.Any<TextWriter>(),
                Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("", "a warning printed to standard error", 0));

        await _sut.RunAsync(DtkConfig.Default, "dotnet", PublishArgs);

        await _tracker.Received(1).RecordAsync(
            Arg.Is<CommandRecord>(r => r!.InputTokens > 0),
            Arg.Any<CancellationToken>());
    }
}
