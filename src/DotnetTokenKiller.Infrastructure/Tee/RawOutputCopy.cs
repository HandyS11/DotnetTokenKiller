using System.Diagnostics.CodeAnalysis;
using System.Text;
using DotnetTokenKiller.Domain.Tee;
using DotnetTokenKiller.Domain.Text;

namespace DotnetTokenKiller.Infrastructure.Tee;

/// <summary>The copy of a run's raw output that <c>--tee &lt;path&gt;</c> writes, over an open stream it owns.</summary>
/// <remarks>
/// Public rather than internal because <c>DotnetTokenKiller.Infrastructure</c>'s tests build one over a stream
/// they break, which <see cref="RawOutputCopyFactory"/> gives them no way to do.
/// </remarks>
public sealed class RawOutputCopy : IRawOutputCopy
{
    private readonly CopyWriter _writer;

    /// <summary>Initializes a new instance.</summary>
    /// <param name="stream">The open, writable file, closed when this copy is disposed.</param>
    public RawOutputCopy(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        _writer = new CopyWriter(stream);
    }

    /// <inheritdoc/>
    public TextWriter Writer => _writer;

    /// <inheritdoc/>
    public string? Failure => _writer.Failure;

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => _writer.DisposeAsync();

    /// <summary>
    /// The sink handed to the output pumps: strips ANSI, serialises the two concurrent pumps, and keeps the
    /// first IO failure instead of throwing it, dropping every write after it.
    /// </summary>
    /// <param name="stream">The open file to append to.</param>
    private sealed class CopyWriter(Stream stream) : TextWriter
    {
        // Never disposed, as in FileTeeSession's writer: a SemaphoreSlim whose AvailableWaitHandle is never
        // touched needs no disposal, and disposing it could throw ObjectDisposedException into a parked pump.
#pragma warning disable CA2213 // Disposable fields should be disposed — intentional, see above.
        private readonly SemaphoreSlim _gate = new(1, 1);
#pragma warning restore CA2213

        /// <summary>Set inside the gate, read by the run once both pumps have finished.</summary>
        private volatile string? _failure;

        /// <summary>Whether the stream is closed; read and written only inside the gate.</summary>
        private bool _closed;

        public string? Failure => _failure;

        public override Encoding Encoding => Encoding.UTF8;

        /// <summary>Forced to a line feed, as in the tee log, so the copy is LF-only on every platform.</summary>
        [AllowNull]
        public override string NewLine
        {
            get => "\n";
            set => _ = value;
        }

        public override async Task WriteLineAsync(
            ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default)
        {
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_closed || _failure is not null)
                {
                    return;
                }

                var bytes = Encoding.UTF8.GetBytes(AnsiStrip.Strip(buffer.ToString()) + "\n");
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Intentional: a throwing sink would propagate out of the output pump and fail the user's
                // command; the run reports the failure in one line instead.
                _failure = ex.Message;
            }
            finally
            {
                _gate.Release();
            }
        }

        public override async Task FlushAsync(CancellationToken cancellationToken)
        {
            // Gated: the pumps flush after every line, from two threads, and a stream's writes and flushes are
            // not thread-safe against each other.
            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                if (_closed || _failure is not null)
                {
                    return;
                }

                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Intentional: see WriteLineAsync.
                _failure = ex.Message;
            }
            finally
            {
                _gate.Release();
            }
        }

        public override Task FlushAsync() => FlushAsync(CancellationToken.None);

        public override void Write(char value)
        {
            // The pumps only ever call WriteLineAsync. Implemented because TextWriter requires it; routing it
            // through the async path would deadlock.
        }

        /// <summary>Flushes and closes the stream, once, keeping a failure to do so like any other.</summary>
        public override async ValueTask DisposeAsync()
        {
            // None: the run's own token may already be cancelled, and closing the file is the cleanup it allows.
            await _gate.WaitAsync(CancellationToken.None).ConfigureAwait(false);
            try
            {
                if (!_closed)
                {
                    _closed = true;
                    await FlushAndCloseAsync().ConfigureAwait(false);
                }
            }
            finally
            {
                _gate.Release();
            }

            await base.DisposeAsync().ConfigureAwait(false);
        }

        private async Task FlushAndCloseAsync()
        {
            try
            {
                if (_failure is null)
                {
                    await stream.FlushAsync(CancellationToken.None).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A full disk can refuse the last buffered bytes only now; the run still reports it.
                _failure = ex.Message;
            }

            try
            {
                await stream.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Closing re-flushes whatever is still buffered, so the failure that stopped the writes (or the
                // flush above) can throw again here; the first one is the one kept.
                _failure ??= ex.Message;
            }
        }
    }
}
