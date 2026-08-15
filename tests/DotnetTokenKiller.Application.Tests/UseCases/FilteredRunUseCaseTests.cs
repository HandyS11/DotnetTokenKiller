using DotnetTokenKiller.Application.Filters;
using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Domain.Configuration;
using DotnetTokenKiller.Domain.Execution;
using DotnetTokenKiller.Domain.Filters;
using DotnetTokenKiller.Domain.Tee;
using DotnetTokenKiller.Domain.Tracking;
using FluentAssertions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace DotnetTokenKiller.Application.Tests.UseCases;

public class FilteredRunUseCaseTests
{
    private static readonly string[] BuildArgs = ["build"];
    private static readonly string[] ListPackageArgs = ["list", "package"];
    private readonly IConfigProvider _configProvider = Substitute.For<IConfigProvider>();
    private readonly IOutputFilter _filter = Substitute.For<IOutputFilter>();

    private readonly ICommandRunner _runner = Substitute.For<ICommandRunner>();
    private readonly FilteredRunUseCase _sut;
    private readonly ITeeService _teeService = Substitute.For<ITeeService>();
    private readonly ITracker _tracker = Substitute.For<ITracker>();

    public FilteredRunUseCaseTests()
    {
        _configProvider.LoadAsync(Arg.Any<CancellationToken>()).Returns(DtkConfig.Default);
        var session = Substitute.For<ITeeSession>();
        session.Writer.Returns(TextWriter.Null);
        _teeService.BeginAsync(Arg.Any<string>(), Arg.Any<TeeLogHeader>(), Arg.Any<CancellationToken>())
            .Returns(session);
        var pipeline = new FilteredOutputPipeline(_tracker, TextWriter.Null, _configProvider);
        _sut = new FilteredRunUseCase(_runner, _teeService, pipeline, TextWriter.Null);
    }

    private static ITeeSession CreateSession(string? finalizeHint = null)
    {
        var session = Substitute.For<ITeeSession>();
        session.Writer.Returns(TextWriter.Null);
        session.FinalizeAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(finalizeHint);
        return session;
    }

    [Fact]
    public async Task RunAsync_OpensTheTeeBeforeRunningTheCommand()
    {
        // Begin must precede the run: a log opened after the child starts is not on disk when a
        // tool-call timeout kills dtk, which is the whole point of the session.
        var callOrder = new List<string>();
        var session = Substitute.For<ITeeSession>();
        session.Writer.Returns(TextWriter.Null);
        _teeService.BeginAsync(Arg.Any<string>(), Arg.Any<TeeLogHeader>(), Arg.Any<CancellationToken>())
            .Returns(_ => { callOrder.Add("begin"); return session; });
        _runner.RunStreamedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<TextWriter>(), Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(_ => { callOrder.Add("run"); return new CommandResult("out", "", 0); });
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");

        await _sut.RunAsync(_filter, "dotnet", BuildArgs, 0);

        callOrder.Should().Equal("begin", "run");
    }

    [Fact]
    public async Task RunAsync_OpensTheTeeWithARunningHeader()
    {
        var session = Substitute.For<ITeeSession>();
        session.Writer.Returns(TextWriter.Null);
        _teeService.BeginAsync(Arg.Any<string>(), Arg.Any<TeeLogHeader>(), Arg.Any<CancellationToken>())
            .Returns(session);
        _runner.RunStreamedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<TextWriter>(), Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("out", "", 0));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");

        await _sut.RunAsync(_filter, "dotnet", ListPackageArgs, 0);

        // Status is derived from ExitCode (see TeeLogHeader.Status), so asserting both would be
        // redundant; CommandLine and ProjectPath are asserted here because they are exactly what
        // `dtk log` filters on, and a wrong value there would make it silently return nothing with
        // no test noticing.
        await _teeService.Received(1).BeginAsync(
            "list package",
            Arg.Is<TeeLogHeader>(h =>
                h.ExitCode == null &&
                h.CommandLine == "dotnet list package" &&
                h.ProjectPath == Environment.CurrentDirectory),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_FinalizesTheSessionWithTheCommandExitCode()
    {
        var session = Substitute.For<ITeeSession>();
        session.Writer.Returns(TextWriter.Null);
        _teeService.BeginAsync(Arg.Any<string>(), Arg.Any<TeeLogHeader>(), Arg.Any<CancellationToken>())
            .Returns(session);
        _runner.RunStreamedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<TextWriter>(), Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("out", "", 7));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");

        await _sut.RunAsync(_filter, "dotnet", BuildArgs, 0);

        await session.Received(1).FinalizeAsync(7, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_DoesNotThrow_WhenFinalizingTheSessionFails()
    {
        var session = Substitute.For<ITeeSession>();
        session.Writer.Returns(TextWriter.Null);
        session.FinalizeAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new IOException("disk full"));
        _teeService.BeginAsync(Arg.Any<string>(), Arg.Any<TeeLogHeader>(), Arg.Any<CancellationToken>())
            .Returns(session);
        _runner.RunStreamedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<TextWriter>(), Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("out", "", 0));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");

        var act = async () => await _sut.RunAsync(_filter, "dotnet", BuildArgs, 0);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RunAsync_ReturnsExitCodeFromCommand()
    {
        _runner.RunStreamedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<TextWriter>(), Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("output", "", 42));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");

        var exitCode = await _sut.RunAsync(_filter, "dotnet", BuildArgs, 0);

        exitCode.Should().Be(42);
    }

