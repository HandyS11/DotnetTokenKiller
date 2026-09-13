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
                r.Outcome == RunOutcome.PassthroughMeasured &&
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
                r.SavedTokens == 0 &&
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
                r.Outcome == RunOutcome.PassthroughUnmeasured &&
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
            Arg.Is<CommandRecord>(r => r.Command == "list package"),
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
            Arg.Is<CommandRecord>(r => r.InputTokens > 0),
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
        await _runner.Received(1).RunPassthroughAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_StreamsAndTees_WhenTrackingIsOffAndTeeIsAtItsDefault()
    {
        // Failures is the shipped default for Tee.Mode. This is what a user actually gets by simply
        // turning tracking off, unlike the Always/Never configs the other tee-and-tracking tests use.
        var config = DtkConfig.Default with
        {
            Tracking = DtkConfig.Default.Tracking with { Enabled = false }
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
    public async Task RunAsync_CompletesAndTees_WhenTrackerIsNull()
    {
        // PassthroughEntryPoint constructs the use case exactly this way when tracking is off — the
        // shape production actually uses. No other test in this fixture passes a null tracker, so the
        // `tracker is null` half of TrackAsync's gate was previously only exercised by production, and
        // if the guard were ever removed this would fail with a NullReferenceException instead of
        // completing.
        var sut = new PassthroughRunUseCase(_runner, tracker: null, _teeService, _stdOut, _stdErr);
        var session = Substitute.For<ITeeSession>();
        session.Writer.Returns(TextWriter.Null);
        _teeService.BeginAsync(Arg.Any<string>(), Arg.Any<TeeLogHeader>(), Arg.Any<CancellationToken>())
            .Returns(session);
        _runner.RunStreamedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<TextWriter>(), Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("out", "", 0));

        var exitCode = await sut.RunAsync(DtkConfig.Default, "dotnet", ["publish"]);

        exitCode.Should().Be(0);
        await session.Received(1).FinalizeAsync(0, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_TeeFinalizeThrows_DoesNotSurfaceExceptionAndKeepsTheExitCode()
    {
        // Mirrors FilteredOutputPipeline's guard: a broken tee stream's FinalizeAsync must never
        // cost the caller the child's real exit code, and PassthroughEntryPoint calls this use case
        // outside Program.cs's own try/catch, so an unguarded throw here would surface as a raw
        // stack trace instead of the child's exit code.
        var session = Substitute.For<ITeeSession>();
        session.Writer.Returns(TextWriter.Null);
        session.FinalizeAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new IOException("disk full"));
        _teeService.BeginAsync(Arg.Any<string>(), Arg.Any<TeeLogHeader>(), Arg.Any<CancellationToken>())
            .Returns(session);
        _runner.RunStreamedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<TextWriter>(), Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("out", "", 5));

        var exitCode = await _sut.RunAsync(DtkConfig.Default, "dotnet", PublishArgs);

        exitCode.Should().Be(5);
    }

    [Fact]
    public async Task RunAsync_FansStdOutToTerminalAndSession()
    {
        // Proves the streamed sinks are actually FanOutTextWriters over session.Writer, not stdOut
        // passed straight through: an implementation that wired RunStreamedAsync directly to stdOut
        // would still make every other test in this file pass, since they all use TextWriter.Null for
        // the session and Arg.Any<TextWriter>() for the runner's sink arguments.
        var sessionWriter = new StringWriter { NewLine = "\n" };
        var session = Substitute.For<ITeeSession>();
        session.Writer.Returns(sessionWriter);
        _teeService.BeginAsync(Arg.Any<string>(), Arg.Any<TeeLogHeader>(), Arg.Any<CancellationToken>())
            .Returns(session);
        _runner.RunStreamedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<TextWriter>(), Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var outWriter = callInfo.ArgAt<TextWriter>(2);
                outWriter.WriteLineAsync("stdout line".AsMemory(), CancellationToken.None)
                    .GetAwaiter().GetResult();
                return new CommandResult("stdout line", "", 0);
            });

        await _sut.RunAsync(DtkConfig.Default, "dotnet", ["publish"]);

        _stdOut.ToString().Should().Contain("stdout line");
        sessionWriter.ToString().Should().Contain("stdout line");
    }

    [Fact]
    public async Task RunAsync_MeasurableSubcommand_StartsTrackerSetupBeforeTheCommandFinishes()
    {
        var warmUpStarted = SignalWhenWarmUpStarts();
        var startedWhileChildRan = false;
        _runner.RunStreamedAsync("dotnet", Arg.Any<IReadOnlyList<string>>(), Arg.Any<TextWriter>(),
                Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                // Captured into a local: VSTHRD003 flags awaiting a Task read directly from a property.
                var pending = warmUpStarted.Task;
                var first = await Task.WhenAny(pending, Task.Delay(TimeSpan.FromSeconds(5)));
                startedWhileChildRan = first == pending;
                return new CommandResult("publish output", "", 0);
            });

        await _sut.RunAsync(DtkConfig.Default, "dotnet", PublishArgs);

        startedWhileChildRan.Should().BeTrue();
        await _tracker.Received(1).WarmUpAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_NonMeasurableSubcommand_StartsTrackerSetupBeforeTheCommandFinishes()
    {
        var warmUpStarted = SignalWhenWarmUpStarts();
        var startedWhileChildRan = false;
        _runner.RunPassthroughAsync("dotnet", Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                // Captured into a local: VSTHRD003 flags awaiting a Task read directly from a property.
                var pending = warmUpStarted.Task;
                var first = await Task.WhenAny(pending, Task.Delay(TimeSpan.FromSeconds(5)));
                startedWhileChildRan = first == pending;
                return 0;
            });

        await _sut.RunAsync(DtkConfig.Default, "dotnet", RunArgs);

        startedWhileChildRan.Should().BeTrue();
        await _tracker.Received(1).WarmUpAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_TrackingOffButTeeOn_DoesNotWarmTheTracker()
    {
        var config = DtkConfig.Default with { Tracking = new TrackingConfig(Enabled: false) };
        _runner.RunStreamedAsync("dotnet", Arg.Any<IReadOnlyList<string>>(), Arg.Any<TextWriter>(),
                Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("publish output", "", 0));

        await _sut.RunAsync(config, "dotnet", PublishArgs);

        await _tracker.DidNotReceive().WarmUpAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_TrackerWarmUpThrows_StillRecordsAndKeepsTheExitCode()
    {
        _tracker.WarmUpAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("db locked"));
        _runner.RunStreamedAsync("dotnet", Arg.Any<IReadOnlyList<string>>(), Arg.Any<TextWriter>(),
                Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("publish output", "", 4));

        var exitCode = await _sut.RunAsync(DtkConfig.Default, "dotnet", PublishArgs);

        exitCode.Should().Be(4);
        await _tracker.Received(1).RecordAsync(Arg.Any<CommandRecord>(), Arg.Any<CancellationToken>());
    }

    /// <summary>Makes the tracker's warm-up signal the returned source when it starts.</summary>
    private TaskCompletionSource SignalWhenWarmUpStarts()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _tracker.WarmUpAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                started.TrySetResult();
                return Task.CompletedTask;
            });
        return started;
    }

    [Fact]
    public async Task RunAsync_FansStdErrToTerminalAndSession()
    {
        // Same proof as RunAsync_FansStdOutToTerminalAndSession, for the stderr sink.
        var sessionWriter = new StringWriter { NewLine = "\n" };
        var session = Substitute.For<ITeeSession>();
        session.Writer.Returns(sessionWriter);
        _teeService.BeginAsync(Arg.Any<string>(), Arg.Any<TeeLogHeader>(), Arg.Any<CancellationToken>())
            .Returns(session);
        _runner.RunStreamedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<TextWriter>(), Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var errWriter = callInfo.ArgAt<TextWriter>(3);
                errWriter.WriteLineAsync("stderr line".AsMemory(), CancellationToken.None)
                    .GetAwaiter().GetResult();
                return new CommandResult("", "stderr line", 0);
            });

        await _sut.RunAsync(DtkConfig.Default, "dotnet", ["publish"]);

        _stdErr.ToString().Should().Contain("stderr line");
        sessionWriter.ToString().Should().Contain("stderr line");
    }
}
