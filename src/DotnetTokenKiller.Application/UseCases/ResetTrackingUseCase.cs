using DotnetTokenKiller.Domain.Tracking;

namespace DotnetTokenKiller.Application.UseCases;

/// <summary>Clears all saved tracking data.</summary>
/// <param name="tracker">The tracker whose data will be cleared.</param>
public sealed class ResetTrackingUseCase(ITracker tracker)
{
    /// <summary>Deletes all tracking records.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public Task ResetAsync(CancellationToken cancellationToken = default)
    {
        return tracker.ResetAsync(cancellationToken);
    }
}
