using System.Text.Json;
using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Cli.Commands;
using DotnetTokenKiller.Cli.Commands.Settings;
using DotnetTokenKiller.Domain.Tracking;
using FluentAssertions;
using Spectre.Console.Testing;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests.Commands;

public class GainCommandTests
{
    private static GainSummary EmptySummary =>
        new(0, 0, 0, 0, 0.0, new Dictionary<string, CommandGainDetail>(StringComparer.Ordinal));

    [Fact]
    public async Task ExecuteAsync_JsonMode_WritesJson()
    {
        var (command, _, writer) = Create();

        var exitCode = await command.RunAsync(new GainCommandSettings
        {
            Json = true
        }, CancellationToken.None);

        exitCode.Should().Be(0);
        writer.ToString().Should().Contain("{");
    }

    [Fact]
    public async Task ExecuteAsync_NoData_WritesNoDataMessage()
    {
        var (command, console, _) = Create();

        var exitCode = await command.RunAsync(new GainCommandSettings(), CancellationToken.None);

        exitCode.Should().Be(0);
        console.Output.Should().Contain("No data yet");
    }

    [Fact]
    public async Task ExecuteAsync_WithData_WritesTable()
    {
        var successDetail = new CommandGainDetail(3, 1500, 250, 1250, 83.3);
        var failureDetail = new CommandGainDetail(2, 1000, 150, 850, 85.0);
        var details = new Dictionary<string, CommandGainDetail>(StringComparer.Ordinal)
        {
            ["build"] = new(5, 2500, 400, 2100, 84.0, successDetail, failureDetail)
        };
        var summary = new GainSummary(5, 2500, 400, 2100, 84.0, details);
        var (command, console, _) = Create(summary);

        var exitCode = await command.RunAsync(new GainCommandSettings(), CancellationToken.None);

        exitCode.Should().Be(0);
        console.Output.Should().Contain("build (ok)");
        console.Output.Should().Contain("build (fail)");
    }

    [Fact]
    public async Task ExecuteAsync_ProjectFlag_PassesCurrentDirectoryAsPath()
    {
        var tracker = new StubTracker
        {
            Summary = EmptySummary
        };
        var command = new GainCommand(new GainReportUseCase(tracker), new TestConsole(), new StringWriter());

        await command.RunAsync(new GainCommandSettings
        {
            Project = true
        }, CancellationToken.None);

        tracker.LastProjectPath.Should().Be(Environment.CurrentDirectory);
    }

    [Fact]
    public async Task ExecuteAsync_CommandFilter_PassesCommandFilterToUseCase()
    {
        var tracker = new StubTracker
        {
            Summary = EmptySummary
        };
        var command = new GainCommand(new GainReportUseCase(tracker), new TestConsole(), new StringWriter());

        await command.RunAsync(new GainCommandSettings
        {
            Command = "build"
        }, CancellationToken.None);

        tracker.LastCommandFilter.Should().Be("build");
    }

    private static (GainCommand command, TestConsole console, StringWriter writer) Create(GainSummary? summary = null)
    {
        var console = new TestConsole();
        var writer = new StringWriter();
        var tracker = new StubTracker
        {
            Summary = summary ?? EmptySummary
        };
        return (new GainCommand(new GainReportUseCase(tracker), console, writer), console, writer);
    }

    private static (GainCommand command, TestConsole console, StringWriter writer) CreateForCoverage(
        CoverageSummary coverage)
    {
        var console = new TestConsole();
        var writer = new StringWriter();
        var tracker = new StubTracker
        {
            Coverage = coverage
        };
        return (new GainCommand(new GainReportUseCase(tracker), console, writer), console, writer);
    }

    [Fact]
    public async Task ExecuteAsync_ExportCsv_NoRecords_WritesHeaderOnly()
    {
        var (command, _, writer) = Create();

        var exitCode = await command.RunAsync(new GainCommandSettings
        {
            Export = "csv"
        }, CancellationToken.None);

        exitCode.Should().Be(0);
        writer.ToString().Should().Contain("timestamp,command,project_path");
        writer.ToString().Should().Contain("execution_time_ms,success");
    }

