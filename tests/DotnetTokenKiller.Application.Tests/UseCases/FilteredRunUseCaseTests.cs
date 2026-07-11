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
    private readonly IConfigProvider _configProvider = Substitute.For<IConfigProvider>();
    private readonly IOutputFilter _filter = Substitute.For<IOutputFilter>();

    private readonly ICommandRunner _runner = Substitute.For<ICommandRunner>();
    private readonly FilteredRunUseCase _sut;
    private readonly ITeeService _teeService = Substitute.For<ITeeService>();
    private readonly ITracker _tracker = Substitute.For<ITracker>();

    public FilteredRunUseCaseTests()
    {
        _configProvider.LoadAsync(Arg.Any<CancellationToken>()).Returns(DtkConfig.Default);
        _sut = new FilteredRunUseCase(_runner, _tracker, _teeService, TextWriter.Null, _configProvider);
    }

    [Fact]
    public async Task RunAsync_ReturnsExitCodeFromCommand()
    {
        _runner.RunCapturedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("output", "", 42));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");
        _teeService.TeeAndHintAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        var exitCode = await _sut.RunAsync(_filter, "dotnet", BuildArgs, 0);

        exitCode.Should().Be(42);
    }

    [Fact]
    public async Task RunAsync_FilterThrows_FallsBackToRawOutput_StillReturnsExitCode()
    {
        _runner.RunCapturedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("raw output", "", 0));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Throws(new InvalidOperationException("boom"));
        _teeService.TeeAndHintAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        var act = async () => await _sut.RunAsync(_filter, "dotnet", BuildArgs, 0);

        await act.Should().NotThrowAsync();
        var exitCode = await _sut.RunAsync(_filter, "dotnet", BuildArgs, 0);
        exitCode.Should().Be(0);
    }

    [Fact]
    public async Task RunAsync_TrackingThrows_DoesNotSurfaceException()
    {
        _runner.RunCapturedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("output", "", 0));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");
        _tracker.RecordAsync(Arg.Any<CommandRecord>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("db error"));
        _teeService.TeeAndHintAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        var act = async () => await _sut.RunAsync(_filter, "dotnet", BuildArgs, 0);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RunAsync_TeeThrows_DoesNotSurfaceException()
    {
        _runner.RunCapturedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("output", "", 1));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");
        _teeService.TeeAndHintAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("io error"));

        var act = async () => await _sut.RunAsync(_filter, "dotnet", BuildArgs, 0);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RunAsync_CallsFilterWithCombinedStrippedOutput()
    {
        const string stdout = "\x1b[32mHello\x1b[0m\n";
        const string stderr = "\x1b[31mError\x1b[0m\n";
        _runner.RunCapturedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult(stdout, stderr, 0));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("ok");
        _teeService.TeeAndHintAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        await _sut.RunAsync(_filter, "dotnet", BuildArgs, 0);

        _filter.Received(1).Apply("Hello\nError\n", 0);
    }

    [Fact]
    public async Task RunAsync_RecordsCorrectTokenCounts_AfterSuccessfulExecution()
    {
        // tiktoken cl100k_base: "1234567890123456" = 6 tokens; "1234" = 2 tokens; saved = 4
        _runner.RunCapturedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("1234567890123456", "", 0));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("1234");
        _teeService.TeeAndHintAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

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
        _runner.RunCapturedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("output", "", 0));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");
        _teeService.TeeAndHintAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        await _sut.RunAsync(_filter, "dotnet", BuildArgs, 0);

        await _tracker.Received(1).RecordAsync(
            Arg.Is<CommandRecord>(r => r.Command == "build"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_RecordsCorrectCommand_WhenNoArgs_UsesCommandName()
    {
        _runner.RunCapturedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("output", "", 0));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");
        _teeService.TeeAndHintAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        await _sut.RunAsync(_filter, "dotnet", [], 0);

        await _tracker.Received(1).RecordAsync(
            Arg.Is<CommandRecord>(r => r.Command == "dotnet"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_RecordsNegativeSavedTokens_WhenFilterExpandsOutput()
    {
        // tiktoken cl100k_base: "1234" = 2 tokens; filter returns "1234567890123456" = 6 tokens; saved = -4
        _runner.RunCapturedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("1234", "", 0));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("1234567890123456");
        _teeService.TeeAndHintAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

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
        var sut = new FilteredRunUseCase(_runner, _tracker, _teeService, writer, configProvider);

        _runner.RunCapturedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("raw output", "", 0));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("✓ dotnet build\n");
        _teeService.TeeAndHintAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

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
        var sut = new FilteredRunUseCase(_runner, _tracker, _teeService, writer, configProvider);

        _runner.RunCapturedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("raw output", "", 0));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("✓ dotnet build\n");
        _teeService.TeeAndHintAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

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
        var sut = new FilteredRunUseCase(_runner, _tracker, _teeService, writer, configProvider);

        _runner.RunCapturedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("raw output", "", 1));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered\n");
        _teeService.TeeAndHintAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns("[full output: 123_test.log]");

        await sut.RunAsync(_filter, "dotnet", BuildArgs, 0);

        writer.ToString().Should().NotContain("[full output:");
    }

    [Fact]
    public async Task RunAsync_ShowLogHintTrue_PrintsHintLine()
    {
        await using var writer = new StringWriter();
        var configProvider = Substitute.For<IConfigProvider>();
        configProvider.LoadAsync(Arg.Any<CancellationToken>()).Returns(DtkConfig.Default);
        var sut = new FilteredRunUseCase(_runner, _tracker, _teeService, writer, configProvider);

        _runner.RunCapturedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("raw output", "", 1));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered\n");
        _teeService.TeeAndHintAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns("[full output: 123_test.log]");

        await sut.RunAsync(_filter, "dotnet", BuildArgs, 0, true);

        writer.ToString().Should().Contain("[full output: 123_test.log]");
    }

    [Fact]
    public async Task RunAsync_VerbosityLevel1_PrintsCommandLine()
    {
        await using var writer = new StringWriter();
        var configProvider = Substitute.For<IConfigProvider>();
        configProvider.LoadAsync(Arg.Any<CancellationToken>()).Returns(DtkConfig.Default);
        var sut = new FilteredRunUseCase(_runner, _tracker, _teeService, writer, configProvider);

        _runner.RunCapturedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("output", "", 0));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");
        _teeService.TeeAndHintAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        await sut.RunAsync(_filter, "dotnet", BuildArgs, 1);

        writer.ToString().Should().Contain("$ dotnet build");
    }

    [Fact]
    public async Task RunAsync_VerbosityLevel2_PrintsRawOutputAndElapsed()
    {
        await using var writer = new StringWriter();
        var configProvider = Substitute.For<IConfigProvider>();
        configProvider.LoadAsync(Arg.Any<CancellationToken>()).Returns(DtkConfig.Default);
        var sut = new FilteredRunUseCase(_runner, _tracker, _teeService, writer, configProvider);

        _runner.RunCapturedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("raw output", "", 0));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");
        _teeService.TeeAndHintAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

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
        var sut = new FilteredRunUseCase(_runner, _tracker, _teeService, writer, configProvider);

        _runner.RunCapturedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("raw output", "", 0));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Throws(new InvalidOperationException("boom"));
        _teeService.TeeAndHintAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

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
        var sut = new FilteredRunUseCase(_runner, _tracker, _teeService, writer, configProvider);

        _runner.RunCapturedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("raw output", "", 0));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered\n");
        _teeService.TeeAndHintAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

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
        _runner.RunCapturedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("", "", 0));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("");
        _teeService.TeeAndHintAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

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
        _runner.RunCapturedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("Hello world", "", 0));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("Hello");
        _teeService.TeeAndHintAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

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
        var sut = new FilteredRunUseCase(_runner, _tracker, _teeService, writer, configProvider);

        _runner.RunCapturedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("output", "", 0));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");
        _teeService.TeeAndHintAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

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
        var sut = new FilteredRunUseCase(_runner, _tracker, _teeService, writer, configProvider);

        _runner.RunCapturedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("raw stuff", "", 0));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");
        _teeService.TeeAndHintAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

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
        var sut = new FilteredRunUseCase(_runner, _tracker, _teeService, writer, configProvider);

        _runner.RunCapturedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("output", "", 0));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("✓ build ok\n");
        _teeService.TeeAndHintAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

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
        var sut = new FilteredRunUseCase(_runner, _tracker, _teeService, writer, configProvider);

        _runner.RunCapturedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("output", "", 0));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered\n");
        _teeService.TeeAndHintAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        await sut.RunAsync(_filter, "dotnet", BuildArgs, 0, true);

        writer.ToString().Should().Be("filtered\n");
    }

    [Fact]
    public async Task RunAsync_EmptyRawOutput_RecordsZeroSavingsPct()
    {
        // Covers inputTokens == 0 → savingsPct = 0.0 branch (line 126)
        _runner.RunCapturedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("", "", 0));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("");
        _teeService.TeeAndHintAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

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
        var sut = new FilteredRunUseCase(_runner, _tracker, _teeService, writer, configProvider);

        _runner.RunCapturedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("output", "", 0));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");
        _teeService.TeeAndHintAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

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
        var sut = new FilteredRunUseCase(_runner, _tracker, _teeService, writer, configProvider);

        _runner.RunCapturedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("output", "", 1));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered\n");
        _teeService.TeeAndHintAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns("[full output: 123_test.log]");

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
        var sut = new FilteredRunUseCase(_runner, _tracker, _teeService, writer, configProvider);

        _runner.RunCapturedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("output", "", 0));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered result\n");
        _teeService.TeeAndHintAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

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
        var sut = new FilteredRunUseCase(_runner, _tracker, _teeService, TextWriter.Null, configProvider);

        _runner.RunCapturedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("output", "", 0));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");
        _teeService.TeeAndHintAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

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
        var sut = new FilteredRunUseCase(_runner, _tracker, _teeService, writer, configProvider);

        _runner.RunCapturedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("raw output", "", 0));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Throws(new InvalidOperationException("boom"));
        _teeService.TeeAndHintAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

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
        var sut = new FilteredRunUseCase(_runner, _tracker, _teeService, writer, configProvider);

        _runner.RunCapturedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("\x1b[32mraw fallback\x1b[0m", "", 0));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Throws(new InvalidOperationException("boom"));
        _teeService.TeeAndHintAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        await sut.RunAsync(_filter, "dotnet", BuildArgs, 0);

        writer.ToString().Should().Contain("raw fallback");
    }

    [Fact]
    public async Task RunAsync_TeeCalledWithCorrectSlugAndExitCode()
    {
        // Kills statement mutations on tee invocation (line 97-98)
        _runner.RunCapturedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("output", "", 7));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");
        _teeService.TeeAndHintAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        await _sut.RunAsync(_filter, "dotnet", BuildArgs, 0);

        await _teeService.Received(1).TeeAndHintAsync(
            Arg.Any<string>(),
            "build",
            7,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_TrackingEnabled_RecordAsyncCalled()
    {
        // Kills boolean mutation on config.Tracking.Enabled (line 138)
        _runner.RunCapturedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("output", "", 0));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");
        _teeService.TeeAndHintAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        await _sut.RunAsync(_filter, "dotnet", BuildArgs, 0);

        await _tracker.Received(1).RecordAsync(
            Arg.Any<CommandRecord>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_ZeroExitCode_RecordsSuccessTrue()
    {
        // Kills equality mutation: exitCode == 0 → exitCode != 0
        _runner.RunCapturedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("output", "", 0));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");
        _teeService.TeeAndHintAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        await _sut.RunAsync(_filter, "dotnet", BuildArgs, 0);

        await _tracker.Received(1).RecordAsync(
            Arg.Is<CommandRecord>(r => r.Success),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_NonZeroExitCode_RecordsSuccessFalse()
    {
        // Kills equality mutation: exitCode == 0 → exitCode != 0
        _runner.RunCapturedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult("output", "", 1));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("filtered");
        _teeService.TeeAndHintAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

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

    private async Task<string> RunUseCaseAsync(int exitCode, string rawOutput)
    {
        await using var writer = new StringWriter();
        var sut = new FilteredRunUseCase(_runner, _tracker, _teeService, writer, _configProvider);

        _runner.RunCapturedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
            .Returns(new CommandResult(rawOutput, "", exitCode));
        _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns(string.Empty);
        _teeService.TeeAndHintAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns((string?)null);

        await sut.RunAsync(_filter, "dotnet", BuildArgs, 0);

        return writer.ToString();
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
            var sut = new FilteredRunUseCase(_runner, _tracker, _teeService, writer, configProvider);

            _runner.RunCapturedAsync(Arg.Any<string>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<CancellationToken>())
                .Returns(new CommandResult("output", "", 0));
            _filter.Apply(Arg.Any<string>(), Arg.Any<int>()).Returns("✓ build ok\n");
            _teeService.TeeAndHintAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(),
                    Arg.Any<CancellationToken>())
                .Returns((string?)null);

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
}
