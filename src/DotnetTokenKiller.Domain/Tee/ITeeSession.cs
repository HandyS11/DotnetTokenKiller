namespace DotnetTokenKiller.Domain.Tee;

/// <summary>
/// A tee log being written while its run is still in progress.
/// </summary>
/// <remarks>
/// The log exists on disk from the moment the session is created, which is what makes it survive a
/// dtk process that is killed rather than cancelled — dtk's cancellation token never fires in
/// production, so no handler-based approach can offer the same guarantee.
/// </remarks>
public interface ITeeSession : IAsyncDisposable
{
    /// <summary>
    /// Receives the run's output as it arrives. Never throws: a failing sink would otherwise
    /// propagate out of the output pump and fail the user's command.
    /// </summary>
    TextWriter Writer { get; }

    /// <summary>
    /// Records the exit code and applies the retention rules to the older logs this run supersedes.
    /// </summary>
    /// <param name="exitCode">The producing command's exit code.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// A hint naming the log file, or <see langword="null"/> when no log was kept: tee is off, or
    /// writing it failed. The latest run of each kind is always kept, whatever its exit code.
    /// </returns>
    Task<string?> FinalizeAsync(int exitCode, CancellationToken cancellationToken = default);
}
