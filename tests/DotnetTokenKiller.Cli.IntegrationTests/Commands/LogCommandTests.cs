using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Cli.Commands;
using DotnetTokenKiller.Cli.Commands.Settings;
using DotnetTokenKiller.Cli.Infrastructure;
using DotnetTokenKiller.Cli.IntegrationTests.Helpers;
using DotnetTokenKiller.Domain.Configuration;
using DotnetTokenKiller.Domain.Tee;
using DotnetTokenKiller.Domain.Tracking;
using FluentAssertions;
using Spectre.Console.Testing;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests.Commands;

public sealed class LogCommandTests
{
    private static DateTimeOffset At(int minute) => new(2026, 7, 28, 9, minute, 0, TimeSpan.Zero);

    private const string Cwd = "/proj";

    private static TeeLogEntry Entry(int minute, string slug, string? cwd = Cwd, int exitCode = 1) =>
        new($"/tee/{minute}_{slug}.log",
            cwd is null ? null : new TeeLogHeader($"dotnet {slug}", cwd, exitCode, RunSource.Run, At(minute)),
            2048,
            At(minute),
            slug);

    private sealed class FakeStore(Dictionary<string, string> bodies, params TeeLogEntry[] entries) : ITeeLogStore
    {
        public Task<IReadOnlyList<TeeLogEntry>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TeeLogEntry>>([.. entries.OrderByDescending(e => e.TimestampUtc)]);

        public Task<string> ReadBodyAsync(TeeLogEntry entry, CancellationToken cancellationToken = default) =>
            Task.FromResult(bodies.TryGetValue(entry.FilePath, out var b) ? b : string.Empty);
    }

    private sealed class ThrowingBodyStore(params TeeLogEntry[] entries) : ITeeLogStore
    {
        public Task<IReadOnlyList<TeeLogEntry>> ListAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TeeLogEntry>>([.. entries.OrderByDescending(e => e.TimestampUtc)]);

        public Task<string> ReadBodyAsync(TeeLogEntry entry, CancellationToken cancellationToken = default) =>
            throw new IOException("log file deleted by rotation");
    }

    private sealed class FakeWorkingDirectory(string path) : IWorkingDirectory
    {
        public string Current => path;
    }

    private static (LogCommand command, TestConsole console, StringWriter writer) Create(
        ITeeLogStore store,
        TeeMode teeMode = TeeMode.Failures)
    {
        var console = new TestConsole();

        // Wide enough that the listing table never wraps a path or command mid-word, which would
        // make the Contain assertions below fail for reasons that have nothing to do with selection.
        console.Profile.Width = 200;

        var writer = new StringWriter();
        var config = new FakeConfigProvider(
            DtkConfig.Default with { Tee = new TeeConfig(teeMode) });
        var command = new LogCommand(
            new LogViewUseCase(store), config, console, writer, new FakeWorkingDirectory(Cwd));
        return (command, console, writer);
    }

    [Fact]
    public async Task Run_PrintsTheNewestLogForTheCurrentProject()
    {
        var entry = Entry(5, "build");
        var bodies = new Dictionary<string, string> { [entry.FilePath] = "line one\nline two\n" };
        var (command, _, writer) = Create(new FakeStore(bodies, entry, Entry(9, "build", cwd: "/elsewhere")));

        var exitCode = await command.RunAsync(new LogCommandSettings(), CancellationToken.None);

        exitCode.Should().Be(0);
        var output = writer.ToString();
        output.Should().Contain("dotnet build");
        output.Should().Contain("line two");
        // The newer log belongs to another project and must not be what we got.
        output.Should().NotContain("/elsewhere");
    }

    [Fact]
    public async Task Run_ReportsWhatItWithheld()
    {
        var entry = Entry(5, "build");
        var body = string.Join('\n', Enumerable.Range(1, 400).Select(i => $"line {i}")) + "\n";
        var (command, _, writer) = Create(new FakeStore(
            new Dictionary<string, string> { [entry.FilePath] = body }, entry));

        await command.RunAsync(new LogCommandSettings { Lines = 10 }, CancellationToken.None);

        var output = writer.ToString();
        output.Should().Contain("showing last 10 of 400 lines");
        output.Should().Contain("--full");
        output.Should().NotContain("line 389\n");
    }

