using System.Globalization;
using System.Text;

namespace DotnetTokenKiller.Application.Helpers;

/// <summary>String formatting utilities.</summary>
public static class TextHelpers
{
    /// <summary>How many positions a grouped diagnostic line lists before it counts the rest.</summary>
    internal const int MaxPositionsPerLine = 20;

    /// <summary>Truncates text to the given length, appending "..." if truncated.</summary>
    /// <param name="text">The text to truncate.</param>
    /// <param name="maxLen">Maximum allowed length before truncation.</param>
    public static string Truncate(string text, int maxLen)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= maxLen)
        {
            return text;
        }

        return $"{text[..maxLen]}...";
    }

    /// <summary>Formats a token count as a human-readable string (e.g. "1.2K").</summary>
    /// <param name="count">The token count to format.</param>
    public static string FormatTokens(int count)
    {
        return count switch
        {
            >= 1_000_000 => (count / 1_000_000.0).ToString("F1", CultureInfo.InvariantCulture) + "M",
            >= 1_000 => (count / 1_000.0).ToString("F1", CultureInfo.InvariantCulture) + "K",
            _ => count.ToString(CultureInfo.InvariantCulture)
        };
    }

    /// <summary>Returns the path relative to rootPath, falling back to filename on error.</summary>
    /// <param name="absolutePath">The absolute path to shorten.</param>
    /// <param name="rootPath">The root path to make the path relative to.</param>
    public static string ShortenPath(string absolutePath, string rootPath)
    {
        if (string.IsNullOrEmpty(absolutePath))
        {
            return absolutePath;
        }

        try
        {
            var relative = Path.GetRelativePath(rootPath, absolutePath);
            return relative.Replace('\\', '/');
        }
        catch
        {
            return Path.GetFileName(absolutePath);
        }
    }

    /// <summary>
    /// Drops the root directory prefix from every path in a free-text output line, leaving the rest exactly as
    /// printed.
    /// </summary>
    /// <remarks>
    /// <see cref="ShortenPath"/> takes a path: handed a whole line, it resolves the line against the current
    /// directory (prefixing it with <c>../../…</c> whenever the root is not the current directory) and, on Windows,
    /// path normalisation trims the line's trailing dots and spaces. Only an occurrence that starts a path (the start
    /// of the line or a character that cannot be part of one) and is followed by a directory separator is removed,
    /// so <c>/repository</c> is not shortened against the root <c>/repo</c>.
    /// </remarks>
    /// <param name="line">The output line.</param>
    /// <param name="rootPath">The root directory, with or without a trailing separator.</param>
    /// <returns>The line with its root-relative paths shortened.</returns>
    public static string ShortenPathsInLine(string line, string rootPath)
    {
        if (string.IsNullOrEmpty(line) || string.IsNullOrEmpty(rootPath))
        {
            return line;
        }

        var root = rootPath.TrimEnd('/', '\\');
        if (root.Length == 0)
        {
            return line;
        }

        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var result = new StringBuilder(line.Length);
        var start = 0;
        var index = line.IndexOf(root, comparison);
        while (index >= 0)
        {
            var end = index + root.Length;
            if (end < line.Length && line[end] is '/' or '\\' && (index == 0 || !IsPathCharacter(line[index - 1])))
            {
                result.Append(line, start, index - start);
                start = end + 1;
            }

            index = line.IndexOf(root, end, comparison);
        }

        return start == 0 ? line : result.Append(line, start, line.Length - start).ToString();
    }

    /// <summary>Whether <paramref name="c"/> can continue a path, so a root found right after it is not one's start.</summary>
    /// <param name="c">The character before a candidate root.</param>
    private static bool IsPathCharacter(char c) => char.IsLetterOrDigit(c) || c is '/' or '\\' or '.' or '-' or '_' or '~';

    /// <summary>
    /// Joins the positions of one grouped diagnostic line, listing <see cref="MaxPositionsPerLine"/> at most: a
    /// line that names thousands of positions (a repository-wide whitespace fix, one error repeated everywhere)
    /// costs more than it tells. What the line leaves out is counted, <c> … +N more</c>, after the last one listed;
    /// the count of diagnostics on the line is the caller's, and stays whole.
    /// </summary>
    /// <param name="positions">The positions, already in the order to list them.</param>
    /// <param name="separator">What goes between two positions.</param>
    /// <returns>The joined positions, or an empty string for none.</returns>
    internal static string JoinCapped(IReadOnlyList<string> positions, string separator)
    {
        ArgumentNullException.ThrowIfNull(positions);

        var listed = string.Join(separator, positions.Take(MaxPositionsPerLine));
        return positions.Count <= MaxPositionsPerLine
            ? listed
            : string.Create(CultureInfo.InvariantCulture, $"{listed} … +{positions.Count - MaxPositionsPerLine} more");
    }

    /// <summary>A line or column number to sort by; a digit string too long for an int sorts last.</summary>
    /// <param name="digits">The digits a pattern captured.</param>
    internal static int ParseNumberOrMax(string digits) =>
        int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : int.MaxValue;
}
