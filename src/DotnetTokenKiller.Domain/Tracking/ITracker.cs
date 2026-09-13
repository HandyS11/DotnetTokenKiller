namespace DotnetTokenKiller.Domain.Tracking;

/// <summary>Persists and queries command tracking records.</summary>
public interface ITracker
{
    /// <summary>Records a command execution.</summary>
    /// <param name="record">The command record to persist.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task RecordAsync(CommandRecord record, CancellationToken cancellationToken = default);

    /// <summary>
    /// Performs the one-time setup the first call would otherwise do, so it can run early (in the
    /// background, while a child process runs) and leave only the record itself for later.
    /// </summary>
    /// <remarks>
    /// <see cref="WarmUpAsync"/> may run on a background thread concurrently with the other members
    /// and with disposal. A failed <see cref="WarmUpAsync"/> must not prevent a later call (such as
    /// <see cref="RecordAsync"/>) from attempting the setup again; implementations must not cache the
    /// failure.
    /// </remarks>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task WarmUpAsync(CancellationToken cancellationToken = default);

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
