using DotnetTokenKiller.Domain.Tee;

namespace DotnetTokenKiller.Application.UseCases;

/// <summary>Selects a previously written tee log and windows its contents.</summary>
/// <remarks>
/// Does no I/O of its own — everything comes through <see cref="ITeeLogStore"/> — so selection and
/// windowing are testable without a filesystem.
/// </remarks>
/// <param name="store">The tee log store.</param>
public sealed class LogViewUseCase(ITeeLogStore store)
{
    /// <summary>Finds the logs matching a query.</summary>
    /// <param name="query">The query to apply.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The matches, newest first, plus the count of logs excluded for lacking metadata.</returns>
    public async Task<LogSelection> SelectAsync(
        LogQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        IEnumerable<TeeLogEntry> candidates = await store.ListAsync(cancellationToken).ConfigureAwait(false);

        if (query.Subcommand is not null)
        {
            var slug = TeeLogFileName.Sanitize(query.Subcommand);
            candidates = candidates.Where(e =>
                string.Equals(e.Slug, slug, StringComparison.OrdinalIgnoreCase));
        }

        if (query.ProjectPath is null)
        {
            return new LogSelection([.. candidates], 0);
        }

        var wanted = NormalizePath(query.ProjectPath);
        var matches = new List<TeeLogEntry>();
        var legacyExcluded = 0;
        foreach (var entry in candidates)
        {
            if (entry.Header is null)
            {
                // Cannot be proven to belong to this project. Counted rather than silently dropped.
                legacyExcluded++;
                continue;
            }

            if (PathsEqual(entry.Header.ProjectPath, wanted))
            {
                matches.Add(entry);
            }
        }

        return new LogSelection(matches, legacyExcluded);
    }

    /// <summary>Selects one log and windows its body.</summary>
    /// <param name="query">The query to apply.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The view and the selection it came from.</returns>
    public async Task<LogViewResult> ViewAsync(
        LogQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var selection = await SelectAsync(query, cancellationToken).ConfigureAwait(false);
        if (query.Index < 1 || query.Index > selection.Matches.Count)
        {
            return new LogViewResult(null, selection);
        }

        var entry = selection.Matches[query.Index - 1];
        var body = await store.ReadBodyAsync(entry, cancellationToken).ConfigureAwait(false);
        var lines = SplitLines(body);
        var shown = query.Full ? lines.Length : Math.Min(Math.Max(query.Lines, 1), lines.Length);
        var window = lines[^shown..];

        return new LogViewResult(
            new LogView(entry, string.Join('\n', window), lines.Length, window.Length),
            selection);
    }

    /// <summary>Splits a body into lines, without inventing a trailing empty one.</summary>
    /// <param name="body">The log body.</param>
    /// <returns>The lines, with carriage returns stripped.</returns>
    private static string[] SplitLines(string body)
    {
        if (string.IsNullOrEmpty(body))
        {
            return [];
        }

        var trimmed = body.EndsWith('\n') ? body[..^1] : body;
        return [.. trimmed.Split('\n').Select(line => line.TrimEnd('\r'))];
    }

    /// <summary>Removes a trailing directory separator, without reducing a root to nothing.</summary>
    /// <param name="path">The path to normalize.</param>
    /// <returns>The normalized path.</returns>
    private static string NormalizePath(string path)
    {
        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return trimmed.Length == 0 ? path : trimmed;
    }

    /// <summary>Compares two project paths, case-insensitively only where the platform is.</summary>
    /// <param name="recorded">The path recorded in the log header.</param>
    /// <param name="wanted">The already-normalized path to match.</param>
    /// <returns><see langword="true"/> when they name the same directory.</returns>
    private static bool PathsEqual(string recorded, string wanted) =>
        string.Equals(
            NormalizePath(recorded),
            wanted,
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
}
