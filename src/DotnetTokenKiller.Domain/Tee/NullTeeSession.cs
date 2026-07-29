namespace DotnetTokenKiller.Domain.Tee;

/// <summary>
/// A session that writes nothing, used when tee is switched off or could not be opened.
/// </summary>
/// <remarks>
/// Returning this rather than null keeps every call site free of a "is tee on?" branch, which is
/// what stops the two run paths drifting apart.
/// </remarks>
public sealed class NullTeeSession : ITeeSession
{
    /// <summary>The shared instance; the type holds no state.</summary>
    public static readonly NullTeeSession Instance = new();

    private NullTeeSession()
    {
    }

    /// <inheritdoc/>
    public TextWriter Writer => TextWriter.Null;

    /// <inheritdoc/>
    public Task<string?> FinalizeAsync(int exitCode, CancellationToken cancellationToken = default) =>
        Task.FromResult<string?>(null);

    /// <inheritdoc/>
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
