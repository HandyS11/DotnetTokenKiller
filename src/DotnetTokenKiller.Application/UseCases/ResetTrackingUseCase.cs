using DotnetTokenKiller.Domain.Tracking;

namespace DotnetTokenKiller.Application.UseCases;

public sealed class ResetTrackingUseCase(ITracker tracker)
{
    public Task ResetAsync(CancellationToken cancellationToken = default)
    {
        return tracker.ResetAsync(cancellationToken);
    }
}
