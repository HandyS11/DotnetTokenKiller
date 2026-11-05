using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Domain.Configuration;
using DotnetTokenKiller.Domain.Execution;
using DotnetTokenKiller.Domain.Filters;
using DotnetTokenKiller.Domain.Tee;
using DotnetTokenKiller.Domain.Tracking;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using Xunit;

namespace DotnetTokenKiller.Application.Tests.UseCases;

public class PipeFilterUseCaseTests
{
    private readonly IConfigProvider _configProvider = Substitute.For<IConfigProvider>();
    private readonly IOutputFilter _filter = Substitute.For<IOutputFilter>();
    private readonly IRawOutputCopyFactory _rawOutputCopyFactory = Substitute.For<IRawOutputCopyFactory>();
    private readonly ITeeService _teeService = Substitute.For<ITeeService>();
    private readonly ITracker _tracker = Substitute.For<ITracker>();

    public PipeFilterUseCaseTests()
    {
        _configProvider.LoadAsync(Arg.Any<CancellationToken>()).Returns(DtkConfig.Default);
        var session = Substitute.For<ITeeSession>();
        session.Writer.Returns(TextWriter.Null);
        _teeService.BeginAsync(Arg.Any<string>(), Arg.Any<TeeLogHeader>(), Arg.Any<CancellationToken>())
            .Returns(session);
    }

    private PipeFilterUseCase Create(string stdin, TextWriter? output = null) =>
        Create(new StringReader(stdin), output);

    private PipeFilterUseCase Create(TextReader stdin, TextWriter? output = null)
    {
        var pipeline = new FilteredOutputPipeline(_tracker, output ?? TextWriter.Null, _configProvider);
        return new PipeFilterUseCase(pipeline, _teeService, _rawOutputCopyFactory, stdin);
    }

    [Fact]
    public async Task RunAsync_PassesStdinToTheFilter()
    {
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");

        await Create("piped input").RunAsync(_filter, "build", 0, new OutputOptions());

        _filter.Received(1).Apply("piped input", 0);
    }

    [Fact]
    public async Task RunAsync_PassesExitCodeToTheFilterAndReturnsIt()
    {
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");

        var exitCode = await Create("boom").RunAsync(_filter, "build", 1, new OutputOptions());

        _filter.Received(1).Apply("boom", 1);
        exitCode.Should().Be(1);
    }

