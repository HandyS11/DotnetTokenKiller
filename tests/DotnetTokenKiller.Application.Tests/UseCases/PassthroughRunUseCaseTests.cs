using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Domain.Configuration;
using DotnetTokenKiller.Domain.Execution;
using DotnetTokenKiller.Domain.Tee;
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
    private readonly ITeeService _teeService = Substitute.For<ITeeService>();
    private readonly ITracker _tracker = Substitute.For<ITracker>();

    public PassthroughRunUseCaseTests()
    {
        var defaultSession = Substitute.For<ITeeSession>();
        defaultSession.Writer.Returns(TextWriter.Null);
        _teeService.BeginAsync(Arg.Any<string>(), Arg.Any<TeeLogHeader>(), Arg.Any<CancellationToken>())
            .Returns(defaultSession);

        _sut = new PassthroughRunUseCase(_runner, _tracker, _teeService, _stdOut, _stdErr);
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
    public async Task RunAsync_TrackingAndTeeDisabled_NeverCapturesAndNeverRecords()
    {
        // Both tracking and tee off is the escape hatch back to today's inherited-stdio behaviour.
        var config = DtkConfig.Default with
        {
            Tracking = new TrackingConfig(Enabled: false),
            Tee = new TeeConfig(TeeMode.Never)
        };
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
    public async Task RunAsync_MeasuredRunWithSubstantialOutput_TrackingThrows_KeepsTheExitCode()
    {
        // The exit code is captured before AnsiStrip.Strip/TokenEstimator.Estimate run. Both now
        // execute inside TrackAsync's try/catch alongside RecordAsync, so a large, ANSI-laden
        // captured output that exercises stripping and tokenization in full, followed by a failing
        // store write, must still leave the child's exit code untouched.
        var largeOutput = string.Join('\n',
            Enumerable.Repeat("[32mBuild succeeded.[0m 0 Warning(s) 0 Error(s)", 5000));
        _runner.RunStreamedAsync("dotnet", Arg.Any<IReadOnlyList<string>>(), Arg.Any<TextWriter>(),
                Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult(largeOutput, "", 3));
        _tracker.RecordAsync(Arg.Any<CommandRecord>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("db error"));

        var exitCode = await _sut.RunAsync(DtkConfig.Default, "dotnet", PublishArgs);

        exitCode.Should().Be(3);
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

    [Fact]
    public async Task RunAsync_TeesAMeasurableRun()
    {
        var session = Substitute.For<ITeeSession>();
        session.Writer.Returns(TextWriter.Null);
        _teeService.BeginAsync(Arg.Any<string>(), Arg.Any<TeeLogHeader>(), Arg.Any<CancellationToken>())
            .Returns(session);
        _runner.RunStreamedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<TextWriter>(), Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("out", "", 0));

        await _sut.RunAsync(DtkConfig.Default, "dotnet", ["publish"]);

        await session.Received(1).FinalizeAsync(0, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_StreamsAndTees_WhenTrackingIsOffButTeeIsOn()
    {
        // Keying the streamed path on tracking alone would silently produce no passthrough log for a
        // user who turned tracking off and left tee on.
        var config = DtkConfig.Default with
        {
            Tracking = DtkConfig.Default.Tracking with { Enabled = false },
            Tee = new TeeConfig(TeeMode.Always)
        };
        var session = Substitute.For<ITeeSession>();
        session.Writer.Returns(TextWriter.Null);
        _teeService.BeginAsync(Arg.Any<string>(), Arg.Any<TeeLogHeader>(), Arg.Any<CancellationToken>())
            .Returns(session);
        _runner.RunStreamedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<TextWriter>(), Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("out", "", 0));

        await _sut.RunAsync(config, "dotnet", ["publish"]);

        await session.Received(1).FinalizeAsync(0, Arg.Any<CancellationToken>());
        await _tracker.DidNotReceive().RecordAsync(Arg.Any<CommandRecord>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_TakesTheCheapPath_WhenBothTrackingAndTeeAreOff()
    {
        var config = DtkConfig.Default with
        {
            Tracking = DtkConfig.Default.Tracking with { Enabled = false },
            Tee = new TeeConfig(TeeMode.Never)
        };
        _runner.RunPassthroughAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<CancellationToken>()).Returns(0);

        await _sut.RunAsync(config, "dotnet", ["publish"]);

        await _runner.Received(1).RunPassthroughAsync(Arg.Any<string>(),
            Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>());
        await _teeService.DidNotReceive().BeginAsync(Arg.Any<string>(), Arg.Any<TeeLogHeader>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_DoesNotTeeAnInteractiveRun()
    {
        // run/watch keep their stdio attached to the terminal, so there is nothing to capture.
        _runner.RunPassthroughAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<CancellationToken>()).Returns(0);

        await _sut.RunAsync(DtkConfig.Default, "dotnet", ["run"]);

        await _teeService.DidNotReceive().BeginAsync(Arg.Any<string>(), Arg.Any<TeeLogHeader>(),
            Arg.Any<CancellationToken>());
    }
}