    [Fact]
    public async Task Run_SaysShowingAllLines_WhenNothingWasWithheld()
    {
        var entry = Entry(5, "build");
        var (command, _, writer) = Create(new FakeStore(
            new Dictionary<string, string> { [entry.FilePath] = "a\nb\n" }, entry));

        await command.RunAsync(new LogCommandSettings { Lines = 100 }, CancellationToken.None);

        writer.ToString().Should().Contain("showing all 2 lines");
    }

    [Fact]
    public async Task Run_ExitsOneAndExplains_WhenTeeIsDisabled()
    {
        var (command, console, _) = Create(new FakeStore([]), TeeMode.Never);

        var exitCode = await command.RunAsync(new LogCommandSettings(), CancellationToken.None);

        exitCode.Should().Be(1);
        console.Output.Should().Contain("tee.mode");
        console.Output.Should().Contain("Never");
    }

    [Fact]
    public async Task Run_ExitsOneAndExplainsTheDefaults_WhenNoLogExists()
    {
        var (command, console, _) = Create(new FakeStore([]));

        var exitCode = await command.RunAsync(new LogCommandSettings(), CancellationToken.None);

        exitCode.Should().Be(1);
        // The reason matters more than the fact: with tee.mode Failures a passing run leaves nothing.
        console.Output.Should().Contain("Failures");
        console.Output.Should().Contain("500");
    }

    [Fact]
    public async Task Run_NamesExcludedLegacyLogs_SoAnEmptyResultIsNotMistakenForDataLoss()
    {
        var (command, console, _) = Create(new FakeStore([], Entry(5, "build", cwd: null)));

        var exitCode = await command.RunAsync(new LogCommandSettings(), CancellationToken.None);

        exitCode.Should().Be(1);
        console.Output.Should().Contain("--all");
        console.Output.Should().Contain("1");
    }

    [Fact]
    public async Task Run_ReportsTheAvailableCount_WhenTheIndexIsTooHigh()
    {
        var entry = Entry(5, "build");
        var (command, console, _) = Create(new FakeStore(
            new Dictionary<string, string> { [entry.FilePath] = "x\n" }, entry));

        var exitCode = await command.RunAsync(
            new LogCommandSettings { Index = 7 }, CancellationToken.None);

        exitCode.Should().Be(1);
        console.Output.Should().Contain("7");
        console.Output.Should().Contain("1");
    }

    [Fact]
    public async Task Run_ListsMatches_WhenListRequested()
    {
        var (command, console, _) = Create(new FakeStore([], Entry(5, "build"), Entry(9, "test")));

        var exitCode = await command.RunAsync(
            new LogCommandSettings { List = true }, CancellationToken.None);

        exitCode.Should().Be(0);
        console.Output.Should().Contain("build").And.Contain("test");
    }

    [Fact]
    public async Task Run_ListWins_OverIndexAndFull()
    {
        var entry = Entry(5, "build");
        var (command, console, writer) = Create(new FakeStore(
            new Dictionary<string, string> { [entry.FilePath] = "body text\n" }, entry));

        var exitCode = await command.RunAsync(
            new LogCommandSettings { List = true, Full = true, Index = 1 }, CancellationToken.None);

        exitCode.Should().Be(0);
        writer.ToString().Should().NotContain("body text");
        console.Output.Should().Contain("build");
    }

    [Fact]
    public async Task Run_IncludesOtherProjects_WhenAllRequested()
    {
        var (command, console, _) = Create(new FakeStore([], Entry(9, "build", cwd: "/elsewhere")));

        var exitCode = await command.RunAsync(
            new LogCommandSettings { List = true, All = true }, CancellationToken.None);

        exitCode.Should().Be(0);
        console.Output.Should().Contain("elsewhere");
    }

