using DotnetTokenKiller.Domain.Configuration;
using DotnetTokenKiller.Domain.Tee;
using DotnetTokenKiller.Domain.Tracking;

namespace DotnetTokenKiller.Application.UseCases;

/// <summary>Removes all dtk state: tracking data, tee logs, and the configuration file.</summary>
/// <param name="tracker">The tracker whose data will be cleared.</param>
/// <param name="configProvider">The configuration provider whose file will be deleted.</param>
/// <param name="teeService">The tee service whose logs will be deleted.</param>
public sealed class FullResetUseCase(
    ITracker tracker,
    IConfigProvider configProvider,
    ITeeService teeService)
{
    /// <summary>Clears tracking data, tee logs, and the configuration file.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task ResetAsync(CancellationToken cancellationToken = default)
    {
        await tracker.ResetAsync(cancellationToken).ConfigureAwait(false);
        await teeService.DeleteLogsAsync(cancellationToken).ConfigureAwait(false);
        await configProvider.DeleteAsync(cancellationToken).ConfigureAwait(false);
    }
}
