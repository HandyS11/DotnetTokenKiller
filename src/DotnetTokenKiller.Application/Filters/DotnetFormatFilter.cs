using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Domain.Filters;

namespace DotnetTokenKiller.Application.Filters;

/// <summary>Condenses dotnet format output to a concise summary.</summary>
/// <remarks>
/// When <c>dotnet format</c> finds nothing to change, it produces no output at all.
/// This filter treats empty or whitespace-only input on a zero exit code as a confirmed
/// success signal and synthesises a <c>✓ dotnet format (nothing to format)</c> confirmation
/// line so that AI agents receive an explicit positive signal instead of silence.
/// <para>
/// A <c>--verify-no-changes</c> run that finds violations is laid out by file and code, with the verdict
/// last: <c>file: CODE ×n (line:col replacement, …)</c> for each, then
/// <c>✗ dotnet format: 21 violations in 4 files</c>. A line lists at most
/// <see cref="TextHelpers.MaxPositionsPerLine"/> positions and the report at most 40 lines, so a first run over an
/// unformatted repository stays readable; what is left out is counted, and the verdict counts everything.
/// </para>
/// </remarks>
/// <param name="rootPath">Optional root path used to shorten file paths in violation messages.</param>
public sealed partial class DotnetFormatFilter(string? rootPath = null) : IOutputFilter
{
    /// <summary>How many group lines the report lists before it counts the groups it leaves out.</summary>
    private const int MaxGroupLines = 40;

    private string RootPath => rootPath ?? Environment.CurrentDirectory;

    /// <summary>Applies the filter to the format output.</summary>
    /// <param name="strippedOutput">The format output to filter, with ANSI escape sequences already stripped.</param>
    /// <param name="exitCode">The process exit code; the sole source of truth for the success/failure verdict.</param>
    public string Apply(string strippedOutput, int exitCode)
    {
        var stripped = strippedOutput ?? string.Empty;

        // Heuristic: dotnet format produces no output when nothing needs formatting.
        // Synthesise a positive confirmation so agents receive an explicit success signal —
        // but only when the process actually exited successfully.
        if (string.IsNullOrWhiteSpace(stripped))
        {
            return exitCode == 0 ? "✓ dotnet format (nothing to format)\n" : string.Empty;
        }

        var lines = stripped.Split('\n');

        var violations = ParseViolations(lines);
        if (violations.Count == 0 && exitCode != 0)
        {
            // Failed run with nothing parsed (crashed process, localized SDK, garbled output):
            // degrade to blank so FilteredRunUseCase's raw-tail fallback surfaces the real output
            // instead of a misleadingly clean "0 violations" status.
            return string.Empty;
        }

        if (violations.Count > 0)
        {
            return BuildViolationOutput(violations, exitCode);
        }

        var elapsedStr = ParseElapsedStr(lines);
        var formattedCount = Array.FindAll(lines, l => FormattedFilePattern().IsMatch(l)).Length;
        if (formattedCount > 0)
        {
            var suffix = elapsedStr is null ? "" : $", {elapsedStr}";
            return $"✓ dotnet format ({formattedCount} file{(formattedCount == 1 ? "" : "s")} formatted{suffix})\n";
        }

        var elapsedPart = elapsedStr is null ? "" : $", {elapsedStr}";
        return $"✓ dotnet format (nothing to format{elapsedPart})\n";
    }

    /// <summary>
    /// Turns what <c>dotnet format --verify-no-changes</c> printed into one distinct violation per position, code and
    /// message. The tool reports some diagnostics twice, so a repeat is dropped rather than counted.
    /// </summary>
    /// <param name="lines">The output, split into lines.</param>
    private List<Violation> ParseViolations(string[] lines)
    {
        var seen = new HashSet<Violation>();
        var violations = new List<Violation>();
        foreach (var line in lines)
        {
            var match = ViolationPattern().Match(line);
            if (!match.Success)
            {
                continue;
            }

            var violation = ToViolation(match);
            if (seen.Add(violation))
            {
                violations.Add(violation);
            }
        }

        return violations;
    }

