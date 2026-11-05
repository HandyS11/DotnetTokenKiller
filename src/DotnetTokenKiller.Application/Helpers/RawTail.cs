using System.Text;

namespace DotnetTokenKiller.Application.Helpers;

/// <summary>
/// The last lines of a run's raw output: what a filtered run shows when its filter recognised nothing, so the
/// reader still sees how the output ended.
/// </summary>
public static class RawTail
{
    /// <summary>How many lines a raw-tail fallback shows.</summary>
    public const int DefaultLines = 40;

    /// <summary>
    /// Returns the last <paramref name="lines"/> non-blank lines of <paramref name="text"/>, each ended by
    /// <c>\n</c>. A blank line is left out and does not count; carriage returns are dropped, so a CRLF text
    /// yields what its LF form does.
    /// </summary>
    /// <param name="text">The raw (ANSI-stripped) output.</param>
    /// <param name="lines">How many lines to keep.</param>
    /// <returns>The tail, or an empty string when <paramref name="text"/> has no non-blank line.</returns>
    public static string Last(string text, int lines = DefaultLines)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentOutOfRangeException.ThrowIfNegative(lines);

        var sb = new StringBuilder();
        foreach (var line in text.Split('\n')
                     .Select(l => l.TrimEnd('\r'))
                     .Where(l => l.Length > 0)
                     .TakeLast(lines))
        {
            sb.Append(line).Append('\n');
        }

        return sb.ToString();
    }
}
