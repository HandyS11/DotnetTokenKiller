using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Cli.Commands;
using DotnetTokenKiller.Cli.Commands.Settings;
using DotnetTokenKiller.Domain.Configuration;
using DotnetTokenKiller.Domain.Tee;
using DotnetTokenKiller.Domain.Tracking;
using FluentAssertions;
using Spectre.Console.Testing;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests.Commands;

public class ResetCommandTests
{
    [Fact]
    public async Task ExecuteAsync_ForceFlag_ResetsWithoutConfirmation()
    {
        var (tracker, _, _) = CreateStubs();
        var console = new TestConsole();
        var command = CreateCommand(tracker, console);

        var exitCode = await command.RunAsync(new ResetCommandSettings
        {
            Force = true
        }, CancellationToken.None);

        exitCode.Should().Be(0);
        tracker.WasReset.Should().BeTrue();
        console.Output.Should().Contain("cleared");
    }

    [Fact]
    public async Task ExecuteAsync_NoForce_UserDenies_DoesNotReset()
    {
        var (tracker, _, _) = CreateStubs();
        var console = new TestConsole();
        console.Input.PushTextWithEnter("n");
        var command = CreateCommand(tracker, console);

        var exitCode = await command.RunAsync(new ResetCommandSettings
        {
            Force = false
        }, CancellationToken.None);

        exitCode.Should().Be(0);
        tracker.WasReset.Should().BeFalse();
        console.Output.Should().Contain("Aborted");
    }

    [Fact]
    public async Task ExecuteAsync_AllFlag_WithForce_RemovesAllState()
    {
        var (tracker, config, tee) = CreateStubs();
        var console = new TestConsole();
        var command = CreateCommand(tracker, console, config, tee);

        var exitCode = await command.RunAsync(new ResetCommandSettings
        {
            Force = true,
            All = true
        }, CancellationToken.None);

        exitCode.Should().Be(0);
        tracker.WasReset.Should().BeTrue();
        config.WasDeleted.Should().BeTrue();
        tee.LogsWereDeleted.Should().BeTrue();
        console.Output.Should().Contain("All dtk state removed");
    }

    [Fact]
    public async Task ExecuteAsync_AllFlag_NoForce_UserDenies_DoesNotReset()
    {
        var (tracker, config, tee) = CreateStubs();
        var console = new TestConsole();
        console.Input.PushTextWithEnter("n");
        var command = CreateCommand(tracker, console, config, tee);

        var exitCode = await command.RunAsync(new ResetCommandSettings
        {
            Force = false,
            All = true
        }, CancellationToken.None);

        exitCode.Should().Be(0);
        tracker.WasReset.Should().BeFalse();
        config.WasDeleted.Should().BeFalse();
        tee.LogsWereDeleted.Should().BeFalse();
    }

    [Fact]
    public async Task ExecuteAsync_AllFlag_Prompt_MentionsConfigAndLogs()
    {
        var (tracker, config, tee) = CreateStubs();
        var console = new TestConsole();
        console.Input.PushTextWithEnter("n");
        var command = CreateCommand(tracker, console, config, tee);

        await command.RunAsync(new ResetCommandSettings
        {
            Force = false,
            All = true
        }, CancellationToken.None);

        console.Output.Should().Contain("configuration file");
    }

    private static (StubTracker tracker, StubConfigProvider config, StubTeeService tee) CreateStubs()
    {
        return (new StubTracker(), new StubConfigProvider(), new StubTeeService());
    }

    private static ResetCommand CreateCommand(
        StubTracker tracker,
        TestConsole console,
        StubConfigProvider? config = null,
        StubTeeService? tee = null)
    {
        config ??= new StubConfigProvider();
        tee ??= new StubTeeService();
        return new ResetCommand(
            new ResetTrackingUseCase(tracker),
            new FullResetUseCase(tracker, config, tee),
            console);
    }

    private sealed class StubTracker : ITracker
    {
        public bool WasReset { get; private set; }

        public Task RecordAsync(CommandRecord record, CancellationToken cancellationToken = default)
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
            WasReset = true;
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
        public bool WasDeleted { get; private set; }

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
            WasDeleted = true;
            return Task.CompletedTask;
        }
    }

    private sealed class StubTeeService : ITeeService
    {
        public bool LogsWereDeleted { get; private set; }

        public Task<string?> TeeAndHintAsync(string rawOutput, string commandSlug, TeeLogHeader header,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult<string?>(null);
        }

        public Task DeleteLogsAsync(CancellationToken cancellationToken = default)
        {
            LogsWereDeleted = true;
            return Task.CompletedTask;
        }

        public Task<ITeeSession> BeginAsync(
            string commandSlug, TeeLogHeader provisional, CancellationToken cancellationToken = default)
        {
            return Task.FromResult<ITeeSession>(NullTeeSession.Instance);
        }
    }
}
