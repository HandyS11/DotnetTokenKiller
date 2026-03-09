using DotnetTokenKiller.Domain.Tracking;

namespace DotnetTokenKiller.Infrastructure.Tracking;

public sealed class NullTracker : ITracker
{
    public Task RecordAsync(CommandRecord record, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<GainSummary> GetSummaryAsync(int days, string? projectPath, CancellationToken cancellationToken = default)
        => Task.FromResult(new GainSummary(0, 0, 0, 0, 0.0, new Dictionary<string, int>()));

    public Task<IReadOnlyList<CommandRecord>> GetHistoryAsync(int days, string? projectPath, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<CommandRecord>>([]);

    public Task CleanupAsync(int retentionDays, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
