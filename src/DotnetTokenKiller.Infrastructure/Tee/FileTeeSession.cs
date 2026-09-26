using System.Diagnostics.CodeAnalysis;
using System.Text;
using DotnetTokenKiller.Domain.Tee;
using DotnetTokenKiller.Domain.Text;

namespace DotnetTokenKiller.Infrastructure.Tee;

/// <summary>A tee log written incrementally to an open file for the duration of one run.</summary>
/// <remarks>
/// Public rather than internal because <c>DotnetTokenKiller.Infrastructure</c> grants no
/// <c>InternalsVisibleTo</c> and this type carries the durability guarantee its tests exist to prove.
/// </remarks>
/// <param name="stream">
/// The open log file, positioned at the end of the header. Opened for both read and write so
/// <see cref="FinalizeAsync"/> can read back the status region before overwriting it.
/// </param>
/// <param name="filePath">The log's path, used for the hint and for deletion.</param>
/// <param name="statusRegionOffset">Byte offset of the status/exit region within the file.</param>
/// <param name="statusRegionLength">Byte length of that region.</param>
/// <param name="policy">The size and retention rules to apply when the run completes.</param>
public sealed class FileTeeSession(
    FileStream stream,
    string filePath,
    long statusRegionOffset,
    int statusRegionLength,
    TeeSessionPolicy policy) : ITeeSession
{
    private readonly SessionWriter _writer = new(stream, policy.MaxBodyBytes);
    private bool _disposed;

    /// <inheritdoc/>
    public TextWriter Writer => _writer;

    /// <inheritdoc/>
    public async Task<string?> FinalizeAsync(int exitCode, CancellationToken cancellationToken = default)
    {
        if (_disposed)
        {
            return null;
        }

        try
        {
            // Latch first: any WriteLineAsync/FlushAsync call that has not yet reached the gate
            // below observes this and returns immediately, instead of racing to append once the
            // header rewrite has repositioned the stream. Calls already inside the gate are waited
            // out by RunExclusivelyAsync rather than raced with.
            _writer.MarkBroken();
            return await _writer.RunExclusivelyAsync(FinalizeCoreAsync, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Intentional: tee errors must never surface to the user (but cancellation must propagate)
            await DisposeAsync().ConfigureAwait(false);
            return null;
        }

        async Task<string?> FinalizeCoreAsync(CancellationToken ct)
        {
            await stream.FlushAsync(ct).ConfigureAwait(false);

            // A truncated body bypasses the MinBodyBytes floor: hitting the byte cap already proves
            // the run produced at least that many bytes of output (in practice, likely far more —
            // the child kept producing output after the point the cap was reached), which is exactly
            // the kind of run the floor exists to keep, not the one-line "Restored." it exists to
            // drop. Without this, a cap configured below the floor (itself clamped to the cap in
            // FileTeeService.BeginAsync so a *plain* small-but-real log can still pass it) discarded
            // every truncated log outright — the marker line the writer just spent a whole cap's
            // worth of effort reserving room for would never survive to be read.
            if ((policy.KeepOnlyOnFailure && exitCode == 0) ||
                (_writer.BodyBytesWritten < policy.MinBodyBytes && !_writer.Truncated))
            {
                await DisposeAsync().ConfigureAwait(false);
                File.Delete(filePath);

                // This run's own log is already gone, so it cannot claim a phantom slot here — this
                // only trims leftovers (abandoned "Running" logs from runs killed mid-flight, or
                // logs from a previous read-back-guard failure below) that would otherwise never be
                // rotated on a config where every run in Failures mode happens to succeed.
                TryRotateFiles();
                return null;
            }

            // Read back the region rather than trusting statusRegionOffset: both renderings are
            // fixed-width ASCII, so a length check can never fail, but the offset itself is a
            // UTF-8 byte count into a header that can contain non-ASCII text (a command line or
            // cwd). A caller that measured it in UTF-16 chars instead would otherwise overwrite the
            // header and the delimiter with no warning.
            var expected = Encoding.UTF8.GetBytes(TeeLogHeader.RenderStatusAndExit(null));
            var actual = new byte[statusRegionLength];
            stream.Seek(statusRegionOffset, SeekOrigin.Begin);
            await stream.ReadExactlyAsync(actual, ct).ConfigureAwait(false);
            if (!actual.AsSpan().SequenceEqual(expected))
            {
                await DisposeAsync().ConfigureAwait(false);
                return null;
            }

            var region = Encoding.UTF8.GetBytes(TeeLogHeader.RenderStatusAndExit(exitCode));
            stream.Seek(statusRegionOffset, SeekOrigin.Begin);
            await stream.WriteAsync(region, ct).ConfigureAwait(false);
            await stream.FlushAsync(ct).ConfigureAwait(false);
            await DisposeAsync().ConfigureAwait(false);

            // The hint is computed before rotation runs, and TryRotateFiles can never throw, so a
            // rotation failure can never cost this kept log its hint (see TryRotateFiles).
            var hint = $"[full output: {filePath}]";

            // Rotation runs here, on the keep path, rather than when the session was opened: at
            // open time it is not yet known whether this run's log will be kept at all, and
            // rotating before that decision would let a run destined for deletion (a success in
            // Failures mode, or a body under the guard) evict an older log it was never going to
            // replace. The stream is closed first so a file this call decides to delete cannot
            // still be open on platforms that refuse to delete an in-use handle.
            TryRotateFiles();
            return hint;
        }

        void TryRotateFiles()
        {
            try
            {
                FileTeeService.RotateFiles(policy.TeeDir, policy.MaxFiles);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Intentional: rotation is best-effort housekeeping, not part of this run's outcome.
                // File.Delete inside RotateFiles can throw IOException — reachable on Windows, where
                // FileTeeService opens log files without FileShare.Delete, so a concurrently running
                // dtk process's own open log cannot be unlinked here. Left uncaught, this would
                // propagate to FinalizeAsync's outer catch, which returns null — silently costing
                // the user the "[full output: …]" hint (or, on the discard path, no hint to lose,
                // but still an unrelated failure) even though the log itself was written and, on the
                // keep path, already correctly kept.
            }
        }
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _writer.MarkBroken();
        try
        {
            await _writer.DisposeAsync().ConfigureAwait(false);
            await stream.DisposeAsync().ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Intentional: disposal is best-effort cleanup, not part of the run's outcome. Closing
            // the FileStream re-flushes whatever is still buffered, so the very IO failure that sent
            // FinalizeAsync's catch clause here (disk full, IO error, removed volume) can throw
            // again on close; left uncaught, that second throw would escape FinalizeAsync entirely
            // and, on the passthrough path, cost the caller the child's real exit code.
        }
    }

    /// <summary>
    /// The sink handed to the output pump: strips ANSI, enforces the byte budget, serialises the
    /// two concurrent pumps, and swallows IO failures so a broken log cannot fail the run.
    /// </summary>
    /// <param name="stream">The open log file to append to.</param>
    /// <param name="maxBodyBytes">The body's byte budget; writes stop once it is reached.</param>
    private sealed class SessionWriter(FileStream stream, long maxBodyBytes) : TextWriter
    {
        // Never disposed: a SemaphoreSlim whose AvailableWaitHandle is never touched needs no
        // disposal, and disposing it was the cause of an ObjectDisposedException that could
        // otherwise escape WriteLineAsync from a pump parked in _gate.WaitAsync.
#pragma warning disable CA2213 // Disposable fields should be disposed — intentional, see above.
        private readonly SemaphoreSlim _gate = new(1, 1);
#pragma warning restore CA2213

        /// <summary>
        /// The marker's bytes (with a leading line feed baked in — see
        /// <see cref="RecordTruncationAsync"/>), the content budget left once room for it is set
        /// aside, and whether it fits within the cap at all — computed once, together, from
        /// <c>maxBodyBytes</c>. A single tuple field rather than three separate ones: C# forbids a
        /// field initializer from reading another instance field (only <see langword="static"/>
        /// members and the primary constructor's own parameters are in scope at that point), so
        /// three independent initializers would each have had to re-render and re-measure the marker
        /// text.
        /// </summary>
        private readonly (byte[] MarkerBytes, long ContentBudget, bool MarkerFits) _cap = ComputeCap(maxBodyBytes);

        /// <summary>
        /// Written from both pump threads and from <c>FinalizeAsync</c>; volatile so a check on one
        /// thread cannot be reordered ahead of a <see cref="MarkBroken"/> call that happened-before
        /// it on another.
        /// </summary>
        private volatile bool _broken;

        /// <summary>
        /// Whether the byte most recently written to the stream was a line feed — i.e. whether the
        /// marker can start its own line without help. True vacuously when nothing has been written
        /// yet. Only a mid-line cut (see <see cref="WriteLineAsync"/>) ever leaves this false: a
        /// whole line always ends with the "\n" <c>WriteLineAsync</c> appends before measuring it.
        /// </summary>
        private bool _bodyEndsWithNewline = true;

        /// <summary>
        /// Whether the byte cap has been hit. Also gates the marker write (or, on a cap too small
        /// even for the marker, nothing) so it happens exactly once no matter how many further lines
        /// the pump offers. Deliberately set only after that write succeeds; see
        /// <see cref="RecordTruncationAsync"/>.
        /// </summary>
        public bool Truncated { get; private set; }

        public long BodyBytesWritten { get; private set; }

        public override Encoding Encoding => Encoding.UTF8;

        /// <summary>
        /// Forced to a line feed so the log matches the repo's LF-only policy, and so the text the
        /// pump accumulates for the filter is identical on every platform.
        /// </summary>
        [AllowNull]
        public override string NewLine
        {
            get => "\n";
            set => _ = value;
        }

        public void MarkBroken() => _broken = true;

        /// <summary>
        /// Runs <paramref name="action"/> while holding the gate that serialises
        /// <see cref="WriteLineAsync"/> and <see cref="FlushAsync(CancellationToken)"/>, so
        /// finalizing can flush, read back, and overwrite the status region without racing a pump
        /// write that is already past the gate.
        /// </summary>
        /// <typeparam name="T">The action's result type.</typeparam>
        /// <param name="action">The work to run exclusively of the two pumps.</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>The action's result.</returns>
        public async Task<T> RunExclusivelyAsync<T>(
            Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await action(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                _gate.Release();
            }
        }

        public override async Task WriteLineAsync(
            ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default)
        {
            if (_broken)
            {
                return;
            }

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (Truncated)
                {
                    // The cap was already dealt with — nothing more can ever be appended.
                    return;
                }

                var remaining = _cap.ContentBudget - BodyBytesWritten;
                if (remaining <= 0)
                {
                    // The previous line used up the last of the content budget and there is more to
                    // write — this call is proof some output is genuinely being dropped now.
                    await RecordTruncationAsync(cancellationToken).ConfigureAwait(false);
                    return;
                }

                var stripped = AnsiStrip.Strip(buffer.ToString()) + "\n";
                if (Encoding.UTF8.GetByteCount(stripped) > remaining)
                {
                    // This line itself has to be cut short: whatever text follows the cut is lost
                    // right now, so the marker belongs immediately after it rather than waiting for
                    // a next call that may never come. TruncateToUtf8Bytes cuts on a rune boundary
                    // strictly before the budget, and the needed byte count here always exceeds
                    // remaining by at least the trailing "\n" alone, so the cut can include every
                    // content rune but never that final "\n" — checked directly below rather than
                    // assumed, since the marker gluing onto this line instead of starting its own is
                    // exactly the bug this tracking exists to prevent.
                    var truncatedLine = Utf8Text.TruncateToUtf8Bytes(stripped, remaining);
                    var truncatedBytes = Encoding.UTF8.GetBytes(truncatedLine);
                    await stream.WriteAsync(truncatedBytes, cancellationToken).ConfigureAwait(false);
                    BodyBytesWritten += truncatedBytes.Length;

                    // A cut that kept nothing (the first rune alone exceeds remaining) wrote no
                    // byte, so the body still ends however the previous write left it.
                    if (truncatedBytes.Length > 0)
                    {
                        _bodyEndsWithNewline = truncatedLine.EndsWith('\n');
                    }
                    await RecordTruncationAsync(cancellationToken).ConfigureAwait(false);
                    return;
                }

                var bytes = Encoding.UTF8.GetBytes(stripped);
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                BodyBytesWritten += bytes.Length;
                _bodyEndsWithNewline = true; // stripped always ends with the "\n" appended above.

                // The line landed exactly on the content budget with nothing cut. Whether that was
                // truly the last line or more was about to follow is unknowable here — recording
                // truncation now would risk a false positive on output that just happened to fit
                // exactly, so it waits for the "remaining <= 0" branch above to decide, on the next
                // call, whether there really was more.
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Intentional: a throwing sink would propagate out of the output pump and fail the
                // user's command, which is the one way streaming can break "tee errors never surface".
                _broken = true;
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>
        /// Records that the byte cap has been hit: appends the truncation marker, when it fits at
        /// all. Called only once per session, from within the gate <see cref="WriteLineAsync"/>
        /// already holds.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token.</param>
        private async Task RecordTruncationAsync(CancellationToken cancellationToken)
        {
            if (_cap.MarkerFits)
            {
                // _cap.MarkerBytes has a leading "\n" baked in for the mid-line-cut case, where the
                // byte just written is not itself a "\n" and the marker would otherwise glue onto the
                // end of that content instead of starting its own line — silently breaking
                // TeeTruncationMarker.IsMarkerLine's anchored match against the last line. Skipped
                // when the preceding write already ended with "\n" on its own (a whole line, or
                // nothing written yet), so the body never gains a spurious blank line either.
                // Guaranteed to fit either way: _cap.ContentBudget was sized to leave exactly this
                // much room, leading "\n" included.
                var offset = _bodyEndsWithNewline ? 1 : 0;
                var markerBytes = _cap.MarkerBytes.AsMemory(offset);
                await stream.WriteAsync(markerBytes, cancellationToken).ConfigureAwait(false);
                BodyBytesWritten += markerBytes.Length;
            }

            // Set only after the write above (if any) actually completed, not before it: Task 9
            // wires Ctrl+C cancellation through this same writer, and a cancellation that throws out
            // of WriteAsync would otherwise leave Truncated permanently true with the
            // marker never actually on the stream — every future call short-circuits on it above, so
            // the marker would be lost for good rather than retried. A cancellation during the flush
            // immediately below is accepted as a narrower risk: the marker bytes are already handed
            // to the stream by that point, just not yet guaranteed durable on disk.
            Truncated = true;

            // Durable immediately, not deferred to FinalizeAsync: a run killed right after this
            // point (SIGKILL, a tool-call timeout) must still leave a log whose body says it was
            // truncated, the same durability guarantee AbandonedSession already relies on for the
            // running/complete status.
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Computes the marker bytes and, from their length, the content budget and whether the
        /// marker fits within the cap at all.
        /// </summary>
        /// <param name="maxBodyBytes">The body's byte budget.</param>
        /// <returns>
        /// The marker's UTF-8 bytes, with a leading line feed baked in unconditionally (skipped at
        /// the write site — see <see cref="RecordTruncationAsync"/> — when it turns out not to be
        /// needed, which just leaves the body a byte under the cap rather than risking it a byte
        /// over) plus a trailing one; the byte budget left for real content once room for the whole
        /// of that is set aside — the full cap, unreduced, when the marker cannot fit at all even
        /// with the leading line feed, since reserving room for a marker that will never be written
        /// would only discard content for nothing; and whether the marker fits within the cap.
        /// </returns>
        private static (byte[] MarkerBytes, long ContentBudget, bool MarkerFits) ComputeCap(long maxBodyBytes)
        {
            var markerBytes = Encoding.UTF8.GetBytes("\n" + TeeTruncationMarker.Render(maxBodyBytes) + "\n");
            var markerFits = markerBytes.Length <= maxBodyBytes;
            var contentBudget = markerFits ? Math.Max(0, maxBodyBytes - markerBytes.Length) : maxBodyBytes;
            return (markerBytes, contentBudget, markerFits);
        }

        public override async Task FlushAsync(CancellationToken cancellationToken)
        {
            if (_broken)
            {
                return;
            }

            // Gated for the same reason as WriteLineAsync: production flushes per line from both
            // pumps, and FileStream.WriteAsync/FlushAsync on one instance are not thread-safe
            // against each other.
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Intentional: see WriteLineAsync.
                _broken = true;
            }
            finally
            {
                _gate.Release();
            }
        }

        public override Task FlushAsync() => FlushAsync(CancellationToken.None);

        public override void Write(char value)
        {
            // The pump only ever calls WriteLineAsync. Implemented because TextWriter requires it;
            // routing it through the async path would deadlock.
        }
    }
}
