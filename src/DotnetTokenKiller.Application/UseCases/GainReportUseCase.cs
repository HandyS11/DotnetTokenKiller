using DotnetTokenKiller.Domain.Tracking;

namespace DotnetTokenKiller.Application.UseCases;

public sealed class GainReportUseCase(ITracker tracker)
{
    public Task<GainSummary> GetSummaryAsync(
        int days,
        string? projectPath,
        CancellationToken cancellationToken = default)
    {
        return tracker.GetSummaryAsync(days, projectPath, cancellationToken);
    }
}