    private Violation ToViolation(Match match)
    {
        // "WHITESPACE: Fix whitespace formatting. Insert '\s'. [/abs/X.csproj]": the code, then the message,
        // then the project the tool reports it for, which the file already says more usefully.
        var rest = ProjectSuffixPattern().Replace(match.Groups["rest"].Value.Trim(), string.Empty);
        var colon = rest.IndexOf(':', StringComparison.Ordinal);
        var code = rest[..colon];
        var message = rest[(colon + 1)..].Trim();
        var file = TextHelpers.ShortenPath(match.Groups["path"].Value.Trim(), RootPath);
        var position = match.Groups["lineCol"].Value.Split(',');

        var change = ChangePattern().Match(message);
        return change.Success
            ? new Violation(file, position[0], position[1], code, change.Groups["head"].Value, DescribeChange(change))
            : new Violation(file, position[0], position[1], code, message, null);
    }

    /// <summary>
    /// Lays the violations out by file, the file with the most first and ties in ordinal order, so the same
    /// violations always read the same whatever order the tool reported them in. Each file lists one line per code
    /// (see <see cref="DescribeGroup"/>), the code with the most violations first. The first
    /// <see cref="MaxGroupLines"/> lines are listed; a line then counts the groups left out and the files they are
    /// in. The status line is last and counts every violation and file, listed or not.
    /// </summary>
    /// <param name="violations">The distinct violations, at least one.</param>
    /// <param name="exitCode">The process exit code; the sole source of truth for the verdict's glyph.</param>
    private static string BuildViolationOutput(List<Violation> violations, int exitCode)
    {
        var sb = new StringBuilder();
        var files = violations
            .GroupBy(v => v.File, StringComparer.Ordinal)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .ToList();
        var groups = files
            .SelectMany(file => file
                .GroupBy(v => (v.Code, v.Head))
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key.Code, StringComparer.Ordinal)
                .ThenBy(g => g.Key.Head, StringComparer.Ordinal)
                .Select(group => (File: file.Key, Group: group)))
            .ToList();
        foreach (var (file, group) in groups.Take(MaxGroupLines))
        {
            sb.AppendLine(DescribeGroup(file, group));
        }

