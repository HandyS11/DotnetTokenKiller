using DotnetTokenKiller.Domain.Execution;
using DotnetTokenKiller.Domain.Tee;

namespace DotnetTokenKiller.Application.UseCases;

/// <summary>What the run use cases do once the command they wrapped has been cancelled.</summary>
internal static class CancelledRun
{
    /// <summary>
    /// Finalizes the run's log with <see cref="ExitCodes.Cancelled"/>, swallowing any failure so the
    /// cancellation, not a tee error, is what reaches the caller.
    /// </summary>
    /// <remarks>
    /// <see cref="CancellationToken.None"/>, not the run's token: that token is already cancelled, and
    /// finalizing is the cleanup the cancellation exists to allow.
    /// </remarks>
    /// <param name="session">The session to finalize.</param>
    internal static async Task FinalizeTeeAsync(ITeeSession session)
    {
        try
        {
            await session.FinalizeAsync(ExitCodes.Cancelled, CancellationToken.None).ConfigureAwait(false);
        }
        catch
        {
            // Intentional: tee errors must not surface to the user
        }
    }
}