    [Fact]
    public async Task ExecuteAsync_ExportCsv_WithRecords_WritesCsvRows()
    {
        var writer = new StringWriter();
        var tracker = new StubTracker
        {
            History =
            [
                new CommandRecord(
                    new DateTimeOffset(2025, 1, 15, 10, 0, 0, TimeSpan.Zero),
                    "build",
                    "/my/project",
                    new TokenStatistics(1000, 150, 850, 85.0),
                    TimeSpan.FromMilliseconds(500))
            ]
        };
        var command = new GainCommand(new GainReportUseCase(tracker), new TestConsole(), writer);

        var exitCode = await command.RunAsync(new GainCommandSettings
        {
            Export = "csv"
        }, CancellationToken.None);

        exitCode.Should().Be(0);
        writer.ToString().Should().Contain("build");
        writer.ToString().Should().Contain("/my/project");
        writer.ToString().Should().Contain("1000");
        writer.ToString().Should().Contain("850");
        writer.ToString().Should().Contain(",1"); // success=1
    }

    [Fact]
    public async Task ExecuteAsync_ExportCsv_UnknownFormat_ReturnsOne()
    {
        var (command, console, _) = Create();

        var exitCode = await command.RunAsync(new GainCommandSettings
        {
            Export = "xml"
        }, CancellationToken.None);

        exitCode.Should().Be(1);
        console.Output.Should().Contain("Unknown export format");
    }

    [Fact]
    public async Task ExecuteAsync_ExportCsv_FieldWithComma_IsQuoted()
    {
        var writer = new StringWriter();
        var tracker = new StubTracker
        {
            History =
            [
                new CommandRecord(
                    DateTimeOffset.UtcNow,
                    "build",
                    "/path/with,comma",
                    new TokenStatistics(100, 50, 50, 50.0),
                    TimeSpan.FromMilliseconds(100))
            ]
        };
        var command = new GainCommand(new GainReportUseCase(tracker), new TestConsole(), writer);

        await command.RunAsync(new GainCommandSettings
        {
            Export = "csv"
        }, CancellationToken.None);

        writer.ToString().Should().Contain("\"/path/with,comma\"");
    }

    [Fact]
    public async Task ExecuteAsync_JsonMode_LongCommand_ProducesParseableJson()
    {
        // A command string longer than a narrow console width would wrap and corrupt the JSON
        // if it were written through Spectre. The dedicated writer must emit it verbatim.
        var longCommand = new string('x', 200);
        var detail = new CommandGainDetail(1, 1000, 100, 900, 90.0);
        var details = new Dictionary<string, CommandGainDetail>(StringComparer.Ordinal)
        {
            [longCommand] = new(1, 1000, 100, 900, 90.0, detail)
        };
        var summary = new GainSummary(1, 1000, 100, 900, 90.0, details);
        var (command, _, writer) = Create(summary);

        await command.RunAsync(new GainCommandSettings
        {
            Json = true
        }, CancellationToken.None);

        var output = writer.ToString();
        var act = () => JsonDocument.Parse(output);
        act.Should().NotThrow();
        output.Should().Contain(longCommand); // key survived intact, not split across a wrap
    }

    [Fact]
    public async Task ExecuteAsync_ExportCsv_LongCommand_RowSurvivesNarrowConsole()
    {
        // Long records must not be line-wrapped: the header stays a single CSV line and the
        // long field is emitted whole.
        var longCommand = new string('x', 200);
        var writer = new StringWriter();
        var tracker = new StubTracker
        {
            History =
            [
                new CommandRecord(
                    new DateTimeOffset(2025, 1, 15, 10, 0, 0, TimeSpan.Zero),
                    longCommand,
                    "/my/project",
                    new TokenStatistics(1000, 150, 850, 85.0),
                    TimeSpan.FromMilliseconds(500))
            ]
        };
        var command = new GainCommand(new GainReportUseCase(tracker), new TestConsole(), writer);

        await command.RunAsync(new GainCommandSettings
        {
            Export = "csv"
        }, CancellationToken.None);

        var output = writer.ToString();
        // AppendLine emits "\r\n" on Windows; trim the "\r" so the split is cross-platform.
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        lines[0].TrimEnd('\r').Should().Be(GainCommand.CsvHeader); // complete header, unwrapped
        output.Should().Contain(longCommand); // full field, not split across a wrap
    }

    [Fact]
    public async Task ExecuteAsync_WithData_DisplaysAllColumnHeaders()
    {
        // Kills string mutations on column header literals ("Command", "Runs", etc.)
        var successDetail = new CommandGainDetail(2, 1000, 150, 850, 85.0);
        var details = new Dictionary<string, CommandGainDetail>(StringComparer.Ordinal)
        {
            ["build"] = new(2, 1000, 150, 850, 85.0, successDetail)
        };
        var summary = new GainSummary(2, 1000, 150, 850, 85.0, details);
        var (command, console, _) = Create(summary);

        await command.RunAsync(new GainCommandSettings(), CancellationToken.None);

        console.Output.Should().Contain("Command");
        console.Output.Should().Contain("Runs");
        console.Output.Should().Contain("Without Tool");
        console.Output.Should().Contain("Used by Tool");
        console.Output.Should().Contain("Saved");
        console.Output.Should().Contain("Avg%");
        console.Output.Should().Contain("Impact");
    }

