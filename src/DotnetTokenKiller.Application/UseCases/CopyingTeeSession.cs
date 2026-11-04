using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Domain.Tee;

namespace DotnetTokenKiller.Application.UseCases;

/// <summary>A tee session whose output also goes to the <c>--tee &lt;path&gt;</c> copy.</summary>
/// <remarks>
/// <para>
/// Each line goes to <paramref name="inner"/>'s writer, then to the copy's; finalizing and disposing are the inner
/// session's alone. The copy belongs to the run, which closes it once the child has exited, so a failure closing
/// it is known before the output is built.
/// </para>
/// <para>
/// Both writers are safe for the two output pumps on their own, and neither throws (cancellation aside), so the
/// pair needs no lock of its own. Two lines arriving at once can therefore reach the log and the copy in
/// different orders; each file is still in the order its own writer received them.
/// </para>
/// </remarks>
/// <param name="inner">The run's tee session; a <see cref="NullTeeSession"/> when tee is off.</param>
/// <param name="copy">The open copy.</param>
internal sealed class CopyingTeeSession(ITeeSession inner, IRawOutputCopy copy) : ITeeSession
{
    // The copy is secondary: FanOutTextWriter keeps the inner writer's NewLine, which the output pump uses
    // to rebuild the raw text the filter sees, so adding a copy cannot change what is filtered.
#pragma warning disable CA2213 // Disposable fields should be disposed — it owns nothing: both writers belong to others.
    private readonly FanOutTextWriter _writer = new(inner.Writer, copy.Writer);
#pragma warning restore CA2213

    /// <inheritdoc/>
    public TextWriter Writer => _writer;

    /// <summary>Wraps <paramref name="session"/> when there is a copy to write, and returns it unchanged otherwise.</summary>
    /// <param name="session">The run's tee session.</param>
    /// <param name="copy">The open copy, or <see langword="null"/> when no <c>--tee</c> path was given.</param>
    public static ITeeSession Wrap(ITeeSession session, IRawOutputCopy? copy) =>
        copy is null ? session : new CopyingTeeSession(session, copy);

    /// <inheritdoc/>
    public Task<string?> FinalizeAsync(int exitCode, CancellationToken cancellationToken = default) =>
        inner.FinalizeAsync(exitCode, cancellationToken);

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => inner.DisposeAsync();
}
