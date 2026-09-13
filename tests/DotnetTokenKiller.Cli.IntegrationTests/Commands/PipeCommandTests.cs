using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Cli.Commands;
using DotnetTokenKiller.Cli.Commands.Settings;
using DotnetTokenKiller.Cli.Infrastructure;
using DotnetTokenKiller.Domain.Configuration;
using DotnetTokenKiller.Domain.Tee;
using DotnetTokenKiller.Domain.Tracking;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console.Testing;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests.Commands;

public class PipeCommandTests
{
    [Fact]
    public async Task ExecuteAsync_StdinNotRedirected_ReturnsOneAndShowsGuidanceAsync()
    {
        var console = new TestConsole();
        var command = CreateCommand(console, isRedirected: false);

        var exitCode = await command.RunAsync(new PipeCommandSettings
        {
            Subcommand = ["build"]
        }, CancellationToken.None);

        exitCode.Should().Be(1);
        console.Output.Should().Contain("dtk pipe reads from standard input");
    }

    private static PipeCommand CreateCommand(TestConsole console, bool isRedirected)
    {
        var pipeline = new FilteredOutputPipeline(
            new StubTracker(), new StringWriter(), new StubConfigProvider());
        var pipeFilter = new PipeFilterUseCase(pipeline, new StubTeeService(), new StringReader(string.Empty));
        var services = new ServiceCollection().BuildServiceProvider();
        return new PipeCommand(pipeFilter, services, console, new StubStandardInputState(isRedirected));
    }

    private sealed class StubStandardInputState(bool isRedirected) : IStandardInputState
    {
        public bool IsRedirected { get; } = isRedirected;
    }

    private sealed class StubTracker : ITracker
    {
        public Task RecordAsync(CommandRecord record, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task WarmUpAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<GainSummary> GetSummaryAsync(int days, string? projectPath,
            string? commandFilter = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new GainSummary(0, 0, 0, 0, 0.0,
                new Dictionary<string, CommandGainDetail>(StringComparer.Ordinal)));
        }

        public Task<IReadOnlyList<CommandRecord>> GetHistoryAsync(int days, string? projectPath,
            string? commandFilter = null, CancellationToken cancellationToken = default)
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

        public Task<CoverageSummary> GetCoverageAsync(int days, string? projectPath,
            string? commandFilter = null, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new CoverageSummary([], 0, 0));
        }
    }

    private sealed class StubConfigProvider : IConfigProvider
    {
        public DtkConfig Load()
        {
            return DtkConfig.Default;
        }

        public Task<DtkConfig> LoadAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(DtkConfig.Default);
        }

        public Task SaveAsync(DtkConfig config, CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task DeleteAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }
    }

    private sealed class StubTeeService : ITeeService
    {
        public Task DeleteLogsAsync(CancellationToken cancellationToken = default)
        {
            return Task.CompletedTask;
        }

        public Task<ITeeSession> BeginAsync(
            string commandSlug, TeeLogHeader provisional, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<ITeeSession>(NullTeeSession.Instance);
        }
    }
}