    [Fact]
    public async Task RunAsync_RecordsPipeSourceUnderTheCanonicalSlug()
    {
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");

        await Create("raw").RunAsync(_filter, "list package", 0, new OutputOptions());

        await _tracker.Received(1).RecordAsync(
            Arg.Is<CommandRecord>(r => r.Source == RunSource.Pipe && r.Command == "list package"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_WritesFilteredOutput()
    {
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("condensed");
        var output = new StringWriter();

        await Create("raw", output).RunAsync(_filter, "build", 0, new OutputOptions());

        output.ToString().Should().Be("condensed");
    }

    [Fact]
    public async Task RunAsync_EmptyStdinOnFailure_EmitsRawTailFallbackHeader()
    {
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("");
        var output = new StringWriter();

        await Create("", output).RunAsync(_filter, "build", 7, new OutputOptions());

        output.ToString().Should().Contain("dotnet build failed (exit 7)");
    }

    [Fact]
    public async Task RunAsync_OpensATeeSessionUnderTheCanonicalSlug()
    {
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");

        await Create("raw").RunAsync(_filter, "list package", 0, new OutputOptions());

        // CommandLine and ProjectPath are asserted here because they are exactly what `dtk log`
        // filters on, and a wrong value there would make it silently return nothing with no test
        // noticing.
        await _teeService.Received(1).BeginAsync(
            "list package",
            Arg.Is<TeeLogHeader>(h =>
                h.ExitCode == null &&
                h.Source == RunSource.Pipe &&
                h.CommandLine == "dotnet list package" &&
                h.ProjectPath == Environment.CurrentDirectory),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_ReadsInputLargerThanOneBlockUnchanged()
    {
        var big = string.Concat(Enumerable.Repeat("a line of piped output\n", 20_000));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");

        await Create(big).RunAsync(_filter, "build", 0, new OutputOptions());

        _filter.Received(1).Apply(big, 0);
    }

    [Fact]
    public async Task RunAsync_TrackingOn_RecordsTheExactCount()
    {
        const string stdin = "raw\nmore raw\n";
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");

        await Create(stdin).RunAsync(_filter, "build", 0, new OutputOptions());

        var expected = TokenEstimator.Estimate(stdin);
        await _tracker.Received(1).RecordAsync(
            Arg.Is<CommandRecord>(r => r.InputTokens == expected), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_TrackingOff_FiltersTheWholeInputWithoutRecordingOrWarmingUp()
    {
        _configProvider.LoadAsync(Arg.Any<CancellationToken>())
            .Returns(DtkConfig.Default with { Tracking = DtkConfig.Default.Tracking with { Enabled = false } });
        var big = string.Concat(Enumerable.Repeat("a line of piped output\n", 20_000));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");

        await Create(big).RunAsync(_filter, "build", 0, new OutputOptions());

        _filter.Received(1).Apply(big, 0);
        await _tracker.DidNotReceiveWithAnyArgs().WarmUpAsync(default);
        await _tracker.DidNotReceiveWithAnyArgs().RecordAsync(default!, default);
    }

    [Fact]
    public async Task RunAsync_WithTeePath_CopiesStdin()
    {
        var copy = new RecordingRawOutputCopy();
        _rawOutputCopyFactory.Open("raw.log").Returns(copy);
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("condensed\n");
        var output = new StringWriter();

        await Create("line 1\nline 2", output)
            .RunAsync(_filter, "build", 0, new OutputOptions(TeePath: "raw.log"));

        copy.Text.Should().Be("line 1\nline 2\n");
        copy.DisposeCount.Should().BeGreaterThan(0);
        output.ToString().Should().Be("condensed\n");
    }

    [Fact]
    public async Task RunAsync_WithTeePath_ReportsAFailureToWriteTheCopyBeforeTheStatusLine()
    {
        _rawOutputCopyFactory.Open("raw.log")
            .Returns(new RecordingRawOutputCopy { FailureOnDispose = "No space left on device" });
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("✓ dotnet build\n");
        var output = new StringWriter();

        await Create("raw", output).RunAsync(_filter, "build", 0, new OutputOptions(TeePath: "raw.log"));

        output.ToString().Should().Be("⚠ --tee: could not write raw.log: No space left on device\n✓ dotnet build\n");
    }

    [Fact]
    public async Task RunAsync_WithUncreatableTeePath_ReturnsUsageErrorWithoutReadingStdin()
    {
        _rawOutputCopyFactory.Open("missing/raw.log")
            .Throws(new RawOutputCopyException("missing/raw.log: Could not find a part of the path."));
        var stdin = new StringReader("piped");
        var output = new StringWriter();

        var exitCode = await Create(stdin, output)
            .RunAsync(_filter, "build", 0, new OutputOptions(TeePath: "missing/raw.log"));

        exitCode.Should().Be(ExitCodes.UsageError);
        output.ToString().Should().Be(
            "✗ dtk: --tee cannot create missing/raw.log: Could not find a part of the path." + Environment.NewLine);
        stdin.Peek().Should().Be('p', "stdin is left unread");
        _filter.DidNotReceive().Apply(Arg.Any<string>(), Arg.Any<int>());
        await _teeService.DidNotReceive().BeginAsync(
            Arg.Any<string>(), Arg.Any<TeeLogHeader>(), Arg.Any<CancellationToken>());
        await _tracker.DidNotReceiveWithAnyArgs().RecordAsync(default!, default);
    }

    [Fact]
    public async Task RunAsync_StartsTrackingSetupBeforeStdinIsRead()
    {
        // Setup overlaps a slow producer on the other end of the pipe. The reader waits (bounded)
        // for the warm-up to start, so a use case that starts it only after reading fails here.
        var warmUpStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _tracker.WarmUpAsync(Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                warmUpStarted.TrySetResult();
                return Task.CompletedTask;
            });
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");
        var reader = new WarmUpAwareReader(warmUpStarted.Task, "piped input");
        var pipeline = new FilteredOutputPipeline(_tracker, TextWriter.Null, _configProvider);

        await new PipeFilterUseCase(pipeline, _teeService, _rawOutputCopyFactory, reader)
            .RunAsync(_filter, "build", 0, new OutputOptions());

        reader.WarmUpStartedBeforeRead.Should().BeTrue();
        await _tracker.Received(1).WarmUpAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_CancelledOnceStdinIsRead_StillWritesTheOutputTheLogAndTheRecord()
    {
        // As in a wrapped run once the child exits: the input is complete, so a late cancellation must not drop
        // the filtered output, the log or the record of a run that is already over.
        using var cts = new CancellationTokenSource();
        var session = Substitute.For<ITeeSession>();
        session.Writer.Returns(new StringWriter());
        _teeService.BeginAsync(Arg.Any<string>(), Arg.Any<TeeLogHeader>(), Arg.Any<CancellationToken>())
            .Returns(session);
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("condensed");
        var output = new StringWriter();

        var exitCode = await Create(new CancelAtEndReader("raw", cts), output)
            .RunAsync(_filter, "build", 0, new OutputOptions(), cancellationToken: cts.Token);

        exitCode.Should().Be(0);
        output.ToString().Should().Be("condensed");
        await session.Received(1).FinalizeAsync(0, Arg.Any<CancellationToken>());
        await _tracker.Received(1).RecordAsync(Arg.Any<CommandRecord>(), Arg.Any<CancellationToken>());
    }

    /// <summary>A stdin stand-in that cancels the run once it has handed over all of its content.</summary>
    /// <param name="content">The content to return from the first read.</param>
    /// <param name="cts">Cancelled when the reader reports the end of its input.</param>
    private sealed class CancelAtEndReader(string content, CancellationTokenSource cts) : TextReader
    {
        private bool _consumed;

        public override async ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default)
        {
            if (_consumed)
            {
                await cts.CancelAsync();
                return 0;
            }

            _consumed = true;
            content.AsSpan().CopyTo(buffer.Span);
            return content.Length;
        }
    }

    /// <summary>A stdin stand-in that records whether tracking setup had started when it was read.</summary>
    /// <param name="warmUpStarted">Completes once tracking setup has started.</param>
    /// <param name="content">The content to return from the first <see cref="ReadAsync(Memory{char}, CancellationToken)"/> call.</param>
    private sealed class WarmUpAwareReader(Task warmUpStarted, string content) : TextReader
    {
        private bool _consumed;

        public bool WarmUpStartedBeforeRead { get; private set; }

        /// <summary>
        /// Intercepts the block read <c>PipeFilterUseCase.ReadAllAsync</c> uses (not
        /// <see cref="ReadToEndAsync(CancellationToken)"/>), so this is the call to observe the ordering on.
        /// </summary>
        /// <param name="buffer">Filled with the reader's content on the first call.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        public override async ValueTask<int> ReadAsync(Memory<char> buffer, CancellationToken cancellationToken = default)
        {
            if (_consumed)
            {
                return 0;
            }

            _consumed = true;

            // Captured into a local: VSTHRD003 flags awaiting a Task read directly from a field
            // (which is what a primary-constructor parameter becomes once captured).
            var pending = warmUpStarted;
            var first = await Task.WhenAny(pending, Task.Delay(TimeSpan.FromSeconds(5), cancellationToken));
            WarmUpStartedBeforeRead = first == pending;
            content.AsSpan().CopyTo(buffer.Span);
            return content.Length;
        }
    }
}
