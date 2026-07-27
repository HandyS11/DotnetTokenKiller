namespace DotnetTokenKiller.Domain.Tracking;

/// <summary>Persists and queries command tracking records.</summary>
public interface ITracker
{
    /// <summary>Records a command execution.</summary>
    /// <param name="record">The command record to persist.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RecordAsync(CommandRecord record, CancellationToken cancellationToken = default);

    /// <summary>Returns an aggregated gain summary for the given time window.</summary>
    /// <param name="days">Number of days of history to include.</param>
    /// <param name="projectPath">Optional project path filter.</param>
    /// <param name="commandFilter">Optional command name filter (e.g. "build", "test").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<GainSummary> GetSummaryAsync(
        int days,
        string? projectPath,
        string? commandFilter = null,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the raw command history for the given time window.</summary>
    /// <param name="days">Number of days of history to include.</param>
    /// <param name="projectPath">Optional project path filter.</param>
    /// <param name="commandFilter">Optional command name filter (e.g. "build", "test").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<CommandRecord>> GetHistoryAsync(
        int days,
        string? projectPath,
        string? commandFilter = null,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the filtering-coverage breakdown for the given time window.</summary>
    /// <param name="days">Number of days of history to include.</param>
    /// <param name="projectPath">Optional project path filter.</param>
    /// <param name="commandFilter">Optional command name filter (e.g. "publish").</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<CoverageSummary> GetCoverageAsync(
        int days,
        string? projectPath,
        string? commandFilter = null,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes records older than the retention period.</summary>
    /// <param name="retentionDays">Number of days to retain.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task CleanupAsync(int retentionDays, CancellationToken cancellationToken = default);

    /// <summary>Deletes all tracking records.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task ResetAsync(CancellationToken cancellationToken = default);
}
