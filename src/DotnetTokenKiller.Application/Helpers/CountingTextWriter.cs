using System.Diagnostics.CodeAnalysis;
using System.Text;

namespace DotnetTokenKiller.Application.Helpers;

/// <summary>
/// The stdout sink of a tracked run: every line goes to the tee writer as before, and to the counter.
/// </summary>
/// <remarks>
/// Like <see cref="FanOutTextWriter"/>, only <see cref="WriteLineAsync(ReadOnlyMemory{char}, CancellationToken)"/>
/// and <see cref="FlushAsync(CancellationToken)"/> are routed, because the output pump calls nothing
/// else. Unlike it, nothing is swallowed: a counter that cannot append is a bug, not a broken tee.
/// The counter receives each line plus the inner writer's <see cref="NewLine"/>, which is what the
/// pump accumulates for the filter, so the counted text is the filtered text.
/// </remarks>
/// <param name="inner">The tee session's writer.</param>
/// <param name="counter">The run's counter.</param>
internal sealed class CountingTextWriter(TextWriter inner, ChunkedTokenCounter counter) : TextWriter
{
    /// <inheritdoc/>
    public override Encoding Encoding => inner.Encoding;

    /// <inheritdoc/>
    public override string NewLine
    {
        get => inner.NewLine;
#pragma warning disable CS8765 // matches TextWriter.NewLine's [AllowNull] contract; Roslyn still flags the override.
        [param: AllowNull]
        set => inner.NewLine = value;
#pragma warning restore CS8765
    }

    /// <inheritdoc/>
    public override async Task WriteLineAsync(ReadOnlyMemory<char> buffer, CancellationToken cancellationToken = default)
    {
        await inner.WriteLineAsync(buffer, cancellationToken).ConfigureAwait(false);
        counter.Append(buffer.Span);
        counter.Append(inner.NewLine);
    }

    /// <inheritdoc/>
    public override Task FlushAsync(CancellationToken cancellationToken) => inner.FlushAsync(cancellationToken);

    /// <inheritdoc/>
    public override Task FlushAsync() => inner.FlushAsync();

    /// <inheritdoc/>
    public override void Write(char value) => inner.Write(value);
}
