using DotnetTokenKiller.Domain.Tee;

namespace DotnetTokenKiller.Application.Tests.UseCases;

/// <summary>An in-memory <c>--tee</c> copy that records what it was given and when it was closed.</summary>
internal sealed class RecordingRawOutputCopy : IRawOutputCopy
{
    private readonly StringWriter _writer = new() { NewLine = "\n" };

    /// <inheritdoc/>
    public TextWriter Writer => _writer;

    /// <inheritdoc/>
    public string? Failure { get; set; }

    /// <summary>The failure closing the copy reports, as a full disk refusing the last buffered bytes would.</summary>
    public string? FailureOnDispose { get; init; }

    /// <summary>Everything written so far, readable after disposal too.</summary>
    public string Text => _writer.ToString();

    /// <summary>How many times the copy was disposed.</summary>
    public int DisposeCount { get; private set; }

    /// <inheritdoc/>
    public ValueTask DisposeAsync()
    {
        DisposeCount++;
        Failure ??= FailureOnDispose;
        return _writer.DisposeAsync();
    }
}
