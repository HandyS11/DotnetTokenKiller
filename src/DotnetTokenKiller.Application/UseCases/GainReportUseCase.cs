using DotnetTokenKiller.Domain.Tracking;

namespace DotnetTokenKiller.Application.UseCases;

/// <summary>Retrieves aggregated token-savings analytics.</summary>
/// <param name="tracker">The tracker used to query saved data.</param>
public sealed class GainReportUseCase(ITracker tracker)
{
    /// <summary>Returns the gain summary for the given time window.</summary>
    /// <param name="days">Number of days of history to include.</param>
    /// <param name="projectPath">Optional project path filter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<GainSummary> GetSummaryAsync(
        int days,
        string? projectPath,
        CancellationToken cancellationToken = default)
    {
        return tracker.GetSummaryAsync(days, projectPath, cancellationToken);
    }

    /// <summary>Returns the raw command history for the given time window.</summary>
    /// <param name="days">Number of days of history to include.</param>
    /// <param name="projectPath">Optional project path filter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task<IReadOnlyList<CommandRecord>> GetHistoryAsync(
        int days,
        string? projectPath,
        CancellationToken cancellationToken = default)
    {
        return tracker.GetHistoryAsync(days, projectPath, cancellationToken);
    }
}
