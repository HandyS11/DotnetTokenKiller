using System.Diagnostics;
using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Domain.Configuration;
using DotnetTokenKiller.Domain.Filters;
using DotnetTokenKiller.Domain.Tee;
using DotnetTokenKiller.Domain.Tracking;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace DotnetTokenKiller.Application.Tests.UseCases;

[Collection(NoColorEnvironment.Name)]
public class FilteredOutputPipelineTests
{
    private readonly IConfigProvider _configProvider = Substitute.For<IConfigProvider>();
    private readonly IOutputFilter _filter = Substitute.For<IOutputFilter>();
    private readonly FilteredOutputPipeline _sut;
    private readonly ITracker _tracker = Substitute.For<ITracker>();

    public FilteredOutputPipelineTests()
    {
        _configProvider.LoadAsync(Arg.Any<CancellationToken>()).Returns(DtkConfig.Default);
        _sut = new FilteredOutputPipeline(_tracker, TextWriter.Null, _configProvider);
    }

    private FilteredOutputRequest Request(
        string raw = "raw output",
        int exitCode = 0,
        RunSource source = RunSource.Run,
        string command = "dotnet build",
        OutputOptions? options = null) =>
        new(_filter, raw, exitCode, "build", command, source,
            options ?? new OutputOptions(), Stopwatch.GetTimestamp());

    private static ITeeSession SessionWithHint(string hint)
    {
        var session = Substitute.For<ITeeSession>();
        session.Writer.Returns(TextWriter.Null);
        session.FinalizeAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(hint);
        return session;
    }

    [Fact]
    public async Task ProcessAsync_ReturnsSuppliedExitCode()
    {
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");

        var exitCode = await _sut.ProcessAsync(Request(exitCode: 42), NullTeeSession.Instance);

        exitCode.Should().Be(42);
    }

    [Fact]
    public async Task ProcessAsync_StripsAnsiBeforePassingToTheFilter()
    {
        // The pipeline is the sole owner of ANSI stripping: every IOutputFilter.Apply implementation
        // assumes its input has already been stripped and does not strip again. This pins that
        // contract at the seam between the two, independent of any single filter's own behavior.
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");

        await _sut.ProcessAsync(Request(raw: "\x1b[32mBuild succeeded.\x1b[0m\n"), NullTeeSession.Instance);

        _filter.Received(1).Apply(Arg.Is<string>(s => !s.Contains('\x1b')), Arg.Any<int>());
    }