    [Fact]
    public async Task Run_ListsALegacyNullHeaderEntry_WithUnknownProjectAndExitPlaceholders()
    {
        // A log written by dtk <= 0.6.0 has no header (Header is null). That is exactly what every
        // existing user sees on their first upgrade under --all, and the renderer must fall back to
        // the unknown-project and unknown-exit placeholders rather than throwing or misrendering.
        var (command, console, _) = Create(new FakeStore([], Entry(5, "build", cwd: null)));

        var exitCode = await command.RunAsync(
            new LogCommandSettings { List = true, All = true }, CancellationToken.None);

        exitCode.Should().Be(0);
        console.Output.Should().Contain("unknown");
        console.Output.Should().Contain("?");
    }

    [Fact]
    public async Task Run_RejectsAnUnknownSubcommand_AndListsTheKnownOnes()
    {
        var (command, console, _) = Create(new FakeStore([]));

        var exitCode = await command.RunAsync(
            new LogCommandSettings { Subcommand = ["banana"] }, CancellationToken.None);

        exitCode.Should().Be(1);
        console.Output.Should().Contain("banana").And.Contain("build");
    }

    [Fact]
    public async Task Run_RejectsAnInteractivePassthroughSubcommand()
    {
        // "run" is a real dotnet verb, but it is not in PassthroughSubcommands.Measurable — its
        // stdio stays attached to the terminal and it is never tee'd, so dtk log must still reject
        // it rather than widening to every known dotnet verb.
        var (command, console, _) = Create(new FakeStore([]));

        var exitCode = await command.RunAsync(
            new LogCommandSettings { Subcommand = ["run"] }, CancellationToken.None);

        exitCode.Should().Be(1);
        console.Output.Should().Contain("run").And.Contain("publish");
    }

    [Fact]
    public async Task Run_AcceptsAMeasurablePassthroughSubcommand()
    {
        // Slugged exactly as PassthroughSubcommands.CommandName(["publish"]) writes it.
        var entry = Entry(5, "publish");
        var (command, console, _) = Create(new FakeStore(
            new Dictionary<string, string> { [entry.FilePath] = "publishing...\n" }, entry, Entry(9, "build")));

        var exitCode = await command.RunAsync(
            new LogCommandSettings { Subcommand = ["publish"], List = true },
            CancellationToken.None);

        exitCode.Should().Be(0);
        console.Output.Should().Contain("publish");
    }

    [Fact]
    public async Task Run_AcceptsAMultiTokenMeasurablePassthroughSubcommand()
    {
        // PassthroughSubcommands.CommandName(["ef", "migrations"]) is "ef migrations", sanitised to
        // "ef-migrations" by TeeLogFileName.Sanitize — the same slug the log was actually written under.
        var entry = Entry(5, "ef-migrations");
        var (command, console, _) = Create(new FakeStore(
            new Dictionary<string, string> { [entry.FilePath] = "migrating...\n" }, entry, Entry(9, "build")));

        var exitCode = await command.RunAsync(
            new LogCommandSettings { Subcommand = ["ef", "migrations"], List = true },
            CancellationToken.None);

        exitCode.Should().Be(0);
        console.Output.Should().Contain("ef-migrations");
    }

    [Fact]
    public async Task Run_MatchesAMultiTokenSubcommand()
    {
        var entry = Entry(5, "list-package");
        var (command, console, _) = Create(new FakeStore(
            new Dictionary<string, string> { [entry.FilePath] = "x\n" }, entry, Entry(9, "build")));

        var exitCode = await command.RunAsync(
            new LogCommandSettings { Subcommand = ["list", "package"], List = true },
            CancellationToken.None);

        exitCode.Should().Be(0);
        // The Entry helper builds its command line from the slug, so this is "dotnet list-package".
        console.Output.Should().Contain("list-package");
        console.Output.Should().NotContain("dotnet build");
    }

