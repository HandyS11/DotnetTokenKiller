namespace DotnetTokenKiller.Domain.Tee;

/// <summary>Persists command output to a file and optionally returns a hint message.</summary>
public interface ITeeService
{
    /// <summary>Deletes all tee log files from the configured tee directory.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteLogsAsync(CancellationToken cancellationToken = default);

    /// <summary>Opens a log for a run that is about to start.</summary>
    /// <param name="commandSlug">A short identifier for the command, used in the file name.</param>
    /// <param name="provisional">
    /// The header to write immediately. Its exit code is ignored and normalized to
    /// <see langword="null"/> before writing, since the run has not finished yet; the real one is
    /// supplied to <see cref="ITeeSession.FinalizeAsync"/> when the run ends.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The session, or a <see cref="NullTeeSession"/> when tee is off or the log could not be
    /// opened. Never null, and never throws.
    /// </returns>
    Task<ITeeSession> BeginAsync(
        string commandSlug,
        TeeLogHeader provisional,
        CancellationToken cancellationToken = default);
}
