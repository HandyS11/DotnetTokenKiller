using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Cli.Commands;
using DotnetTokenKiller.Cli.Commands.Settings;
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
        var tracker = new StubTracker();
        var console = new TestConsole();
        var command = new ResetCommand(new ResetTrackingUseCase(tracker), console);

        var exitCode = await command.ExecuteAsync(null!, new ResetCommandSettings { Force = true }, CancellationToken.None);

        exitCode.Should().Be(0);
        tracker.WasReset.Should().BeTrue();
        console.Output.Should().Contain("cleared");
    }

    [Fact]
    public async Task ExecuteAsync_NoForce_UserDenies_DoesNotReset()
    {
        var tracker = new StubTracker();
        var console = new TestConsole();
        console.Input.PushTextWithEnter("n");
        var command = new ResetCommand(new ResetTrackingUseCase(tracker), console);

        var exitCode = await command.ExecuteAsync(null!, new ResetCommandSettings { Force = false }, CancellationToken.None);

        exitCode.Should().Be(0);
        tracker.WasReset.Should().BeFalse();
        console.Output.Should().Contain("Aborted");
    }

    private sealed class StubTracker : ITracker
    {
        public bool WasReset { get; private set; }

        public Task RecordAsync(CommandRecord record, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<GainSummary> GetSummaryAsync(int days, string? projectPath, CancellationToken cancellationToken = default) =>
            Task.FromResult(new GainSummary(0, 0, 0, 0, 0.0, new Dictionary<string, CommandGainDetail>(StringComparer.Ordinal)));

        public Task<IReadOnlyList<CommandRecord>> GetHistoryAsync(int days, string? projectPath, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<CommandRecord>>(Array.Empty<CommandRecord>());

        public Task CleanupAsync(int retentionDays, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task ResetAsync(CancellationToken cancellationToken = default)
        {
            WasReset = true;
            return Task.CompletedTask;
        }
    }
}
