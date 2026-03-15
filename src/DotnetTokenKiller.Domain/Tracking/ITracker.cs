namespace DotnetTokenKiller.Domain.Tracking;

public interface ITracker
{
    Task RecordAsync(CommandRecord record, CancellationToken cancellationToken = default);

    Task<GainSummary> GetSummaryAsync(
        int days,
        string? projectPath,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CommandRecord>> GetHistoryAsync(
        int days,
        string? projectPath,
        CancellationToken cancellationToken = default);

    Task CleanupAsync(int retentionDays, CancellationToken cancellationToken = default);

    Task ResetAsync(CancellationToken cancellationToken = default);
}