    [Fact]
    public async Task ExecuteAsync_WithData_DisplaysRecapBlock()
    {
        var successDetail = new CommandGainDetail(3, 1500, 300, 1200, 80.0);
        var details = new Dictionary<string, CommandGainDetail>(StringComparer.Ordinal)
        {
            ["build"] = new(3, 1500, 300, 1200, 80.0, successDetail)
        };
        var summary = new GainSummary(3, 1500, 300, 1200, 80.0, details, TimeSpan.FromSeconds(2));
        var (command, console, _) = Create(summary);

        await command.RunAsync(new GainCommandSettings(), CancellationToken.None);

        console.Output.Should().Contain("DTK Token Savings (Global Scope)");
        console.Output.Should().Contain("Total commands:    3");
        console.Output.Should().Contain("Without tool:      1.5K");
        console.Output.Should().Contain("Used by tool:      300");
        console.Output.Should().Contain("Tokens saved:      1.2K (80.0%)");
        console.Output.Should().Contain("Efficiency meter:");
    }

    [Fact]
    public async Task ExecuteAsync_ProjectFlag_DisplaysProjectScope()
    {
        var successDetail = new CommandGainDetail(1, 1000, 100, 900, 90.0);
        var details = new Dictionary<string, CommandGainDetail>(StringComparer.Ordinal)
        {
            ["build"] = new(1, 1000, 100, 900, 90.0, successDetail)
        };
        var summary = new GainSummary(1, 1000, 100, 900, 90.0, details);
        var (command, console, _) = Create(summary);

        await command.RunAsync(new GainCommandSettings
        {
            Project = true
        }, CancellationToken.None);

        console.Output.Should().Contain("DTK Token Savings (Project Scope)");
    }

    [Fact]
    public async Task ExecuteAsync_NonDefaultDaysAndCommand_DisplaysFiltersInScope()
    {
        var successDetail = new CommandGainDetail(1, 1000, 100, 900, 90.0);
        var details = new Dictionary<string, CommandGainDetail>(StringComparer.Ordinal)
        {
            ["build"] = new(1, 1000, 100, 900, 90.0, successDetail)
        };
        var summary = new GainSummary(1, 1000, 100, 900, 90.0, details);
        var (command, console, _) = Create(summary);

        await command.RunAsync(new GainCommandSettings
        {
            Days = 7,
            Command = "build"
        }, CancellationToken.None);

        console.Output.Should().Contain("last 7 days");
        console.Output.Should().Contain("command: build");
    }

    [Fact]
    public async Task ExecuteAsync_JsonMode_IncludesExecutionTime()
    {
        var (command, _, writer) = Create();

        await command.RunAsync(new GainCommandSettings
        {
            Json = true
        }, CancellationToken.None);

        writer.ToString().Should().Contain("TotalExecutionTime");
    }

    [Fact]
    public async Task ExecuteAsync_WithSuccessDetail_DisplaysFormattedPercentage()
    {
        // Kills string mutation on "F1" format specifier and "%" literal in AverageSavingsPercentage display
        var successDetail = new CommandGainDetail(1, 1000, 167, 833, 83.3);
        var details = new Dictionary<string, CommandGainDetail>(StringComparer.Ordinal)
        {
            ["build"] = new(1, 1000, 167, 833, 83.3, successDetail)
        };
        var summary = new GainSummary(1, 1000, 167, 833, 83.3, details);
        var (command, console, _) = Create(summary);

        await command.RunAsync(new GainCommandSettings(), CancellationToken.None);

        console.Output.Should().Contain("83.3%");
    }

    [Fact]
    public async Task ExecuteAsync_Coverage_ReportsUnfilteredCommands()
    {
        var coverage = new CoverageSummary(
            [
                new CoverageDetail("publish", RunOutcome.PassthroughMeasured, RunSource.Run, 4, 48_000,
                    TimeSpan.FromSeconds(12)),
                new CoverageDetail("build", RunOutcome.Filtered, RunSource.Run, 30, 300_000, TimeSpan.FromSeconds(90))
            ],
            34,
            48_000);
        var (command, console, _) = CreateForCoverage(coverage);

        var exitCode = await command.RunAsync(new GainCommandSettings
        {
            Coverage = true
        }, CancellationToken.None);

        exitCode.Should().Be(0);
        console.Output.Should().Contain("publish");
        console.Output.Should().Contain("PassthroughMeasured");
    }

