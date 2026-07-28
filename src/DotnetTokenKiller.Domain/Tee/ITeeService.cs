namespace DotnetTokenKiller.Domain.Tee;

/// <summary>Persists command output to a file and optionally returns a hint message.</summary>
public interface ITeeService
{
    /// <summary>Writes the raw output to a tee file if configured, and returns a hint pointing to it.</summary>
    /// <param name="rawOutput">The raw command output to tee.</param>
    /// <param name="commandSlug">A short identifier for the command, used in the file name.</param>
    /// <param name="header">
    /// The metadata block written ahead of the body. Its exit code decides whether to tee in
    /// "failures" mode, and its timestamp names the file.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<string?> TeeAndHintAsync(
        string rawOutput,
        string commandSlug,
        TeeLogHeader header,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes all tee log files from the configured tee directory.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteLogsAsync(CancellationToken cancellationToken = default);
}
