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
        var (command, console) = Create();

        var exitCode = await command.RunAsync(new GainCommandSettings
        {
            Json = true
        }, CancellationToken.None);

        exitCode.Should().Be(0);
        console.Output.Should().Contain("{");
    }

    [Fact]
    public async Task ExecuteAsync_NoData_WritesNoDataMessage()
    {
        var (command, console) = Create();

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
        var (command, console) = Create(summary);

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
        var console = new TestConsole();
        var command = new GainCommand(new GainReportUseCase(tracker), console);

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
        var console = new TestConsole();
        var command = new GainCommand(new GainReportUseCase(tracker), console);

        await command.RunAsync(new GainCommandSettings
        {
            Command = "build"
        }, CancellationToken.None);

        tracker.LastCommandFilter.Should().Be("build");
    }

    private static (GainCommand command, TestConsole console) Create(GainSummary? summary = null)
    {
        var console = new TestConsole();
        var tracker = new StubTracker
        {
            Summary = summary ?? EmptySummary
        };
        return (new GainCommand(new GainReportUseCase(tracker), console), console);
    }

    [Fact]
    public async Task ExecuteAsync_ExportCsv_NoRecords_WritesHeaderOnly()
    {
        var (command, console) = Create();

        var exitCode = await command.RunAsync(new GainCommandSettings
        {
            Export = "csv"
        }, CancellationToken.None);

        exitCode.Should().Be(0);
        console.Output.Should().Contain("timestamp,command,project_path");
        console.Output.Should().Contain("execution_time_ms,success");
    }

    [Fact]
    public async Task ExecuteAsync_ExportCsv_WithRecords_WritesCsvRows()
    {
        var console = new TestConsole();
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
        var command = new GainCommand(new GainReportUseCase(tracker), console);

        var exitCode = await command.RunAsync(new GainCommandSettings
        {
            Export = "csv"
        }, CancellationToken.None);

        exitCode.Should().Be(0);
        console.Output.Should().Contain("build");
        console.Output.Should().Contain("/my/project");
        console.Output.Should().Contain("1000");
        console.Output.Should().Contain("850");
        console.Output.Should().Contain(",1"); // success=1
    }

    [Fact]
    public async Task ExecuteAsync_ExportCsv_UnknownFormat_ReturnsOne()
    {
        var (command, console) = Create();

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
        var console = new TestConsole();
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
        var command = new GainCommand(new GainReportUseCase(tracker), console);

        await command.RunAsync(new GainCommandSettings
        {
            Export = "csv"
        }, CancellationToken.None);

        console.Output.Should().Contain("\"/path/with,comma\"");
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
        var (command, console) = Create(summary);

        await command.RunAsync(new GainCommandSettings(), CancellationToken.None);

        console.Output.Should().Contain("Command");
        console.Output.Should().Contain("Runs");
        console.Output.Should().Contain("Without Tool");
        console.Output.Should().Contain("Used by Tool");
        console.Output.Should().Contain("Saved");
        console.Output.Should().Contain("Avg Savings");
    }

    [Fact]
    public async Task ExecuteAsync_WithData_DisplaysTotalRowWithCorrectValues()
    {
        // Kills string mutations on "TOTAL" markup and statement mutations on AddRow calls
        var successDetail = new CommandGainDetail(3, 1500, 300, 1200, 80.0);
        var details = new Dictionary<string, CommandGainDetail>(StringComparer.Ordinal)
        {
            ["build"] = new(3, 1500, 300, 1200, 80.0, successDetail)
        };
        var summary = new GainSummary(3, 1500, 300, 1200, 80.0, details);
        var (command, console) = Create(summary);

        await command.RunAsync(new GainCommandSettings(), CancellationToken.None);

        console.Output.Should().Contain("TOTAL");
        console.Output.Should().Contain("1500"); // TotalInputTokens
        console.Output.Should().Contain("300"); // TotalOutputTokens
        console.Output.Should().Contain("1200"); // TotalSavedTokens
        console.Output.Should().Contain("80.0%"); // AverageSavingsPercentage formatted as F1
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
        var (command, console) = Create(summary);

        await command.RunAsync(new GainCommandSettings(), CancellationToken.None);

        console.Output.Should().Contain("83.3%");
    }

    private sealed class StubTracker : ITracker
    {
        public GainSummary Summary { get; init; } = new(0, 0, 0, 0, 0.0,
            new Dictionary<string, CommandGainDetail>(StringComparer.Ordinal));

        public IReadOnlyList<CommandRecord> History { get; init; } = [];

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
