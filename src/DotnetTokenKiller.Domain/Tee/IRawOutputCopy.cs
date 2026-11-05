namespace DotnetTokenKiller.Domain.Tee;

/// <summary>
/// The copy of a run's raw output that <c>--tee &lt;path&gt;</c> asks for, being written while the run is in
/// progress: the same body the tee log gets, with no header and no size cap.
/// </summary>
/// <remarks>
/// A secondary writer: a failure writing it mid-run (a full disk) must neither fail the run nor cut its
/// filtered output, so it is recorded in <see cref="Failure"/> for the run to report in one line instead.
/// Disposing it flushes and closes the file; a failure doing so is recorded the same way.
/// </remarks>
public interface IRawOutputCopy : IAsyncDisposable
{
    /// <summary>
    /// Receives the run's output as it arrives, from the two output pumps at once. Strips ANSI escapes and
    /// never throws (cancellation aside): the first write or flush error is kept in <see cref="Failure"/>, and
    /// every later write is dropped.
    /// </summary>
    TextWriter Writer { get; }

    /// <summary>The reason the first write, flush or close failed, or <see langword="null"/> while none has.</summary>
    string? Failure { get; }
}
