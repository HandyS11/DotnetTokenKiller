using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Domain.Filters;

namespace DotnetTokenKiller.Application.Filters;

/// <summary>Condenses <c>dotnet list package</c> output, in all four of its variants.</summary>
/// <remarks>
/// The variant is detected from marker phrases in the output rather than from arguments, because
/// <see cref="IOutputFilter"/>'s Apply method does not receive them. Table rows are mapped onto the
/// column names of the header row above them, which is what makes multi-word cell values
/// (<c>Critical Bugs</c>) and the <c>Transitive Package</c> sub-table's missing <c>Requested</c>
/// column parse correctly.
/// </remarks>
public sealed partial class DotnetListPackageFilter : IOutputFilter
{
    private const int MaxGroups = 30;

    /// <summary>Applies the filter to raw <c>dotnet list package</c> output.</summary>
    /// <param name="rawOutput">The raw output to filter.</param>
    /// <param name="exitCode">The process exit code; the sole source of truth for the success/failure verdict.</param>
    public string Apply(string rawOutput, int exitCode)
    {
        if (string.IsNullOrEmpty(rawOutput))
        {
            return string.Empty;
        }

        var state = Parse(AnsiStrip.Strip(rawOutput).Split(["\r\n", "\n"], StringSplitOptions.None));

        // Nothing recognizable was parsed. Returning empty lets FilteredRunUseCase's raw-tail
        // fallback surface the real output on a failure, and is honest on success too.
        return state.Variant switch
        {
            Variant.Plain => FormatPlain(state, exitCode),
            _ => string.Empty
        };
    }

    private static ParseState Parse(string[] lines)
    {
        var state = new ParseState();
        string[] columns = [];
        var project = string.Empty;

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();
            if (line.Length == 0)
            {
                continue;
            }

            var header = ProjectHeaderPattern().Match(line);
            if (header.Success)
            {
                project = header.Groups["proj"].Value;
                state.Projects.Add(project);
                ApplyProjectHeader(state, header.Groups["what"].Value);
                columns = [];
                continue;
            }

            var trimmed = line.TrimStart();

            // A table header, e.g. "Top-level Package   Requested   Resolved".
            if (!trimmed.StartsWith('>') && trimmed.Contains("Package", StringComparison.Ordinal))
            {
                columns = SplitCells(line);
                continue;
            }

            if (trimmed.StartsWith("> ", StringComparison.Ordinal) && columns.Length > 0)
            {
                AddEntry(state, project, columns, SplitCells(trimmed[2..]));
            }
        }

