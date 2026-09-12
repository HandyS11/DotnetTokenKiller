using DotnetTokenKiller.Domain.Tracking;

namespace DotnetTokenKiller.Benchmarks.Support;

/// <summary>
/// A tracker that records nothing, so the pipeline benchmark can separate filtering and token
/// counting from database cost. SQLite is measured on its own in TrackerReadBenchmarks and
/// TrackerWriteBenchmarks.
/// </summary>
internal sealed class NullTracker : ITracker
{
    public Task RecordAsync(CommandRecord record, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task<GainSummary> GetSummaryAsync(
        int days, string? projectPath, string? commandFilter = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The benchmark tracker only records.");

    public Task<IReadOnlyList<CommandRecord>> GetHistoryAsync(
        int days, string? projectPath, string? commandFilter = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The benchmark tracker only records.");

    public Task<CoverageSummary> GetCoverageAsync(
        int days, string? projectPath, string? commandFilter = null,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException("The benchmark tracker only records.");

    public Task CleanupAsync(int retentionDays, CancellationToken cancellationToken = default) =>
        Task.CompletedTask;

    public Task ResetAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
}
