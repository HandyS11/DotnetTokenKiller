namespace DotnetTokenKiller.Domain.Tee;

/// <summary>Persists command output to a file and optionally returns a hint message.</summary>
public interface ITeeService
{
    /// <summary>Writes the raw output to a tee file if configured, and returns a hint pointing to it.</summary>
    /// <param name="rawOutput">The raw command output to tee.</param>
    /// <param name="commandSlug">A short identifier for the command, used in the file name.</param>
    /// <param name="exitCode">The process exit code; used to decide whether to tee in "failures" mode.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<string?> TeeAndHintAsync(
        string rawOutput,
        string commandSlug,
        int exitCode,
        CancellationToken cancellationToken = default);

    /// <summary>Deletes all tee log files from the configured tee directory.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task DeleteLogsAsync(CancellationToken cancellationToken = default);
}