        if (groups.Count > MaxGroupLines)
        {
            var hidden = groups.Skip(MaxGroupLines).ToList();
            var hiddenFiles = hidden.Select(h => h.File).Distinct(StringComparer.Ordinal).Count();
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"… {Counted(hidden.Count, "more group")} in {Counted(hiddenFiles, "file")} (dtk log)");
        }

        // A warning glyph rather than a cross for a zero exit code: the run did not fail, but violations were found.
        var glyph = exitCode == 0 ? '⚠' : '✗';
        sb.AppendLine(CultureInfo.InvariantCulture,
            $"{glyph} dotnet format: {Counted(violations.Count, "violation")} in {Counted(files.Count, "file")}");

        // Normalize to '\n' so output stays identical across platforms; AppendLine emits '\r\n' on Windows.
        return sb.ToString().ReplaceLineEndings("\n");
    }

    /// <summary>
    /// One line for the violations of a file that share a code: <c>file: CODE ×3 (21:45 newline + 4 spaces, …)</c>.
    /// The positions are in line and column order, compared as numbers, each followed by what to change there when
    /// the tool said; the first <see cref="TextHelpers.MaxPositionsPerLine"/> are listed and the rest counted
    /// (<c> … +5 more</c>), while <c>×n</c> counts them all. Violations that carry no change (<c>IMPORTS: Fix imports ordering.</c>) have only their
    /// message to tell them apart, so it follows the positions once:
    /// <c>file: IDE0005 ×2 (1:1, 2:1): Using directive is unnecessary.</c>
    /// Where a change is given the message is only the tool's boilerplate for the code
    /// (<c>Fix whitespace formatting.</c>) and is left out.
    /// </summary>
    /// <param name="file">The file's path, shortened against the root.</param>
    /// <param name="group">The file's violations that share a code and a message head, at least one.</param>
    private static string DescribeGroup(string file, IGrouping<(string Code, string Head), Violation> group)
    {
        var ordered = group
            .OrderBy(v => TextHelpers.ParseNumberOrMax(v.Line))
            .ThenBy(v => TextHelpers.ParseNumberOrMax(v.Col))
            .ToList();
        var positions = TextHelpers.JoinCapped(
            ordered.ConvertAll(v => v.Change is null ? $"{v.Line}:{v.Col}" : $"{v.Line}:{v.Col} {v.Change}"),
            ", ");
        var times = ordered.Count > 1
            ? string.Create(CultureInfo.InvariantCulture, $" ×{ordered.Count}")
            : string.Empty;
        var message = group.Key.Head.Length == 0 || ordered.Exists(v => v.Change is not null)
            ? string.Empty
            : $": {group.Key.Head}";
        return $"{file}: {group.Key.Code}{times} ({positions}){message}";
    }

    /// <summary>A count with its noun, singular for one: <c>1 file</c>, <c>4 files</c>.</summary>
    /// <param name="count">How many.</param>
    /// <param name="noun">The singular noun.</param>
    private static string Counted(int count, string noun) =>
        string.Create(CultureInfo.InvariantCulture, $"{count} {noun}{(count == 1 ? "" : "s")}");

    /// <summary>
    /// What a formatting message asks for, in few words: <c>Replace 1 characters with '\n\s\s\s\s'</c> reads
    /// <c>newline + 4 spaces</c>, <c>Insert '\s'</c> reads <c>insert 1 space</c> and <c>Delete 2 characters</c> reads
    /// <c>delete 2 chars</c>.
    /// </summary>
    /// <param name="change">A match of <see cref="ChangePattern"/>.</param>
    /// <returns>The change, or <see langword="null"/> when it names no text to put in.</returns>
    private static string? DescribeChange(Match change)
    {
        if (change.Groups["delete"].Success)
        {
            var count = change.Groups["delete"].Value;
            return $"delete {count} char{(count == "1" ? "" : "s")}";
        }

        var inserted = change.Groups["insert"].Success;
        var summary = SummarizeReplacement(change.Groups[inserted ? "insert" : "replace"].Value);
        if (summary.Length == 0)
        {
            return null;
        }

        return inserted ? $"insert {summary}" : summary;
    }

    /// <summary>
    /// Reads the escaped text <c>dotnet format</c> prints for what it puts in (<c>\n\s\s\s\s</c>) as words:
    /// <c>\n</c> and <c>\r\n</c> are a <c>newline</c>, <c>\t</c> a <c>tab</c>, <c>\s</c> a space, and a run of
    /// one kind is counted (<c>4 spaces</c>, <c>2 tabs</c>). The parts are joined by <c>" + "</c>, in order; any other
    /// text is quoted as it is.
    /// </summary>
    /// <param name="escaped">The text as the tool printed it between quotes, such as <c>\n\s\s\s\s</c>.</param>
    /// <returns>The summary, or an empty string for empty text.</returns>
    internal static string SummarizeReplacement(string escaped)
    {
        ArgumentNullException.ThrowIfNull(escaped);

        var tokens = EscapeTokenPattern().Matches(escaped).Select(ReadEscape).ToList();
        var parts = new List<string>();
        for (var start = 0; start < tokens.Count;)
        {
            var end = start;
            while (end < tokens.Count && tokens[end].Kind == tokens[start].Kind)
            {
                end++;
            }

            var run = tokens.GetRange(start, end - start);
            parts.Add(tokens[start].Kind switch
            {
                EscapeKind.Newline => run.Count == 1 ? "newline" : $"{run.Count} newlines",
                EscapeKind.Tab => run.Count == 1 ? "tab" : $"{run.Count} tabs",
                EscapeKind.Space => $"{run.Count} space{(run.Count == 1 ? "" : "s")}",
                _ => $"'{string.Concat(run.Select(t => t.Text))}'",
            });
            start = end;
        }

        return string.Join(" + ", parts);
    }

    private static (EscapeKind Kind, string Text) ReadEscape(Match token)
    {
        if (token.Groups["newline"].Success)
        {
            return (EscapeKind.Newline, token.Value);
        }

        if (token.Groups["tab"].Success)
        {
            return (EscapeKind.Tab, token.Value);
        }

        return (token.Groups["space"].Success ? EscapeKind.Space : EscapeKind.Text, token.Value);
    }

    private static string? ParseElapsedStr(string[] lines)
    {
        foreach (var line in lines)
        {
            var match = FormatCompletePattern().Match(line);
            if (!match.Success)
            {
                continue;
            }

            if (double.TryParse(match.Groups["ms"].Value, CultureInfo.InvariantCulture, out var ms))
            {
                return DurationFormat.Format(ms, exact: true);
            }
        }

        return null;
    }

    // "  Formatted code file '/path/to/file.cs'."
    [GeneratedRegex(@"Formatted code file '", RegexOptions.IgnoreCase)]
    private static partial Regex FormattedFilePattern();

    // "Format complete in 2345ms."
    [GeneratedRegex(@"Format complete in (?<ms>[\d.]+)ms", RegexOptions.IgnoreCase)]
    private static partial Regex FormatCompletePattern();

    // "/path/to/file.cs(1,1): error whitespace: Fix whitespace formatting."
    [GeneratedRegex(@"(?<path>.+?)\((?<lineCol>\d+,\d+)\):\s+(?<level>error|warning)\s+(?<rest>\w+:.+)",
        RegexOptions.IgnoreCase)]
    private static partial Regex ViolationPattern();

    // The project the tool reports a violation for, in brackets at the end: " [/abs/path/X.csproj]". Only a project
    // file is taken for it, so a bracket that ends the message itself ("... 'int[]' [Preview]") stays.
    [GeneratedRegex(@"\s+\[[^\]]*\.(?:cs|fs|vb)?proj\]\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex ProjectSuffixPattern();

    // A formatting message: what is wrong ("Fix whitespace formatting.") and what to change, which the tool words as
    // one of "Replace 1 characters with '\n\s\s\s\s'.", "Insert '\s'." and "Delete 2 characters.". Messages of the
    // tool's other codes ("Fix imports ordering.") and of analyzers have no such ending and match nothing.
    [GeneratedRegex(
        @"^(?<head>.+?\.)\s+(?:Replace \d+ characters? with '(?<replace>.*)'|Insert '(?<insert>.*)'"
        + @"|Delete (?<delete>\d+) characters?)\.?$")]
    private static partial Regex ChangePattern();

    // One unit of the tool's escaped whitespace: the escape sequences \r\n, \n, \t and \s as written (a backslash
    // and a letter), or any other single character.
    [GeneratedRegex(@"(?<newline>\\r\\n|\\n)|(?<tab>\\t)|(?<space>\\s)|[\s\S]")]
    private static partial Regex EscapeTokenPattern();

    private enum EscapeKind
    {
        Newline = 0,
        Tab = 1,
        Space = 2,
        Text = 3,
    }

    /// <summary>One violation as it is laid out; equal ones are the tool reporting the same thing twice.</summary>
    /// <param name="File">The file, shortened against the root.</param>
    /// <param name="Line">The line as printed.</param>
    /// <param name="Col">The column as printed.</param>
    /// <param name="Code">The code, such as <c>WHITESPACE</c> or <c>IDE0055</c>.</param>
    /// <param name="Head">The message, without the change it asks for (if any) and without the project.</param>
    /// <param name="Change">What to change there in few words, or <see langword="null"/> when none is given.</param>
    private sealed record Violation(string File, string Line, string Col, string Code, string Head, string? Change);
}
