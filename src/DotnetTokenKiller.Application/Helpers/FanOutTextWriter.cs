using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace DotnetTokenKiller.Application.Helpers;

/// <summary>Forwards each line to two writers.</summary>
/// <remarks>
/// <para>
/// Used on the passthrough path, where output must reach the terminal the user is watching and the
/// tee log at the same time. The secondary is treated as expendable: a tee that fails must not cost
/// the user the output they were waiting for.
/// </para>
/// <para>
/// Only <see cref="WriteLineAsync(ReadOnlyMemory{char}, CancellationToken)"/> and
/// <see cref="FlushAsync(CancellationToken)"/> fan out to both writers. Every other inherited
/// <see cref="TextWriter"/> member (including <see cref="TextWriter.WriteLine(string)"/>,
/// <see cref="TextWriter.WriteAsync(string)"/>, the non-cancellation
/// <see cref="TextWriter.WriteLineAsync(string)"/>, and <see cref="TextWriter.Write(char[])"/>) ultimately
/// calls <see cref="Write(char)"/>, which reaches <c>primary</c> only. A caller that needs the secondary
/// to see everything it writes must use the fan-out members above.
/// </para>
/// </remarks>
/// <param name="primary">The writer whose failures propagate — the terminal.</param>
/// <param name="secondary">The writer whose failures are swallowed — the tee.</param>
internal sealed class FanOutTextWriter(TextWriter primary, TextWriter secondary) : TextWriter
{
    /// <inheritdoc/>
    public override Encoding Encoding => primary.Encoding;

    /// <inheritdoc/>
    public override string NewLine
    {
        get => primary.NewLine;
#pragma warning disable CS8765 // matches TextWriter.NewLine's [AllowNull] contract; Roslyn still flags the override.
        [param: AllowNull]
        set => primary.NewLine = value;
#pragma warning restore CS8765
    }

    /// <inheritdoc/>
    public override async Task WriteLineAsync(
        ReadOnlyMemory<char> buffer,
        CancellationToken cancellationToken = default)
    {
        await primary.WriteLineAsync(buffer, cancellationToken).ConfigureAwait(false);

        try
        {
            await secondary.WriteLineAsync(buffer, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Intentional: tee errors must never surface to the user (but cancellation must propagate)
        }
    }

    /// <inheritdoc/>
    public override async Task FlushAsync(CancellationToken cancellationToken)
    {
        await primary.FlushAsync(cancellationToken).ConfigureAwait(false);

        try
        {
            await secondary.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Intentional: see WriteLineAsync.
        }
    }

    /// <inheritdoc/>
    public override Task FlushAsync() => FlushAsync(CancellationToken.None);

    /// <inheritdoc/>
    public override void Write(char value) => primary.Write(value);
}
