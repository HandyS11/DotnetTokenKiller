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
/// <param name="stream">The open log file, positioned at the end of the header.</param>
/// <param name="filePath">The log's path, used for the hint and for deletion.</param>
/// <param name="statusRegionOffset">Byte offset of the status/exit region within the file.</param>
/// <param name="statusRegionLength">Byte length of that region.</param>
/// <param name="maxBodyBytes">The body's byte budget; appends stop once it is reached.</param>
/// <param name="minBodyBytes">Bodies smaller than this are discarded when the run completes.</param>
/// <param name="keepOnlyOnFailure">Whether a successful run's log is discarded.</param>
public sealed class FileTeeSession(
    FileStream stream,
    string filePath,
    long statusRegionOffset,
    int statusRegionLength,
    long maxBodyBytes,
    long minBodyBytes,
    bool keepOnlyOnFailure) : ITeeSession
{
    private readonly SessionWriter _writer = new(stream, maxBodyBytes);
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
            await _writer.FlushAsync(cancellationToken).ConfigureAwait(false);

            if ((keepOnlyOnFailure && exitCode == 0) || _writer.BodyBytesWritten < minBodyBytes)
            {
                await DisposeAsync().ConfigureAwait(false);
                File.Delete(filePath);
                return null;
            }

            var region = Encoding.UTF8.GetBytes(TeeLogHeader.RenderStatusAndExit(exitCode));
            if (region.Length != statusRegionLength)
            {
                // Unreachable while RenderStatusAndExit pads to fixed widths; writing a
                // differently-sized region here would overwrite the delimiter and make every
                // completed log unparseable, so refuse rather than corrupt.
                await DisposeAsync().ConfigureAwait(false);
                return null;
            }

            stream.Seek(statusRegionOffset, SeekOrigin.Begin);
            await stream.WriteAsync(region, cancellationToken).ConfigureAwait(false);
            await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            await DisposeAsync().ConfigureAwait(false);
            return $"[full output: {filePath}]";
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Intentional: tee errors must never surface to the user (but cancellation must propagate)
            await DisposeAsync().ConfigureAwait(false);
            return null;
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
        await _writer.DisposeAsync().ConfigureAwait(false);
        await stream.DisposeAsync().ConfigureAwait(false);
    }

    /// <summary>
    /// The sink handed to the output pump: strips ANSI, enforces the byte budget, serialises the
    /// two concurrent pumps, and swallows IO failures so a broken log cannot fail the run.
    /// </summary>
    /// <param name="stream">The open log file to append to.</param>
    /// <param name="maxBodyBytes">The body's byte budget; writes stop once it is reached.</param>
    private sealed class SessionWriter(FileStream stream, long maxBodyBytes) : TextWriter
    {
        private readonly SemaphoreSlim _gate = new(1, 1);
        private bool _broken;

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
                var remaining = maxBodyBytes - BodyBytesWritten;
                if (remaining <= 0)
                {
                    return;
                }

                var line = Utf8Text.TruncateToUtf8Bytes(
                    AnsiStrip.Strip(buffer.ToString()) + "\n", remaining);
                var bytes = Encoding.UTF8.GetBytes(line);
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                BodyBytesWritten += bytes.Length;
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

        public override async Task FlushAsync(CancellationToken cancellationToken)
        {
            if (_broken)
            {
                return;
            }

            try
            {
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Intentional: see WriteLineAsync.
                _broken = true;
            }
        }

        public override Task FlushAsync() => FlushAsync(CancellationToken.None);

        public override void Write(char value)
        {
            // The pump only ever calls WriteLineAsync. Implemented because TextWriter requires it;
            // routing it through the async path would deadlock.
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _gate.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