    [Fact]
    public async Task ProcessAsync_RecordsSuppliedSource()
    {
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");

        await _sut.ProcessAsync(Request(source: RunSource.Pipe), NullTeeSession.Instance);

        await _tracker.Received(1).RecordAsync(
            Arg.Is<CommandRecord>(r => r.Source == RunSource.Pipe && r.Command == "build"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_FilterThrows_RecordsFilterFaulted()
    {
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Throws(new InvalidOperationException("boom"));

        await _sut.ProcessAsync(Request(), NullTeeSession.Instance);

        await _tracker.Received(1).RecordAsync(
            Arg.Is<CommandRecord>(r => r.Outcome == RunOutcome.FilterFaulted),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_EmptyFilterOutputOnFailure_RecordsRawTailFallback()
    {
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("");

        await _sut.ProcessAsync(Request(exitCode: 1), NullTeeSession.Instance);

        await _tracker.Received(1).RecordAsync(
            Arg.Is<CommandRecord>(r => r.Outcome == RunOutcome.RawTailFallback),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_EmptyFilterOutputOnSuccess_WritesRawTailThenWarningLast()
    {
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("");
        await using var writer = new StringWriter();
        var sut = new FilteredOutputPipeline(_tracker, writer, _configProvider);

        await sut.ProcessAsync(Request(raw: "a\nb\n", command: "dotnet test"), NullTeeSession.Instance);

        writer.ToString().Should().Be("a\nb\n⚠ dotnet test: output not recognized, raw tail shown above\n");
    }

    [Fact]
    public async Task ProcessAsync_EmptyFilterOutputOnSuccess_RecordsRawTailFallback()
    {
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns(" \n");

        await _sut.ProcessAsync(Request(raw: "a\n"), NullTeeSession.Instance);

        await _tracker.Received(1).RecordAsync(
            Arg.Is<CommandRecord>(r => r.Outcome == RunOutcome.RawTailFallback && r.Success),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_RawTailFallbackOnFailure_EndsWithStatusLine()
    {
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("");
        await using var writer = new StringWriter();
        var sut = new FilteredOutputPipeline(_tracker, writer, _configProvider);
        var raw = string.Join('\n', Enumerable.Range(0, 60).Select(i => $"line {i}")) + "\n";

        await sut.ProcessAsync(Request(raw: raw, exitCode: 1), NullTeeSession.Instance);

        var text = writer.ToString();
        text.Should().EndWith("\n");
        var lines = text.TrimEnd('\n').Split('\n');
        lines.Should().HaveCount(41);
        lines[0].Should().Be("line 20");
        lines[^1].Should().Be("✗ dotnet build failed (exit 1)");
    }

    [Fact]
    public async Task ProcessAsync_RawTailFallbackWithLogHint_PutsTheHintBetweenTailAndStatusLine()
    {
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("");
        await using var writer = new StringWriter();
        var sut = new FilteredOutputPipeline(_tracker, writer, _configProvider);
        var request = Request(raw: "a\nb\n", exitCode: 1, options: new OutputOptions(ShowLogHint: true));

        await sut.ProcessAsync(request, SessionWithHint("[full output: /x.log]"));

        writer.ToString().Should().Be("a\nb\n[full output: /x.log]\n✗ dotnet build failed (exit 1)\n");
    }

    [Fact]
    public async Task ProcessAsync_RawTailFallbackOfEmptyRaw_WritesOnlyTheStatusLine()
    {
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("");
        await using var writer = new StringWriter();
        var sut = new FilteredOutputPipeline(_tracker, writer, _configProvider);

        await sut.ProcessAsync(Request(raw: "", exitCode: 7), NullTeeSession.Instance);

        writer.ToString().Should().Be("✗ dotnet build failed (exit 7)\n");
    }

    [Fact]
    public async Task ProcessAsync_ShowLogHint_PrecedesStatusLine()
    {
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("detail\n✓ dotnet build (1 project)\n");
        await using var writer = new StringWriter();
        var sut = new FilteredOutputPipeline(_tracker, writer, _configProvider);
        var request = Request(options: new OutputOptions(ShowLogHint: true));

        await sut.ProcessAsync(request, SessionWithHint("[full output: /x.log]"));

        writer.ToString().Should().Be("detail\n[full output: /x.log]\n✓ dotnet build (1 project)\n");
    }

    [Fact]
    public async Task ProcessAsync_ShowLogHintOnASingleLineOutput_PrecedesThatLine()
    {
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("✓ dotnet build (1 project)\n");
        await using var writer = new StringWriter();
        var sut = new FilteredOutputPipeline(_tracker, writer, _configProvider);
        var request = Request(options: new OutputOptions(ShowLogHint: true));

        await sut.ProcessAsync(request, SessionWithHint("[full output: /x.log]"));

        writer.ToString().Should().Be("[full output: /x.log]\n✓ dotnet build (1 project)\n");
    }

    [Fact]
    public async Task ProcessAsync_VeryVerbose_StatusLineStillLast()
    {
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("detail\n✓ dotnet build (1 project)\n");
        await using var writer = new StringWriter();
        var sut = new FilteredOutputPipeline(_tracker, writer, _configProvider);
        var request = Request(raw: "raw text\n", options: new OutputOptions(VerbosityLevel: 2, ShowLogHint: true));

        await sut.ProcessAsync(request, SessionWithHint("[full output: /x.log]"));

        // The --vv dump is written with WriteLineAsync, so its lines end in the platform newline (CRLF on Windows).
        var text = writer.ToString().ReplaceLineEndings("\n");
        text.Should().StartWith("[raw output]\n");
        text.Should().EndWith("detail\n[full output: /x.log]\n✓ dotnet build (1 project)\n");
    }

    [Fact]
    public async Task ProcessAsync_EmojiDisabled_RawTailWarningUsesAscii()
    {
        _configProvider.LoadAsync(Arg.Any<CancellationToken>())
            .Returns(DtkConfig.Default with { Display = new DisplayConfig(Emoji: false) });
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("");
        await using var writer = new StringWriter();
        var sut = new FilteredOutputPipeline(_tracker, writer, _configProvider);

        await sut.ProcessAsync(Request(raw: "a\n"), NullTeeSession.Instance);

        var lines = writer.ToString().TrimEnd('\n').Split('\n');
        lines[^1].Should().StartWith("WARN: ");
        writer.ToString().Should().NotContain("⚠");
    }

    [Theory]
    [InlineData("")]
    [InlineData("  \n\n")]
    public async Task ProcessAsync_EmptyFilterOutputOnSuccessWithNothingInRaw_WritesNoOutputStatusLine(string raw)
    {
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("");
        await using var writer = new StringWriter();
        var sut = new FilteredOutputPipeline(_tracker, writer, _configProvider);

        await sut.ProcessAsync(Request(raw: raw, command: "dotnet restore"), NullTeeSession.Instance);

        writer.ToString().Should().Be("✓ dotnet restore (no output)\n");
        await _tracker.Received(1).RecordAsync(
            Arg.Is<CommandRecord>(r => r.Outcome == RunOutcome.RawTailFallback),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_EmptyRawWithAStatusLineOut_RecordsZeroSavedTokens()
    {
        // The filter's own confirmation line costs more tokens than the empty raw output it replaces, so the
        // unclamped difference is negative and gain would report a loss for a run that saved nothing.
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("✓ dotnet format (nothing to format)\n");

        await _sut.ProcessAsync(Request(raw: "", command: "dotnet format"), NullTeeSession.Instance);

        await _tracker.Received(1).RecordAsync(
            Arg.Is<CommandRecord>(r => r.SavedTokens == 0 && Math.Abs(r.SavingsPercentage) < 0.001),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_EmptyFilterOutputOnFailureWithWhitespaceRaw_WritesOnlyTheFailureLine()
    {
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("");
        await using var writer = new StringWriter();
        var sut = new FilteredOutputPipeline(_tracker, writer, _configProvider);

        await sut.ProcessAsync(Request(raw: "  \n", exitCode: 2), NullTeeSession.Instance);

        writer.ToString().Should().Be("✗ dotnet build failed (exit 2)\n");
    }

    [Fact]
    public async Task ProcessAsync_FilterFaultedOnFailure_WritesFullRawThenStatusLine()
    {
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Throws(new InvalidOperationException("boom"));
        await using var writer = new StringWriter();
        var sut = new FilteredOutputPipeline(_tracker, writer, _configProvider);

        await sut.ProcessAsync(Request(raw: "a\nb\n", exitCode: 1), NullTeeSession.Instance);

        writer.ToString().Should().Be("a\nb\n✗ dotnet build failed (exit 1)\n");
        await _tracker.Received(1).RecordAsync(
            Arg.Is<CommandRecord>(r => r.Outcome == RunOutcome.FilterFaulted),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_FilterFaultedWithLogHint_PutsTheHintBetweenRawAndStatusLine()
    {
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Throws(new InvalidOperationException("boom"));
        await using var writer = new StringWriter();
        var sut = new FilteredOutputPipeline(_tracker, writer, _configProvider);
        var request = Request(raw: "a\nb\n", exitCode: 1, options: new OutputOptions(ShowLogHint: true));

        await sut.ProcessAsync(request, SessionWithHint("[full output: /x.log]"));

        writer.ToString().Should().Be("a\nb\n[full output: /x.log]\n✗ dotnet build failed (exit 1)\n");
    }

    [Fact]
    public async Task ProcessAsync_FilterFaulted_WritesTheWholeRawNotJustTheTail()
    {
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Throws(new InvalidOperationException("boom"));
        await using var writer = new StringWriter();
        var sut = new FilteredOutputPipeline(_tracker, writer, _configProvider);
        var raw = string.Join('\n', Enumerable.Range(0, 60).Select(i => $"line {i}"));

        await sut.ProcessAsync(Request(raw: raw, exitCode: 1), NullTeeSession.Instance);

        var lines = writer.ToString().TrimEnd('\n').Split('\n');
        lines.Should().HaveCount(61);
        lines[0].Should().Be("line 0");
        lines[59].Should().Be("line 59");
        lines[^1].Should().Be("✗ dotnet build failed (exit 1)");
    }

    [Theory]
    [InlineData("a\n", "a\n⚠ dotnet build: output not recognized, raw tail shown above\n")]
    [InlineData("", "✓ dotnet build (no output)\n")]
    public async Task ProcessAsync_FilterFaultedOnSuccess_EndsWithTheFallbackStatusLine(string raw, string expected)
    {
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Throws(new InvalidOperationException("boom"));
        await using var writer = new StringWriter();
        var sut = new FilteredOutputPipeline(_tracker, writer, _configProvider);

        await sut.ProcessAsync(Request(raw: raw), NullTeeSession.Instance);

        writer.ToString().Should().Be(expected);
        await _tracker.Received(1).RecordAsync(
            Arg.Is<CommandRecord>(r => r.Outcome == RunOutcome.FilterFaulted),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_PrefersFilterFaulted_WhenBothConditionsOverlap()
    {
        // A faulted filter yields the raw text unchanged, so when that raw text is itself
        // whitespace on a failed command BOTH conditions hold. Faulted must win — this ordering
        // is deliberate and is the one rule in the pipeline that is not incidental.
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Throws(new InvalidOperationException("boom"));

        await _sut.ProcessAsync(Request(raw: "   ", exitCode: 1), NullTeeSession.Instance);

        await _tracker.Received(1).RecordAsync(
            Arg.Is<CommandRecord>(r => r.Outcome == RunOutcome.FilterFaulted),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_TrackingThrows_DoesNotSurfaceException()
    {
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");
        _tracker.RecordAsync(Arg.Any<CommandRecord>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("db error"));

        var act = async () => await _sut.ProcessAsync(Request(), NullTeeSession.Instance);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task ProcessAsync_TeeThrows_DoesNotSurfaceExceptionAndKeepsTheExitCode()
    {
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");
        var session = Substitute.For<ITeeSession>();
        session.Writer.Returns(TextWriter.Null);
        session.FinalizeAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("io error"));

        var exitCode = await _sut.ProcessAsync(Request(exitCode: 5), session);

        exitCode.Should().Be(5);
    }

    [Fact]
    public async Task ProcessAsync_TrackingDisabled_RecordsNothing()
    {
        _configProvider.LoadAsync(Arg.Any<CancellationToken>())
            .Returns(DtkConfig.Default with { Tracking = DtkConfig.Default.Tracking with { Enabled = false } });
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");

        await _sut.ProcessAsync(Request(), NullTeeSession.Instance);

        await _tracker.DidNotReceive().RecordAsync(Arg.Any<CommandRecord>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_NormalizesOptions_WhenCallerPassesUnnormalizedQuiet()
    {
        // A Task-5 caller could pass Quiet: true without also calling .Normalized(). The pipeline
        // must not trust the caller's Options as-is — it normalizes defensively so quiet mode is
        // enforced by construction, not by convention.
        await using var writer = new StringWriter();
        var sut = new FilteredOutputPipeline(_tracker, writer, _configProvider);
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");
        var request = new FilteredOutputRequest(
            _filter, "raw output", 0, "build", "dotnet build", RunSource.Run,
            new OutputOptions(VerbosityLevel: 2, ShowLogHint: true, Quiet: true),
            Stopwatch.GetTimestamp());

        await sut.ProcessAsync(request, NullTeeSession.Instance);

        writer.ToString().Should().NotContain("[raw output]");
    }

    [Fact]
    public async Task ProcessAsync_FinalizesBeforeBuildingTheFallback_SoTheHintAppearsInsideIt()
    {
        // Finalize must run before the raw-tail fallback is built, because the fallback embeds the
        // hint finalize returns. The other hint tests in this file take the normal (non-fallback)
        // branch — empty filter output on a non-zero exit code is what forces the fallback branch
        // instead — so only this test can pin the ordering: reordering the finalize call to run
        // after the fallback is built makes this test fail (verified manually) while leaving every
        // other test in the file green, since none of them observe a hint inside a fallback.
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("");
        var session = Substitute.For<ITeeSession>();
        session.Writer.Returns(TextWriter.Null);
        session.FinalizeAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns("[full output: /tee/x.log]");
        await using var writer = new StringWriter();
        var sut = new FilteredOutputPipeline(_tracker, writer, _configProvider);
        var request = new FilteredOutputRequest(
            _filter, "", 1, "build", "dotnet build", RunSource.Run,
            new OutputOptions(VerbosityLevel: 0, ShowLogHint: true, Quiet: false),
            Stopwatch.GetTimestamp());

        await sut.ProcessAsync(request, session);

        writer.ToString().Should().Contain("[full output: /tee/x.log]");
    }

    [Fact]
    public async Task ProcessAsync_FinalizesTheSessionWithTheRequestsExitCode()
    {
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");
        var session = Substitute.For<ITeeSession>();
        session.Writer.Returns(TextWriter.Null);
        session.FinalizeAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns("[full output: /tee/x.log]");

        var request = Request(exitCode: 1, source: RunSource.Pipe);
        await _sut.ProcessAsync(request, session);

        await session.Received(1).FinalizeAsync(request.ExitCode, Arg.Any<CancellationToken>());
    }

    private static DtkConfig TrackingOff =>
        DtkConfig.Default with { Tracking = DtkConfig.Default.Tracking with { Enabled = false } };

    [Fact]
    public async Task BeginAsync_TrackingDisabled_DoesNotWarmTheTracker()
    {
        _configProvider.LoadAsync(Arg.Any<CancellationToken>()).Returns(TrackingOff);

        var prepared = await _sut.BeginAsync();
        await prepared.WarmUp.WhenReadyAsync();

        await _tracker.DidNotReceive().WarmUpAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BeginAsync_TrackingEnabled_WarmsTheTrackerOnce()
    {
        var prepared = await _sut.BeginAsync();
        await prepared.WarmUp.WhenReadyAsync();

        await _tracker.Received(1).WarmUpAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_WithPreparedRun_DoesNotLoadConfigAgain()
    {
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");
        var prepared = await _sut.BeginAsync();
        _configProvider.ClearReceivedCalls();

        await _sut.ProcessAsync(Request(), NullTeeSession.Instance, prepared);

        await _configProvider.DidNotReceive().LoadAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_WithPreparedRun_TracksUnderThePreparedConfig()
    {
        // A run is tracked under the settings it started with, whatever the file says by the end.
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");
        _configProvider.LoadAsync(Arg.Any<CancellationToken>()).Returns(TrackingOff);
        var prepared = new PreparedRun(DtkConfig.Default, TrackingWarmUp.None);

        await _sut.ProcessAsync(Request(), NullTeeSession.Instance, prepared);

        await _tracker.Received(1).RecordAsync(Arg.Any<CommandRecord>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_TrackerWarmUpThrows_KeepsOutputAndExitCodeAndStillRecords()
    {
        _tracker.WarmUpAsync(Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("db locked"));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");
        await using var writer = new StringWriter();
        var sut = new FilteredOutputPipeline(_tracker, writer, _configProvider);
        var prepared = await sut.BeginAsync();

        var exitCode = await sut.ProcessAsync(Request(exitCode: 3), NullTeeSession.Instance, prepared);

        exitCode.Should().Be(3);
        writer.ToString().Should().Be("filtered");
        await _tracker.Received(1).RecordAsync(Arg.Any<CommandRecord>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_RecordsTheCountersTotalWhenPresent()
    {
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");
        var counter = new ChunkedTokenCounter(TokenizerModel.Cl100kBase);
        counter.Append("raw output\nsecond line\n");
        counter.Finish("stderr text\n");
        var request = Request(raw: "raw output\nsecond line\nstderr text\n") with { InputTokenCounter = counter };

        await _sut.ProcessAsync(request, NullTeeSession.Instance);

        var expected = TokenEstimator.Estimate("raw output\nsecond line\nstderr text\n");
        await _tracker.Received(1).RecordAsync(
            Arg.Is<CommandRecord>(r => r.InputTokens == expected), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_FaultedCounter_RecordsNothingAndKeepsOutputAndExitCode()
    {
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");
        var counter = new ChunkedTokenCounter(TokenizerModel.Cl100kBase, 64,
            static (_, _) => throw new InvalidOperationException("no vocabulary"));
        counter.Finish(string.Empty);
        var output = new StringWriter();
        var sut = new FilteredOutputPipeline(_tracker, output, _configProvider);
        var request = Request(exitCode: 3) with { InputTokenCounter = counter };

        var exitCode = await sut.ProcessAsync(request, NullTeeSession.Instance);

        exitCode.Should().Be(3);
        output.ToString().Should().Be("filtered");
        await _tracker.DidNotReceive().RecordAsync(Arg.Any<CommandRecord>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ProcessAsync_WaitsForTheTrackerWarmUpBeforeRecording()
    {
        // Loaded up front so a pipeline that skipped the wait would reach RecordAsync well inside
        // the delay below, instead of being held up by the vocabulary load and passing by accident.
        TokenEstimator.WarmUp();
        var warmUp = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _tracker.WarmUpAsync(Arg.Any<CancellationToken>()).Returns(warmUp.Task);
        var warmUpFinishedWhenRecorded = false;
        _tracker.RecordAsync(Arg.Any<CommandRecord>(), Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                warmUpFinishedWhenRecorded = warmUp.Task.IsCompleted;
                return Task.CompletedTask;
            });
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");
        var prepared = await _sut.BeginAsync();

        var processing = _sut.ProcessAsync(Request(), NullTeeSession.Instance, prepared);
        await Task.Delay(200);
        warmUp.SetResult();
        await processing;

        warmUpFinishedWhenRecorded.Should().BeTrue();
    }
}