        return state;
    }

    private static void ApplyProjectHeader(ParseState state, string what)
    {
        if (what.Contains("package references", StringComparison.OrdinalIgnoreCase))
        {
            state.Variant = Variant.Plain;
        }
        else if (what.Contains("updates to its packages", StringComparison.OrdinalIgnoreCase))
        {
            state.Variant = Variant.Outdated;
        }
        else if (what.Contains("deprecated packages", StringComparison.OrdinalIgnoreCase))
        {
            state.Variant = Variant.Deprecated;
            TrackCleanProject(state, what);
        }
        else if (what.Contains("vulnerable packages", StringComparison.OrdinalIgnoreCase))
        {
            state.Variant = Variant.Vulnerable;
            TrackCleanProject(state, what);
        }
    }

    private static void TrackCleanProject(ParseState state, string what)
    {
        if (what.StartsWith("no ", StringComparison.OrdinalIgnoreCase))
        {
            state.CleanProjects++;
        }
    }

    private static void AddEntry(ParseState state, string project, string[] columns, string[] cells)
    {
        if (cells.Length == 0)
        {
            return;
        }

        state.Entries.Add(new Entry(
            project,
            cells[0],
            Cell(columns, cells, "Requested"),
            Cell(columns, cells, "Resolved"),
            Cell(columns, cells, "Latest"),
            Cell(columns, cells, "Reason(s)"),
            Cell(columns, cells, "Alternative"),
            Cell(columns, cells, "Severity"),
            Cell(columns, cells, "Advisory URL")));
    }

    /// <summary>Reads the cell under <paramref name="column"/>, or empty when that column is absent.</summary>
    /// <param name="columns">Header cells for the table this row belongs to.</param>
    /// <param name="cells">The row's cells, index-aligned with <paramref name="columns"/>.</param>
    /// <param name="column">The header name to look up.</param>
    private static string Cell(string[] columns, string[] cells, string column)
    {
        var index = Array.FindIndex(columns, c => string.Equals(c, column, StringComparison.OrdinalIgnoreCase));
        return index >= 0 && index < cells.Length ? cells[index] : string.Empty;
    }

    /// <summary>Splits a padded table line on runs of two or more spaces.</summary>
    /// <param name="line">The line to split.</param>
    private static string[] SplitCells(string line) =>
        [.. CellSeparatorPattern().Split(line.Trim()).Select(cell => cell.Trim()).Where(cell => cell.Length > 0)];

    /// <summary>Renders <c>Resolved</c>, or <c>requested→resolved</c> when a floating version moved.</summary>
    /// <param name="entry">The parsed row.</param>
    private static string Version(Entry entry) =>
        entry.Requested.Length > 0 &&
        !string.Equals(entry.Requested, entry.Resolved, StringComparison.OrdinalIgnoreCase)
            ? $"{entry.Requested}→{entry.Resolved}"
            : entry.Resolved;

    private static string FormatPlain(ParseState state, int exitCode)
    {
        if (state.Entries.Count == 0)
        {
            return exitCode == 0
                ? $"✓ dotnet list package ({state.Projects.Count} project{Plural(state.Projects.Count)}, 0 packages)\n"
                : string.Empty;
        }

        var byProject = state.Entries
            .GroupBy(e => e.Project, StringComparer.Ordinal)
            .ToDictionary(
                g => g.Key,
                g => g.Select(e => $"{e.Id} {Version(e)}").ToHashSet(StringComparer.Ordinal),
                StringComparer.Ordinal);

        // Strict all-or-nothing: present in every project at the same version. A near-universal
        // package stays on its projects' own lines rather than becoming an "except X" special case.
        var shared = byProject.Count > 1
            ? byProject.Values.Skip(1).Aggregate(
                new HashSet<string>(byProject.Values.First(), StringComparer.Ordinal),
                (acc, next) =>
                {
                    acc.IntersectWith(next);
                    return acc;
                })
            : [];

        var distinct = byProject.Values.SelectMany(v => v).Distinct(StringComparer.Ordinal).Count();

        var sb = new StringBuilder();
        var glyph = exitCode == 0 ? "✓ " : string.Empty;
        sb.AppendLine(CultureInfo.InvariantCulture,
            $"{glyph}dotnet list package ({state.Projects.Count} project{Plural(state.Projects.Count)}, {distinct} package{Plural(distinct)})");

        var groups = 0;
        var omitted = 0;

        if (shared.Count > 0)
        {
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"  all projects: {string.Join(", ", shared.Order(StringComparer.Ordinal))}");
            groups++;
        }

        foreach (var (project, packages) in byProject.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            var own = packages.Except(shared, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
            if (own.Count == 0)
            {
                continue;
            }

            if (groups >= MaxGroups)
            {
                omitted += own.Count;
                continue;
            }

            sb.AppendLine(CultureInfo.InvariantCulture, $"  {project}: {string.Join(", ", own)}");
            groups++;
        }

        AppendTruncation(sb, omitted);
        return sb.ToString().ReplaceLineEndings("\n");
    }

    /// <summary>Appends the explicit truncation line. Truncation is always stated, never silent.</summary>
    /// <param name="sb">The buffer being built.</param>
    /// <param name="omitted">How many packages were dropped; nothing is appended when zero.</param>
    private static void AppendTruncation(StringBuilder sb, int omitted)
    {
        if (omitted > 0)
        {
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"  … and {omitted} more package{Plural(omitted)} (use --show-log for full output)");
        }
    }

    private static string Plural(int count) => count == 1 ? "" : "s";

    // "Project 'Name' has the following package references"  (plain uses single quotes)
    // "Project `Name` has the following updates to its packages"  (audit variants use backticks)
    // "The given project `Name` has no deprecated packages given the current sources."
    [GeneratedRegex(@"^(?:The given project|Project)\s+['`](?<proj>[^'`]+)['`]\s+has\s+(?<what>.+?)\.?$",
        RegexOptions.IgnoreCase)]
    private static partial Regex ProjectHeaderPattern();

    // Padded table columns are separated by two or more spaces.
    [GeneratedRegex(@"\s{2,}")]
    private static partial Regex CellSeparatorPattern();

    private enum Variant
    {
        Unknown = 0,
        Plain = 1,
        Outdated = 2,
        Deprecated = 3,
        Vulnerable = 4
    }

    private sealed record Entry(
        string Project,
        string Id,
        string Requested,
        string Resolved,
        string Latest,
        string Reason,
        string Alternative,
        string Severity,
        string Advisory);

    private sealed class ParseState
    {
        public Variant Variant { get; set; }
        public HashSet<string> Projects { get; } = new(StringComparer.Ordinal);
        public List<Entry> Entries { get; } = [];
        public int CleanProjects { get; set; }
    }
}