    [Fact]
    public async Task RunAsync_FilterThrows_FallsBackToRawOutput_StillReturnsExitCode()
    {
        _runner.RunStreamedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<TextWriter>(), Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("raw output", "", 0));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Throws(new InvalidOperationException("boom"));

        var act = async () => await _sut.RunAsync(_filter, "dotnet", BuildArgs, 0);

        await act.Should().NotThrowAsync();
        var exitCode = await _sut.RunAsync(_filter, "dotnet", BuildArgs, 0);
        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task RunAsync_TrackingThrows_DoesNotSurfaceException()
    {
        _runner.RunStreamedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<TextWriter>(), Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("output", "", 0));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");
        _tracker.RecordAsync(Arg.Any<CommandRecord>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("db error"));

        var act = async () => await _sut.RunAsync(_filter, "dotnet", BuildArgs, 0);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RunAsync_TeeThrows_DoesNotSurfaceException()
    {
        _runner.RunStreamedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<TextWriter>(), Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("output", "", 1));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");
        var session = CreateSession();
        session.FinalizeAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("io error"));
        _teeService.BeginAsync(Arg.Any<string>(), Arg.Any<TeeLogHeader>(), Arg.Any<CancellationToken>())
            .Returns(session);

        var act = async () => await _sut.RunAsync(_filter, "dotnet", BuildArgs, 0);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RunAsync_CallsFilterWithCombinedStrippedOutput()
    {
        const string stdout = "\x1b[32mHello\x1b[0m\n";
        const string stderr = "\x1b[31mError\x1b[0m\n";
        _runner.RunStreamedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<TextWriter>(), Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult(stdout, stderr, 0));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("ok");

        await _sut.RunAsync(_filter, "dotnet", BuildArgs, 0);

        _filter.Received(1).Apply("Hello\nError\n", 0);
    }

    [Fact]
    public async Task RunAsync_RecordsCorrectTokenCounts_AfterSuccessfulExecution()
    {
        // tiktoken cl100k_base: "1234567890123456" = 6 tokens; "1234" = 2 tokens; saved = 4
        _runner.RunStreamedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<TextWriter>(), Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("1234567890123456", "", 0));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("1234");

        await _sut.RunAsync(_filter, "dotnet", BuildArgs, 0);

        await _tracker.Received(1).RecordAsync(
            Arg.Is<CommandRecord>(r =>
                r.Command == "build" &&
                r.InputTokens == 6 &&
                r.OutputTokens == 2 &&
                r.SavedTokens == 4),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_RecordsCorrectCommand_WhenArgsProvided()
    {
        _runner.RunStreamedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<TextWriter>(), Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("output", "", 0));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");

        await _sut.RunAsync(_filter, "dotnet", BuildArgs, 0);

        await _tracker.Received(1).RecordAsync(
            Arg.Is<CommandRecord>(r => r.Command == "build"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_RecordsCorrectCommand_WhenNoArgs_UsesCommandName()
    {
        _runner.RunStreamedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<TextWriter>(), Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("output", "", 0));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");

        await _sut.RunAsync(_filter, "dotnet", [], 0);

        await _tracker.Received(1).RecordAsync(
            Arg.Is<CommandRecord>(r => r.Command == "dotnet"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_RecordsNegativeSavedTokens_WhenFilterExpandsOutput()
    {
        // tiktoken cl100k_base: "1234" = 2 tokens; filter returns "1234567890123456" = 6 tokens; saved = -4
        _runner.RunStreamedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<TextWriter>(), Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("1234", "", 0));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("1234567890123456");

        await _sut.RunAsync(_filter, "dotnet", BuildArgs, 0);

        await _tracker.Received(1).RecordAsync(
            Arg.Is<CommandRecord>(r =>
                r.InputTokens == 2 &&
                r.OutputTokens == 6 &&
                r.SavedTokens == -4 &&
                r.SavingsPercentage < 0),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_WritesFilteredOutput_ToTextWriter()
    {
        await using var writer = new StringWriter();
        var configProvider = Substitute.For<IConfigProvider>();
        configProvider.LoadAsync(Arg.Any<CancellationToken>()).Returns(DtkConfig.Default);
        var sut = new FilteredRunUseCase(_runner, _teeService,
            new FilteredOutputPipeline(_tracker, writer, configProvider), writer);

        _runner.RunStreamedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<TextWriter>(), Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("raw output", "", 0));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("✓ dotnet build\n");

        await sut.RunAsync(_filter, "dotnet", BuildArgs, 0);

        writer.ToString().Should().Contain("✓ dotnet build");
    }

    [Fact]
    public async Task RunAsync_ReplacesEmoji_WhenDisplayEmojiDisabled()
    {
        await using var writer = new StringWriter();
        var configProvider = Substitute.For<IConfigProvider>();
        var config = DtkConfig.Default with
        {
            Display = new DisplayConfig(Emoji: false)
        };
        configProvider.LoadAsync(Arg.Any<CancellationToken>()).Returns(config);
        var sut = new FilteredRunUseCase(_runner, _teeService,
            new FilteredOutputPipeline(_tracker, writer, configProvider), writer);

        _runner.RunStreamedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<TextWriter>(), Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("raw output", "", 0));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("✓ dotnet build\n");

        await sut.RunAsync(_filter, "dotnet", BuildArgs, 0);

        var output = writer.ToString();
        output.Should().NotContain("✓");
        output.Should().Contain("ok: dotnet build");
    }

    [Fact]
    public async Task RunAsync_ShowLogHintFalse_DoesNotPrintHintLine()
    {
        await using var writer = new StringWriter();
        var configProvider = Substitute.For<IConfigProvider>();
        configProvider.LoadAsync(Arg.Any<CancellationToken>()).Returns(DtkConfig.Default);
        var session = CreateSession("[full output: 123_test.log]");
        _teeService.BeginAsync(Arg.Any<string>(), Arg.Any<TeeLogHeader>(), Arg.Any<CancellationToken>())
            .Returns(session);
        var sut = new FilteredRunUseCase(_runner, _teeService,
            new FilteredOutputPipeline(_tracker, writer, configProvider), writer);

        _runner.RunStreamedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<TextWriter>(), Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("raw output", "", 1));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered\n");

        await sut.RunAsync(_filter, "dotnet", BuildArgs, 0);

        writer.ToString().Should().NotContain("[full output:");
    }

    [Fact]
    public async Task RunAsync_ShowLogHintTrue_PrintsHintLine()
    {
        await using var writer = new StringWriter();
        var configProvider = Substitute.For<IConfigProvider>();
        configProvider.LoadAsync(Arg.Any<CancellationToken>()).Returns(DtkConfig.Default);
        var session = CreateSession("[full output: 123_test.log]");
        _teeService.BeginAsync(Arg.Any<string>(), Arg.Any<TeeLogHeader>(), Arg.Any<CancellationToken>())
            .Returns(session);
        var sut = new FilteredRunUseCase(_runner, _teeService,
            new FilteredOutputPipeline(_tracker, writer, configProvider), writer);

        _runner.RunStreamedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<TextWriter>(), Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("raw output", "", 1));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered\n");

        await sut.RunAsync(_filter, "dotnet", BuildArgs, 0, true);

        writer.ToString().Should().Contain("[full output: 123_test.log]");
    }

    [Fact]
    public async Task RunAsync_VerbosityLevel1_PrintsCommandLine()
    {
        await using var writer = new StringWriter();
        var configProvider = Substitute.For<IConfigProvider>();
        configProvider.LoadAsync(Arg.Any<CancellationToken>()).Returns(DtkConfig.Default);
        var sut = new FilteredRunUseCase(_runner, _teeService,
            new FilteredOutputPipeline(_tracker, writer, configProvider), writer);

        _runner.RunStreamedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<TextWriter>(), Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("output", "", 0));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");

        await sut.RunAsync(_filter, "dotnet", BuildArgs, 1);

        writer.ToString().Should().Contain("$ dotnet build");
    }

    [Fact]
    public async Task RunAsync_VerbosityLevel2_PrintsRawOutputAndElapsed()
    {
        await using var writer = new StringWriter();
        var configProvider = Substitute.For<IConfigProvider>();
        configProvider.LoadAsync(Arg.Any<CancellationToken>()).Returns(DtkConfig.Default);
        var sut = new FilteredRunUseCase(_runner, _teeService,
            new FilteredOutputPipeline(_tracker, writer, configProvider), writer);

        _runner.RunStreamedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<TextWriter>(), Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("raw output", "", 0));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");

        await sut.RunAsync(_filter, "dotnet", BuildArgs, 2);

        var output = writer.ToString();
        output.Should().Contain("[raw output]");
        output.Should().Contain("raw output");
        output.Should().Contain("[elapsed:");
    }

    [Fact]
    public async Task RunAsync_VerbosityLevel2_FilterThrows_PrintsFilterErrorMessage()
    {
        await using var writer = new StringWriter();
        var configProvider = Substitute.For<IConfigProvider>();
        configProvider.LoadAsync(Arg.Any<CancellationToken>()).Returns(DtkConfig.Default);
        var sut = new FilteredRunUseCase(_runner, _teeService,
            new FilteredOutputPipeline(_tracker, writer, configProvider), writer);

        _runner.RunStreamedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<TextWriter>(), Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("raw output", "", 0));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Throws(new InvalidOperationException("boom"));

        await sut.RunAsync(_filter, "dotnet", BuildArgs, 2);

        writer.ToString().Should().Contain("[filter error — using raw output]");
    }

    [Fact]
    public async Task RunAsync_NullFilter_ThrowsArgumentNullException()
    {
        // Kills statement mutation on ArgumentNullException.ThrowIfNull(filter) (line 41)
        var act = () => _sut.RunAsync(null!, "dotnet", BuildArgs, 0);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task RunAsync_NullArgs_ThrowsArgumentNullException()
    {
        // Kills statement mutation on ArgumentNullException.ThrowIfNull(args) (line 42)
        var act = () => _sut.RunAsync(_filter, "dotnet", null!, 0);

        await act.Should().ThrowAsync<ArgumentNullException>();
    }

    [Fact]
    public async Task RunAsync_QuietTrue_OverridesVerbosityLevel()
    {
        // Kills boolean mutation on quiet check (line 50) and verbosity overrides (lines 55, 58)
        await using var writer = new StringWriter();
        var configProvider = Substitute.For<IConfigProvider>();
        configProvider.LoadAsync(Arg.Any<CancellationToken>()).Returns(DtkConfig.Default);
        var sut = new FilteredRunUseCase(_runner, _teeService,
            new FilteredOutputPipeline(_tracker, writer, configProvider), writer);

        _runner.RunStreamedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<TextWriter>(), Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("raw output", "", 0));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered\n");

        // verbosityLevel=2 would normally print command line and raw output, but quiet overrides
        await sut.RunAsync(_filter, "dotnet", BuildArgs, 2, quiet: true);

        var output = writer.ToString();
        output.Should().NotContain("$ dotnet build");
        output.Should().NotContain("[raw output]");
    }

    [Fact]
    public async Task RunAsync_ZeroTokens_RecordsZeroPercentSavings()
    {
        // Kills boolean mutation on inputTokens > 0 check (line 146) and arithmetic mutations
        _runner.RunStreamedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<TextWriter>(), Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("", "", 0));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("");

        await _sut.RunAsync(_filter, "dotnet", BuildArgs, 0);

        await _tracker.Received(1).RecordAsync(
            Arg.Is<CommandRecord>(r =>
                r.InputTokens == 0 &&
                r.OutputTokens == 0 &&
                Math.Abs(r.SavingsPercentage) < 0.01),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_VerifiesSavingsPercentageCalculation()
    {
        // Kills arithmetic mutations: savedTokens / inputTokens * 100.0 (line 146)
        // "Hello world" = 2 tokens; "Hello" = 1 token; saved = 1; pct = 50%
        _runner.RunStreamedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<TextWriter>(), Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("Hello world", "", 0));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("Hello");

        await _sut.RunAsync(_filter, "dotnet", BuildArgs, 0);

        await _tracker.Received(1).RecordAsync(
            Arg.Is<CommandRecord>(r =>
                r.InputTokens == 2 &&
                r.OutputTokens == 1 &&
                r.SavedTokens == 1 &&
                Math.Abs(r.SavingsPercentage - 50.0) < 0.01),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_VerbosityLevel0_DoesNotPrintCommandLine()
    {
        // Kills boolean mutation on verbosityLevel >= 1 (line 55)
        await using var writer = new StringWriter();
        var configProvider = Substitute.For<IConfigProvider>();
        configProvider.LoadAsync(Arg.Any<CancellationToken>()).Returns(DtkConfig.Default);
        var sut = new FilteredRunUseCase(_runner, _teeService,
            new FilteredOutputPipeline(_tracker, writer, configProvider), writer);

        _runner.RunStreamedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<TextWriter>(), Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("output", "", 0));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");

        await sut.RunAsync(_filter, "dotnet", BuildArgs, 0);

        writer.ToString().Should().NotContain("$ dotnet");
    }

    [Fact]
    public async Task RunAsync_VerbosityLevel1_DoesNotPrintRawOutput()
    {
        // Kills boolean mutation on verbosityLevel >= 2 (line 86)
        await using var writer = new StringWriter();
        var configProvider = Substitute.For<IConfigProvider>();
        configProvider.LoadAsync(Arg.Any<CancellationToken>()).Returns(DtkConfig.Default);
        var sut = new FilteredRunUseCase(_runner, _teeService,
            new FilteredOutputPipeline(_tracker, writer, configProvider), writer);

        _runner.RunStreamedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<TextWriter>(), Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("raw stuff", "", 0));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");

        await sut.RunAsync(_filter, "dotnet", BuildArgs, 1);

        var output = writer.ToString();
        output.Should().Contain("$ dotnet build");
        output.Should().NotContain("[raw output]");
        output.Should().NotContain("[elapsed:");
    }

    [Fact]
    public async Task RunAsync_EmojiEnabled_KeepsCheckmark()
    {
        // Kills boolean mutation on !config.Display.Emoji (line 79)
        await using var writer = new StringWriter();
        var configProvider = Substitute.For<IConfigProvider>();
        configProvider.LoadAsync(Arg.Any<CancellationToken>()).Returns(DtkConfig.Default);
        var sut = new FilteredRunUseCase(_runner, _teeService,
            new FilteredOutputPipeline(_tracker, writer, configProvider), writer);

        _runner.RunStreamedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<TextWriter>(), Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("output", "", 0));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("✓ build ok\n");

        await sut.RunAsync(_filter, "dotnet", BuildArgs, 0);

        writer.ToString().Should().Contain("✓");
        writer.ToString().Should().NotContain("ok:");
    }

    [Fact]
    public async Task RunAsync_TeeHintNull_DoesNotPrintHint()
    {
        // Kills boolean mutation on hint is not null (line 116)
        await using var writer = new StringWriter();
        var configProvider = Substitute.For<IConfigProvider>();
        configProvider.LoadAsync(Arg.Any<CancellationToken>()).Returns(DtkConfig.Default);
        // NSubstitute defaults an unconfigured string-returning member to "", not null, so the
        // session's FinalizeAsync must be stubbed explicitly to exercise the null-hint branch.
        var session = CreateSession(finalizeHint: null);
        _teeService.BeginAsync(Arg.Any<string>(), Arg.Any<TeeLogHeader>(), Arg.Any<CancellationToken>())
            .Returns(session);
        var sut = new FilteredRunUseCase(_runner, _teeService,
            new FilteredOutputPipeline(_tracker, writer, configProvider), writer);

        _runner.RunStreamedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<TextWriter>(), Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("output", "", 0));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered\n");

        await sut.RunAsync(_filter, "dotnet", BuildArgs, 0, true);

        writer.ToString().Should().Be("filtered\n");
    }

    [Fact]
    public async Task RunAsync_EmptyRawOutput_RecordsZeroSavingsPct()
    {
        // Covers inputTokens == 0 → savingsPct = 0.0 branch (line 126)
        _runner.RunStreamedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<TextWriter>(), Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("", "", 0));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("");

        await _sut.RunAsync(_filter, "dotnet", BuildArgs, 0);

        await _tracker.Received(1).RecordAsync(
            Arg.Is<CommandRecord>(r => Math.Abs(r.SavingsPercentage) < 0.001),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_QuietMode_SuppressesVerbosityOutput()
    {
        await using var writer = new StringWriter();
        var configProvider = Substitute.For<IConfigProvider>();
        configProvider.LoadAsync(Arg.Any<CancellationToken>()).Returns(DtkConfig.Default);
        var sut = new FilteredRunUseCase(_runner, _teeService,
            new FilteredOutputPipeline(_tracker, writer, configProvider), writer);

        _runner.RunStreamedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<TextWriter>(), Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("output", "", 0));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");

        // verbosityLevel=2 would normally print meta lines, but quiet overrides it
        await sut.RunAsync(_filter, "dotnet", BuildArgs, 2, quiet: true);

        var result = writer.ToString();
        result.Should().NotContain("$ dotnet");
        result.Should().NotContain("[raw output]");
        result.Should().NotContain("[elapsed:");
    }

    [Fact]
    public async Task RunAsync_QuietMode_SuppressesLogHint()
    {
        await using var writer = new StringWriter();
        var configProvider = Substitute.For<IConfigProvider>();
        configProvider.LoadAsync(Arg.Any<CancellationToken>()).Returns(DtkConfig.Default);
        var session = CreateSession("[full output: 123_test.log]");
        _teeService.BeginAsync(Arg.Any<string>(), Arg.Any<TeeLogHeader>(), Arg.Any<CancellationToken>())
            .Returns(session);
        var sut = new FilteredRunUseCase(_runner, _teeService,
            new FilteredOutputPipeline(_tracker, writer, configProvider), writer);

        _runner.RunStreamedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<TextWriter>(), Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("output", "", 1));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered\n");

        // showLogHint=true but quiet overrides it
        await sut.RunAsync(_filter, "dotnet", BuildArgs, 0, true, true);

        writer.ToString().Should().NotContain("[full output:");
    }

    [Fact]
    public async Task RunAsync_QuietMode_StillWritesFilteredContent()
    {
        await using var writer = new StringWriter();
        var configProvider = Substitute.For<IConfigProvider>();
        configProvider.LoadAsync(Arg.Any<CancellationToken>()).Returns(DtkConfig.Default);
        var sut = new FilteredRunUseCase(_runner, _teeService,
            new FilteredOutputPipeline(_tracker, writer, configProvider), writer);

        _runner.RunStreamedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<TextWriter>(), Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("output", "", 0));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered result\n");

        await sut.RunAsync(_filter, "dotnet", BuildArgs, 0, quiet: true);

        writer.ToString().Should().Contain("filtered result");
    }

    [Fact]
    public async Task RunAsync_SkipsTracking_WhenTrackingDisabled()
    {
        var configProvider = Substitute.For<IConfigProvider>();
        var config = DtkConfig.Default with
        {
            Tracking = new TrackingConfig(false)
        };
        configProvider.LoadAsync(Arg.Any<CancellationToken>()).Returns(config);
        var sut = new FilteredRunUseCase(_runner, _teeService,
            new FilteredOutputPipeline(_tracker, TextWriter.Null, configProvider), TextWriter.Null);

        _runner.RunStreamedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<TextWriter>(), Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("output", "", 0));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");

        await sut.RunAsync(_filter, "dotnet", BuildArgs, 0);

        await _tracker.DidNotReceive().RecordAsync(
            Arg.Any<CommandRecord>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_FilterThrows_Verbosity0_NoFilterErrorMessage()
    {
        // Kills boolean mutation on verbosity check inside the filter catch block (line 73)
        await using var writer = new StringWriter();
        var configProvider = Substitute.For<IConfigProvider>();
        configProvider.LoadAsync(Arg.Any<CancellationToken>()).Returns(DtkConfig.Default);
        var sut = new FilteredRunUseCase(_runner, _teeService,
            new FilteredOutputPipeline(_tracker, writer, configProvider), writer);

        _runner.RunStreamedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<TextWriter>(), Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("raw output", "", 0));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Throws(new InvalidOperationException("boom"));

        await sut.RunAsync(_filter, "dotnet", BuildArgs, 0);

        writer.ToString().Should().NotContain("[filter error");
    }

    [Fact]
    public async Task RunAsync_FilterThrows_OutputContainsRawStrippedContent()
    {
        // Kills statement mutation on filtered = stripped fallback (line 76)
        await using var writer = new StringWriter();
        var configProvider = Substitute.For<IConfigProvider>();
        configProvider.LoadAsync(Arg.Any<CancellationToken>()).Returns(DtkConfig.Default);
        var sut = new FilteredRunUseCase(_runner, _teeService,
            new FilteredOutputPipeline(_tracker, writer, configProvider), writer);

        _runner.RunStreamedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<TextWriter>(), Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("\x1b[32mraw fallback\x1b[0m", "", 0));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Throws(new InvalidOperationException("boom"));

        await sut.RunAsync(_filter, "dotnet", BuildArgs, 0);

        writer.ToString().Should().Contain("raw fallback");
    }

    [Fact]
    public async Task RunAsync_TeeCalledWithCorrectSlugAndExitCode()
    {
        // Kills statement mutations on tee invocation (line 97-98)
        var session = CreateSession();
        _teeService.BeginAsync(Arg.Any<string>(), Arg.Any<TeeLogHeader>(), Arg.Any<CancellationToken>())
            .Returns(session);
        _runner.RunStreamedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<TextWriter>(), Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("output", "", 7));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");

        await _sut.RunAsync(_filter, "dotnet", BuildArgs, 0);

        await _teeService.Received(1).BeginAsync(
            "build",
            Arg.Any<TeeLogHeader>(),
            Arg.Any<CancellationToken>());
        await session.Received(1).FinalizeAsync(7, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_TrackingEnabled_RecordAsyncCalled()
    {
        // Kills boolean mutation on config.Tracking.Enabled (line 138)
        _runner.RunStreamedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<TextWriter>(), Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("output", "", 0));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");

        await _sut.RunAsync(_filter, "dotnet", BuildArgs, 0);

        await _tracker.Received(1).RecordAsync(
            Arg.Any<CommandRecord>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_ZeroExitCode_RecordsSuccessTrue()
    {
        // Kills equality mutation: exitCode == 0 → exitCode != 0
        _runner.RunStreamedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<TextWriter>(), Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("output", "", 0));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");

        await _sut.RunAsync(_filter, "dotnet", BuildArgs, 0);

        await _tracker.Received(1).RecordAsync(
            Arg.Is<CommandRecord>(r => r.Success),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_NonZeroExitCode_RecordsSuccessFalse()
    {
        // Kills equality mutation: exitCode == 0 → exitCode != 0
        _runner.RunStreamedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<TextWriter>(), Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("output", "", 1));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");

        await _sut.RunAsync(_filter, "dotnet", BuildArgs, 0);

        await _tracker.Received(1).RecordAsync(
            Arg.Is<CommandRecord>(r => !r.Success),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_FailedCommandWithUnparsedOutput_EmitsRawTailAsync()
    {
        // Arrange a fake ICommandRunner returning ExitCode=1 and output the filter won't match,
        // e.g. "MSBUILD : error MSB1009: Project file does not exist."
        var output = await RunUseCaseAsync(exitCode: 1,
            rawOutput: "MSBUILD : error MSB1009: Project file does not exist.");

        output.Should().NotBeNullOrWhiteSpace(); // the old behavior returned ""
        output.Should().Contain("MSB1009"); // the raw tail must surface the reason
        output.Should().Contain("exit 1");
    }

    [Fact]
    public async Task RunAsync_FailedCommandWithCrlfOutput_RawTailHasNoStrayCarriageReturnsAsync()
    {
        // On Windows the captured output uses CRLF. Splitting the raw tail on '\n' alone would leave
        // a trailing '\r' glued to each content line (the other filters guard against this with
        // TrimEnd('\r')). We can't assert the whole output is '\r'-free: StringBuilder.AppendLine emits
        // Environment.NewLine, which is "\r\n" on Windows — so instead we assert the platform-independent
        // guarantee that no content line carries a stray carriage return.
        var output = await RunUseCaseAsync(exitCode: 1,
            rawOutput: "MSBUILD : error MSB1009: alpha line\r\nbeta line\r\n");

        output.Should().Contain("MSB1009");
        output.Should().NotContain("alpha line\r"); // stray CR would survive without the tail TrimEnd
        output.Should().NotContain("\r\r"); // and no doubled CR where a tail line meets AppendLine's newline
    }

    private async Task<string> RunUseCaseAsync(int exitCode, string rawOutput)
    {
        await using var writer = new StringWriter();
        var sut = new FilteredRunUseCase(_runner, _teeService,
            new FilteredOutputPipeline(_tracker, writer, _configProvider), writer);

        _runner.RunStreamedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<TextWriter>(), Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult(rawOutput, "", exitCode));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns(string.Empty);

        await sut.RunAsync(_filter, "dotnet", BuildArgs, 0);

        return writer.ToString();
    }

    [Fact]
    public async Task RunAsync_FailedRunUnparsedOutputEmojiDisabled_ReplacesFailureMarkerWithFail()
    {
        // The raw-tail fallback (triggered by a failed run with unparseable/empty filter output)
        // prepends a "✗ ... failed (exit N)" marker. When emoji is disabled, that marker must be
        // substituted just like "✓" is, so no raw "✗" glyph escapes to the terminal.
        await using var writer = new StringWriter();
        var configProvider = Substitute.For<IConfigProvider>();
        var config = DtkConfig.Default with
        {
            Display = new DisplayConfig(Emoji: false)
        };
        configProvider.LoadAsync(Arg.Any<CancellationToken>()).Returns(config);
        var sut = new FilteredRunUseCase(_runner, _teeService,
            new FilteredOutputPipeline(_tracker, writer, configProvider), writer);

        _runner.RunStreamedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<TextWriter>(), Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("MSBUILD : error MSB1009: Project file does not exist.", "", 1));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns(string.Empty);

        await sut.RunAsync(_filter, "dotnet", BuildArgs, 0);

        var output = writer.ToString();
        output.Should().Contain("FAIL:");
        output.Should().NotContain("✗");
    }

    [Fact]
    public async Task RunAsync_NoColorEnvVar_ReplacesCheckmarkWithOk()
    {
        // Kills string mutation: "NO_COLOR" → ""
        var saved = Environment.GetEnvironmentVariable("NO_COLOR");
        Environment.SetEnvironmentVariable("NO_COLOR", "1");
        try
        {
            await using var writer = new StringWriter();
            var configProvider = Substitute.For<IConfigProvider>();
            configProvider.LoadAsync(Arg.Any<CancellationToken>()).Returns(DtkConfig.Default);
            var sut = new FilteredRunUseCase(_runner, _teeService,
                new FilteredOutputPipeline(_tracker, writer, configProvider), writer);

            _runner.RunStreamedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                    Arg.Any<TextWriter>(), Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
                .Returns(new CommandResult("output", "", 0));
            _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("✓ build ok\n");

            await sut.RunAsync(_filter, "dotnet", BuildArgs, 0);

            var output = writer.ToString();
            output.Should().NotContain("✓");
            output.Should().Contain("ok: build ok");
        }
        finally
        {
            Environment.SetEnvironmentVariable("NO_COLOR", saved);
        }
    }

    [Fact]
    public async Task RunAsync_ReplacesWarnGlyph_WhenDisplayEmojiDisabled()
    {
        await using var writer = new StringWriter();
        var configProvider = Substitute.For<IConfigProvider>();
        var config = DtkConfig.Default with
        {
            Display = new DisplayConfig(Emoji: false)
        };
        configProvider.LoadAsync(Arg.Any<CancellationToken>()).Returns(config);
        var sut = new FilteredRunUseCase(_runner, _teeService,
            new FilteredOutputPipeline(_tracker, writer, configProvider), writer);

        _runner.RunStreamedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<TextWriter>(), Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("raw output", "", 0));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("⚠ dotnet test: 0 tests found\n");

        await sut.RunAsync(_filter, "dotnet", BuildArgs, 0);

        var output = writer.ToString();
        output.Should().NotContain("⚠");
        output.Should().Contain("WARN: dotnet test: 0 tests found");
    }

    [Fact]
    public async Task RunAsync_NoColorEnvVar_ReplacesWarnGlyphWithWarn()
    {
        // Kills the string mutation "⚠" → "" on the NO_COLOR branch.
        var saved = Environment.GetEnvironmentVariable("NO_COLOR");
        Environment.SetEnvironmentVariable("NO_COLOR", "1");
        try
        {
            await using var writer = new StringWriter();
            var configProvider = Substitute.For<IConfigProvider>();
            configProvider.LoadAsync(Arg.Any<CancellationToken>()).Returns(DtkConfig.Default);
            var sut = new FilteredRunUseCase(_runner, _teeService,
                new FilteredOutputPipeline(_tracker, writer, configProvider), writer);

            _runner.RunStreamedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                    Arg.Any<TextWriter>(), Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
                .Returns(new CommandResult("output", "", 0));
            _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("⚠ nothing matched\n");

            await sut.RunAsync(_filter, "dotnet", BuildArgs, 0);

            var output = writer.ToString();
            output.Should().NotContain("⚠");
            output.Should().Contain("WARN: nothing matched");
        }
        finally
        {
            Environment.SetEnvironmentVariable("NO_COLOR", saved);
        }
    }

    [Fact]
    public async Task RunAsync_RecordsFiltered_WhenTheFilterSucceeds()
    {
        _runner.RunStreamedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<TextWriter>(), Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("raw output", "", 0));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");

        await _sut.RunAsync(_filter, "dotnet", BuildArgs, 0);

        await _tracker.Received(1).RecordAsync(
            Arg.Is<CommandRecord>(r => r.Outcome == RunOutcome.Filtered),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_RecordsFilterFaulted_WhenTheFilterThrows()
    {
        _runner.RunStreamedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<TextWriter>(), Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("raw output", "", 0));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Throws(new InvalidOperationException("boom"));

        await _sut.RunAsync(_filter, "dotnet", BuildArgs, 0);

        await _tracker.Received(1).RecordAsync(
            Arg.Is<CommandRecord>(r => r.Outcome == RunOutcome.FilterFaulted),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_RecordsRawTailFallback_WhenAFailedCommandFiltersToNothing()
    {
        _runner.RunStreamedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<TextWriter>(), Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("line one\nline two\n", "", 1));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("   ");

        await _sut.RunAsync(_filter, "dotnet", BuildArgs, 0);

        await _tracker.Received(1).RecordAsync(
            Arg.Is<CommandRecord>(r => r.Outcome == RunOutcome.RawTailFallback),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_PrefersFilterFaulted_WhenTheFilterThrowsOnAFailedCommand()
    {
        // A throwing filter yields the raw text unchanged. Here the raw text is non-empty, so the
        // raw-tail branch does not also fire — this test alone would still pass even if the switch
        // arms below were reordered. RunAsync_PrefersFilterFaulted_WhenBothConditionsOverlap is the
        // one that requires the ordering.
        _runner.RunStreamedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<TextWriter>(), Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("some raw output", "", 1));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Throws(new InvalidOperationException("boom"));

        await _sut.RunAsync(_filter, "dotnet", BuildArgs, 0);

        await _tracker.Received(1).RecordAsync(
            Arg.Is<CommandRecord>(r => r.Outcome == RunOutcome.FilterFaulted),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_PrefersFilterFaulted_WhenBothConditionsOverlap()
    {
        // Empty raw output plus a non-zero exit code plus a throwing filter makes filterFaulted and
        // usedRawTailFallback both true at once (a faulted filter yields the raw text unchanged, and
        // that raw text is itself empty/whitespace here, so the raw-tail branch's own condition is
        // also satisfied). This is the only case that exercises the switch's ordering: reordering
        // the arms so (false, true) is matched before (true, _) would flip this outcome to
        // RawTailFallback while leaving every other test in this file green.
        _runner.RunStreamedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<TextWriter>(), Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("", "", 1));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Throws(new InvalidOperationException("boom"));

        await _sut.RunAsync(_filter, "dotnet", BuildArgs, 0);

        await _tracker.Received(1).RecordAsync(
            Arg.Is<CommandRecord>(r => r.Outcome == RunOutcome.FilterFaulted),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_MultiTokenSubcommand_TracksTheFullCanonicalName()
    {
        // args[0] alone would record "list", which would never line up with the "list package" rows
        // the coverage report already holds from the passthrough path — making the before/after
        // savings comparison compare two different keys.
        _runner.RunStreamedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(),
                Arg.Any<TextWriter>(), Arg.Any<TextWriter>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("Project 'A' has the following package references", "", 0));

        await _sut.RunAsync(new DotnetListPackageFilter(), "dotnet", ListPackageArgs, 0);

        await _tracker.Received(1).RecordAsync(
            Arg.Is<CommandRecord>(r => r.Command == "list package"),
            Arg.Any<CancellationToken>());
    }
}