    [Fact]
    public async Task ExecuteAsync_Coverage_NoData_WritesNoDataMessage()
    {
        var (command, console, _) = CreateForCoverage(new CoverageSummary([], 0, 0));

        var exitCode = await command.RunAsync(new GainCommandSettings
        {
            Coverage = true
        }, CancellationToken.None);

        exitCode.Should().Be(0);
        console.Output.Should().Contain("No data yet");
    }

    [Fact]
    public async Task ExecuteAsync_CoverageJson_WritesParseableJsonWithNamedOutcomes()
    {
        var coverage = new CoverageSummary(
            [
                new CoverageDetail("publish", RunOutcome.PassthroughMeasured, RunSource.Run, 4, 48_000,
                    TimeSpan.FromSeconds(12))
            ],
            4,
            48_000);
        var (command, _, writer) = CreateForCoverage(coverage);

        var exitCode = await command.RunAsync(new GainCommandSettings
        {
            Coverage = true,
            Json = true
        }, CancellationToken.None);

        exitCode.Should().Be(0);
        var json = writer.ToString();
        // The outcome must serialize as its name, not as the integer 3, or the JSON is unreadable.
        json.Should().Contain("PassthroughMeasured");
        var act = () => JsonDocument.Parse(json);
        act.Should().NotThrow();
    }

    [Fact]
    public async Task ExecuteAsync_Coverage_PassesFiltersToTheUseCase()
    {
        var tracker = new StubTracker
        {
            Coverage = new CoverageSummary([], 0, 0)
        };
        var command = new GainCommand(new GainReportUseCase(tracker), new TestConsole(), new StringWriter());

        await command.RunAsync(new GainCommandSettings
        {
            Coverage = true,
            Project = true,
            Command = "publish"
        }, CancellationToken.None);

        tracker.LastProjectPath.Should().Be(Environment.CurrentDirectory);
        tracker.LastCommandFilter.Should().Be("publish");
    }

    [Fact]
    public void CsvHeader_EndsWithOutcomeThenSource()
    {
        // The export must carry both new dimensions, in order, or the CSV silently loses them.
        GainCommand.CsvHeader.Should().EndWith(",outcome,source");
    }

    [Fact]
    public async Task ExecuteAsync_ExportCsv_WritesTheOutcomeColumn()
    {
        var writer = new StringWriter();
        var tracker = new StubTracker
        {
            History =
            [
                new CommandRecord(
                    new DateTimeOffset(2025, 1, 15, 10, 0, 0, TimeSpan.Zero),
                    "publish",
                    "/my/project",
                    new TokenStatistics(1000, 1000, 0, 0.0),
                    TimeSpan.FromMilliseconds(500),
                    success: true,
                    outcome: RunOutcome.PassthroughMeasured)
            ]
        };
        var command = new GainCommand(new GainReportUseCase(tracker), new TestConsole(), writer);

        await command.RunAsync(new GainCommandSettings
        {
            Export = "csv"
        }, CancellationToken.None);

        writer.ToString().Should().Contain("PassthroughMeasured");
    }

    private sealed class StubTracker : ITracker
    {
        public GainSummary Summary { get; init; } = new(0, 0, 0, 0, 0.0,
            new Dictionary<string, CommandGainDetail>(StringComparer.Ordinal));

        public IReadOnlyList<CommandRecord> History { get; init; } = [];

        public CoverageSummary Coverage { get; init; } = new([], 0, 0);

        public string? LastProjectPath { get; private set; }

        public string? LastCommandFilter { get; private set; }

        public Task RecordAsync(CommandRecord record, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<GainSummary> GetSummaryAsync(int days, string? projectPath,
            string? commandFilter = null, CancellationToken cancellationToken = default)
        {
            LastProjectPath = projectPath;
            LastCommandFilter = commandFilter;
            return Task.FromResult(Summary);
        }

        public Task<IReadOnlyList<CommandRecord>> GetHistoryAsync(int days, string? projectPath,
            string? commandFilter = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(History);
        }

        public Task<CoverageSummary> GetCoverageAsync(int days, string? projectPath,
            string? commandFilter = null, CancellationToken cancellationToken = default)
        {
            LastProjectPath = projectPath;
            LastCommandFilter = commandFilter;
            return Task.FromResult(Coverage);
        }

        public Task CleanupAsync(int retentionDays, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task ResetAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }
}