    [Fact]
    public async Task Run_ReportsUnavailable_WhenTheLogFileWasRotatedAwayBeforeReading()
    {
        var (command, console, _) = Create(new ThrowingBodyStore(Entry(5, "build")));

        var exitCode = await command.RunAsync(new LogCommandSettings(), CancellationToken.None);

        exitCode.Should().Be(1);
        console.Output.Should().Contain("no longer available");
        console.Output.Should().Contain("--list");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Run_RejectsANonPositiveLineCount(int lines)
    {
        var (command, _, _) = Create(new FakeStore([]));

        var exitCode = await command.RunAsync(new LogCommandSettings { Lines = lines }, CancellationToken.None);

        exitCode.Should().Be(1);
    }

    [Fact]
    public async Task Run_RejectsANonPositiveIndex()
    {
        var (command, _, _) = Create(new FakeStore([]));

        var exitCode = await command.RunAsync(new LogCommandSettings { Index = 0 }, CancellationToken.None);

        exitCode.Should().Be(1);
    }

    /// <summary>Builds an entry shaped exactly as an abandoned session leaves one on disk.</summary>
    /// <param name="minute">The minute component used for the timestamp.</param>
    /// <param name="slug">The log's slug.</param>
    /// <param name="commandLine">The command line recorded in the header.</param>
    private static TeeLogEntry IncompleteEntry(int minute, string slug, string commandLine) =>
        new($"/tee/{minute}_{slug}_incomplete.log",
            new TeeLogHeader(commandLine, Cwd, null, RunSource.Run, At(minute)),
            2048,
            At(minute),
            slug);

    [Fact]
    public async Task Run_MarksAnUnfinishedRunAsIncomplete()
    {
        var entry = IncompleteEntry(5, "build", "dotnet build MyApp.slnx");
        var bodies = new Dictionary<string, string> { [entry.FilePath] = "compiling...\n" };
        var (command, _, writer) = Create(new FakeStore(bodies, entry));

        var exitCode = await command.RunAsync(new LogCommandSettings(), CancellationToken.None);

        exitCode.Should().Be(0);
        var output = writer.ToString();
        output.Should().Contain("incomplete");
        output.Should().Contain("run did not finish");
        output.Should().Contain("compiling...");
    }

    [Fact]
    public async Task Run_List_ShowsIncompleteInsteadOfAnExitCode()
    {
        var (command, console, _) = Create(new FakeStore(
            [],
            Entry(5, "build"),
            IncompleteEntry(9, "build", "dotnet build B.slnx")));

        await command.RunAsync(new LogCommandSettings { List = true }, CancellationToken.None);

        console.Output.Should().Contain("incomplete");
    }

    [Fact]
    public async Task Run_OmitsTheKilledNote_ForACompletedRun()
    {
        // Entry() defaults to a non-null ExitCode, i.e. a run that finished normally. The killed-run
        // note is only true for a run that never reached finalization, so it must not appear here.
        var entry = Entry(5, "build");
        var bodies = new Dictionary<string, string> { [entry.FilePath] = "done\n" };
        var (command, _, writer) = Create(new FakeStore(bodies, entry));

        var exitCode = await command.RunAsync(new LogCommandSettings(), CancellationToken.None);

        exitCode.Should().Be(0);
        writer.ToString().Should().NotContain("run did not finish");
    }

    [Fact]
    public async Task Run_OmitsTheKilledNote_ForALegacyLogWithNoHeader()
    {
        // A legacy log (Header is null, cwd: null) predates the header that would let us tell whether
        // dtk was killed. "We don't know" is a different claim from "dtk was killed", so the killed-run
        // note must not appear alongside the existing "exit unknown" placeholder for this case.
        var entry = Entry(5, "build", cwd: null);
        var bodies = new Dictionary<string, string> { [entry.FilePath] = "line one\n" };
        var (command, _, writer) = Create(new FakeStore(bodies, entry));

        // Legacy (headerless) entries are excluded from the default project-scoped view, so --all
        // is needed to select this one at all.
        var exitCode = await command.RunAsync(
            new LogCommandSettings { All = true }, CancellationToken.None);

        exitCode.Should().Be(0);
        var output = writer.ToString();
        output.Should().Contain("exit unknown");
        output.Should().NotContain("run did not finish");
    }
}
