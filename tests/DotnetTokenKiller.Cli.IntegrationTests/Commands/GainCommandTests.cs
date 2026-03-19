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

    private static (GainCommand command, TestConsole console) Create(GainSummary? summary = null)
    {
        var console = new TestConsole();
        var tracker = new StubTracker
        {
            Summary = summary ?? EmptySummary
        };
        return (new GainCommand(new GainReportUseCase(tracker), console), console);
    }

    private sealed class StubTracker : ITracker
    {
        public GainSummary Summary { get; set; } = new(0, 0, 0, 0, 0.0,
            new Dictionary<string, CommandGainDetail>(StringComparer.Ordinal));

        public string? LastProjectPath { get; private set; }

        public Task RecordAsync(CommandRecord record, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<GainSummary> GetSummaryAsync(int days, string? projectPath,
            CancellationToken cancellationToken = default)
        {
            LastProjectPath = projectPath;
            return Task.FromResult(Summary);
        }

        public Task<IReadOnlyList<CommandRecord>> GetHistoryAsync(int days, string? projectPath,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<IReadOnlyList<CommandRecord>>([]);
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
