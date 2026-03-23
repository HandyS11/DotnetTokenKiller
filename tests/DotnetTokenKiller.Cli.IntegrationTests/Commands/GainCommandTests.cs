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

        var exitCode = await command.ExecuteAsync(null!, new GainCommandSettings
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

        var exitCode = await command.ExecuteAsync(null!, new GainCommandSettings(), CancellationToken.None);

        exitCode.Should().Be(0);
        console.Output.Should().Contain("No data yet");
    }

    [Fact]
    public async Task ExecuteAsync_WithData_WritesTable()
    {
        var details = new Dictionary<string, CommandGainDetail>(StringComparer.Ordinal)
        {
            ["build"] = new(5, 2500, 400, 2100, 84.0)
        };
        var summary = new GainSummary(5, 2500, 400, 2100, 84.0, details);
        var (command, console) = Create(summary);

        var exitCode = await command.ExecuteAsync(null!, new GainCommandSettings(), CancellationToken.None);

        exitCode.Should().Be(0);
        console.Output.Should().Contain("build");
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

        await command.ExecuteAsync(null!, new GainCommandSettings
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

        await command.ExecuteAsync(null!, new GainCommandSettings
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

        var exitCode = await command.ExecuteAsync(null!, new GainCommandSettings
        {
            Export = "csv"
        }, CancellationToken.None);

        exitCode.Should().Be(0);
        console.Output.Should().Contain("timestamp,command,project_path");
        console.Output.Should().Contain("execution_time_ms");
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

        var exitCode = await command.ExecuteAsync(null!, new GainCommandSettings
        {
            Export = "csv"
        }, CancellationToken.None);

        exitCode.Should().Be(0);
        console.Output.Should().Contain("build");
        console.Output.Should().Contain("/my/project");
        console.Output.Should().Contain("1000");
        console.Output.Should().Contain("850");
    }

    [Fact]
    public async Task ExecuteAsync_ExportCsv_UnknownFormat_ReturnsOne()
    {
        var (command, console) = Create();

        var exitCode = await command.ExecuteAsync(null!, new GainCommandSettings
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

        await command.ExecuteAsync(null!, new GainCommandSettings
        {
            Export = "csv"
        }, CancellationToken.None);

        console.Output.Should().Contain("\"/path/with,comma\"");
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
