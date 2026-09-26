namespace DotnetTokenKiller.Application.Integration;

/// <summary>
/// Reads and writes dtk's marked section in a Crush <c>crushrc</c>, a Bash script Crush runs to build its config.
/// </summary>
/// <remarks>
/// dtk edits only the lines between its markers and never guesses: a file with a missing, repeated or out-of-order
/// marker makes install and uninstall throw, naming the file, and leaves it untouched. The section takes the file's own
/// line endings; the rest of the file is written back as read, so its mode and every other line survive.
/// </remarks>
internal static class CrushrcFile
{
    /// <summary>The line opening dtk's section.</summary>
    internal const string BeginMarker = "# >>> dtk (DotnetTokenKiller) >>>";

    /// <summary>The line closing dtk's section.</summary>
    internal const string EndMarker = "# <<< dtk <<<";

    /// <summary>dtk's section, with LF endings, registering <paramref name="command"/> for Crush's <c>bash</c> tool.</summary>
    /// <param name="command">The hook command, <c>dtk hook crush</c>.</param>
    internal static string Section(string command) =>
        $"{BeginMarker}\nhook add PreToolUse --name dtk --matcher '^bash$' --command '{command}'\n{EndMarker}\n";

    /// <summary>Creates the file with the section, appends the section, or replaces a stale one.</summary>
    /// <param name="path">The <c>crushrc</c> to write.</param>
    /// <param name="command">The hook command.</param>
    /// <param name="context">Integration context carrying the result accumulators.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">The file holds a damaged dtk section.</exception>
    internal static async Task WriteAsync(string path, string command, IntegrationContext context, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, Section(command), cancellationToken).ConfigureAwait(false);
            context.Created.Add(path);
            return;
        }

        var content = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        var newline = NewlineOf(content);
        var section = Section(command).ReplaceLineEndings(newline);
        string updated;

        if (FindSection(content, path) is { } span)
        {
            var (start, end) = span;
            if (string.Equals(content[start..end], section, StringComparison.Ordinal))
            {
                context.Unchanged.Add(path);
                return;
            }

            updated = content[..start] + section + content[end..];
        }
        else
        {
            var separator = GetSeparator(content, newline);
            updated = content + separator + section;
        }

        await File.WriteAllTextAsync(path, updated, cancellationToken).ConfigureAwait(false);
        context.Updated.Add(path);
    }

    /// <summary>
    /// Removes the section and the blank line dtk put before it; deletes the file when nothing else is left.
    /// </summary>
    /// <param name="path">The <c>crushrc</c> to edit.</param>
    /// <param name="context">The uninstall context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">The file holds a damaged dtk section.</exception>
    internal static async Task RemoveAsync(string path, IntegrationContext context, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return;
        }

        var content = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        if (FindSection(content, path) is not { } span)
        {
            context.Unchanged.Add(path);
            return;
        }

        var (start, end) = span;

        var remaining = content[..start] + content[end..];
        if (remaining.Trim().Length == 0)
        {
            UninstallHelpers.DeleteFile(path, context);
            return;
        }

        if (end == content.Length)
        {
            remaining = remaining.TrimEnd('\r', '\n') + NewlineOf(content);
        }

        await File.WriteAllTextAsync(path, remaining, cancellationToken).ConfigureAwait(false);
        context.Updated.Add(path);
    }

    /// <summary>CRLF when the file already uses it, else LF.</summary>
    /// <param name="content">The file content to check.</param>
    private static string NewlineOf(string content) => content.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";

    /// <summary>The separator to insert before the dtk section: empty if the file is empty, one newline if the file ends with one, else two newlines.</summary>
    /// <param name="content">The existing file content.</param>
    /// <param name="newline">The file's native newline sequence.</param>
    private static string GetSeparator(string content, string newline)
    {
        if (content.Length == 0)
        {
            return string.Empty;
        }

        if (content.EndsWith('\n'))
        {
            return newline;
        }

        return newline + newline;
    }

    /// <summary>
    /// The span from the begin marker's line to the end of the end marker's line (its newline included), or
    /// <see langword="null"/> when neither marker is present.
    /// </summary>
    /// <param name="content">The file content to search.</param>
    /// <param name="path">The file path, for error messages.</param>
    /// <exception cref="InvalidOperationException">A marker is missing, repeated or out of order.</exception>
    private static (int Start, int End)? FindSection(string content, string path)
    {
        var begins = new List<int>();
        var ends = new List<int>();
        var offset = 0;

        while (offset < content.Length)
        {
            var newline = content.IndexOf('\n', offset);
            var lineEnd = newline < 0 ? content.Length : newline + 1;
            var line = content[offset..lineEnd].TrimEnd('\r', '\n');

            if (line == BeginMarker)
            {
                begins.Add(offset);
            }
            else if (line == EndMarker)
            {
                ends.Add(lineEnd);
            }

            offset = lineEnd;
        }

        if (begins.Count == 0 && ends.Count == 0)
        {
            return null;
        }

        if (begins.Count == 1 && ends.Count == 1 && begins[0] < ends[0])
        {
            return (begins[0], ends[0]);
        }

        throw new InvalidOperationException(
            $"The Crush config '{path}' has a damaged dtk section ({begins.Count} '{BeginMarker}' and {ends.Count} "
            + $"'{EndMarker}' lines). Delete dtk's lines and both markers by hand, then run the command again.");
    }
}
