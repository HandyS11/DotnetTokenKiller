using System.Globalization;
using System.Text;

namespace DotnetTokenKiller.Application.Helpers;

/// <summary>
/// Caps a run's output at a number of lines, keeping the last line (the run's status line) in place.
/// </summary>
public static class OutputCap
{
    /// <summary>
    /// Keeps the first <paramref name="maxLines"/> lines before the last line and replaces the rest with a
    /// single <c>… N more lines (target)</c> line, so the last line stays last.
    /// </summary>
    /// <param name="text">The output text; its last line is the status line.</param>
    /// <param name="maxLines">The most lines to keep before the status line; zero or less turns the cap off.</param>
    /// <param name="footerTarget">What the footer points to, for example <c>dtk log</c>.</param>
    /// <returns>
    /// <paramref name="text"/> unchanged when the cap is off or the body fits; otherwise the capped text,
    /// with <c>\n</c> line endings only.
    /// </returns>
    public static string Truncate(string text, int maxLines, string footerTarget)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(footerTarget);

        if (maxLines <= 0)
        {
            return text;
        }

        var lines = text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        if (lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        var bodyCount = lines.Count - 1;
        if (bodyCount <= maxLines)
        {
            return text;
        }

        var hidden = bodyCount - maxLines;
        var sb = new StringBuilder();
        foreach (var line in lines.Take(maxLines))
        {
            sb.Append(line).Append('\n');
        }

        sb.Append(string.Create(CultureInfo.InvariantCulture,
            $"… {hidden} more line{(hidden == 1 ? "" : "s")} ({footerTarget})\n"));
        return sb.Append(lines[^1]).Append('\n').ToString();
    }
}
