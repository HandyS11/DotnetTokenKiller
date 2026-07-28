namespace DotnetTokenKiller.Domain.Tee;

/// <summary>Reads previously written tee logs back.</summary>
/// <remarks>
/// The read-side counterpart to <see cref="ITeeService"/>. Separate because writing happens on
/// every filtered run while reading happens only when <c>dtk log</c> is invoked, and because the
/// two have no shared state beyond the directory their implementations both resolve.
/// </remarks>
public interface ITeeLogStore
{
    /// <summary>Lists every tee log, newest first.</summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The discovered logs, or an empty list when the directory is absent or unreadable.</returns>
    Task<IReadOnlyList<TeeLogEntry>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>Reads one log's body, without its header.</summary>
    /// <param name="entry">An entry previously returned by <see cref="ListAsync"/>.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The raw output the log holds.</returns>
    Task<string> ReadBodyAsync(TeeLogEntry entry, CancellationToken cancellationToken = default);
}
