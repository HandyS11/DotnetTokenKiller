using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Domain.Filters;

namespace DotnetTokenKiller.Application.Filters;

/// <summary>Condenses <c>dotnet list package</c> output, in all four of its variants.</summary>
/// <remarks>
/// The variant is detected from marker phrases in the output rather than from arguments, because
/// <see cref="IOutputFilter.Apply"/> does not receive them. Table rows are mapped onto the
/// column names of the header row above them, which is what makes multi-word cell values
/// (<c>Critical Bugs</c>) and the <c>Transitive Package</c> sub-table's missing <c>Requested</c>
/// column parse correctly.
/// <para>
/// Unlike every other dtk filter, this one carries its own unrecognized-output guard rather than
/// leaning on <c>FilteredRunUseCase</c>'s raw-tail fallback: see <see cref="Unrecognized"/>.
/// </para>
/// </remarks>
public sealed partial class DotnetListPackageFilter : IOutputFilter
{
    private const int MaxGroups = 30;

    /// <summary>Applies the filter to raw <c>dotnet list package</c> output.</summary>
    /// <param name="rawOutput">The raw output to filter.</param>
    /// <param name="exitCode">The process exit code; the sole source of truth for the success/failure verdict.</param>
    public string Apply(string rawOutput, int exitCode)
    {
        if (string.IsNullOrWhiteSpace(rawOutput))
        {
            return string.Empty;
        }

        var stripped = AnsiStrip.Strip(rawOutput);
        var state = Parse(stripped.Split(["\r\n", "\n"], StringSplitOptions.None));

        if (state.Variant == Variant.Unknown || state.DroppedRows > 0)
        {
            return Unrecognized(stripped, exitCode);
        }

        return state.Variant switch
        {
            Variant.Plain => FormatPlain(state, exitCode),
            Variant.Outdated => FormatAudit(
                state, exitCode, "--outdated", ("package with updates", "packages with updates"),
                entry => $"{entry.Resolved} → {entry.Latest}"),
            Variant.Deprecated => FormatAudit(
                state, exitCode, "--deprecated", ("deprecated package", "deprecated packages"),
                entry => $"{Version(entry)} — {entry.Reason}{Arrow(entry.Alternative)}"),
            Variant.Vulnerable => FormatAudit(
                state, exitCode, "--vulnerable", ("vulnerable package", "vulnerable packages"),
                entry => $"{Version(entry)} — {entry.Severity} {entry.Advisory}".TrimEnd()),
            _ => Unrecognized(stripped, exitCode)
        };
    }

    /// <summary>
    /// Surfaces output this filter did not understand, rather than a verdict about it.
    /// </summary>
    /// <param name="stripped">The ANSI-stripped raw output.</param>
    /// <param name="exitCode">The process exit code.</param>
    /// <returns>
    /// On a failed run, <see cref="string.Empty"/> — the exit code is non-zero, so
    /// <c>FilteredRunUseCase</c>'s raw-tail fallback fires, prefixes an explicit failure verdict, and
    /// records the run as <c>RawTailFallback</c>. On a successful run, the raw output behind a
    /// <c>⚠</c> marker, because that fallback is gated on a non-zero exit code and this command never
    /// produces one.
    /// </returns>
    /// <remarks>
    /// <c>dotnet list package</c> exits 0 even when it reports deprecated or vulnerable packages
    /// (verified against SDK 10.0.302), so this filter cannot borrow the exit-code-gated raw-tail
    /// fallback the way its siblings do — it has to carry its own guard. Without one, an unrecognized
    /// table shape drops every <c>&gt; </c> row while the project headers still parse, and the
    /// zero-findings path then emits an affirmative <c>✓ … (no vulnerable packages, N projects)</c>
    /// for a repository that has them. Output in a shape this parser does not know
    /// (<c>--format json</c>, a localized SDK, a future column layout) would likewise vanish entirely.
    /// Passing the raw text through keeps the "never worse than raw" guarantee and mirrors what
    /// <c>FilteredRunUseCase.ApplyFilterSafelyAsync</c> already does for a filter that throws.
    /// </remarks>
    private static string Unrecognized(string stripped, int exitCode)
    {
        if (exitCode != 0)
        {
            return string.Empty;
        }

        var text = stripped.ReplaceLineEndings("\n").TrimEnd('\n');
        return $"⚠ dotnet list package: unrecognized output, passed through unfiltered\n{text}\n";
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

            if (!trimmed.StartsWith("> ", StringComparison.Ordinal))
            {
                continue;
            }

            // A row is either turned into an entry or counted as dropped — never silently skipped.
            // Apply's guard reads DroppedRows to tell "understood, nothing to report" apart from
            // "did not understand this", which is the whole difference between a trustworthy clean
            // verdict and a false one.
            var cells = columns.Length > 0 ? SplitCells(trimmed[2..]) : [];
            if (cells.Length > 0)
            {
                AddEntry(state, project, columns, cells);
            }
            else
            {
                state.DroppedRows++;
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
        else if (what.Contains("no updates", StringComparison.OrdinalIgnoreCase))
        {
            state.Variant = Variant.Outdated;
            state.CleanProjects++;
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

    /// <summary>Records one parsed table row. Callers must not pass an empty <paramref name="cells"/>.</summary>
    /// <param name="state">The parse state being accumulated.</param>
    /// <param name="project">The project whose table this row belongs to.</param>
    /// <param name="columns">Header cells for that table.</param>
    /// <param name="cells">The row's cells, index-aligned with <paramref name="columns"/>.</param>
    private static void AddEntry(ParseState state, string project, string[] columns, string[] cells)
    {
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

    /// <summary>Renders <c> → alternative</c>, or empty when the package suggests no replacement.</summary>
    /// <param name="alternative">The <c>Alternative</c> cell, which is often absent.</param>
    private static string Arrow(string alternative) =>
        alternative.Length > 0 ? $" → {alternative}" : string.Empty;

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
        // Eligibility is derived from state.Projects, not byProject: a project whose header line
        // parsed but which produced zero rows (a legitimate "no packages" project) is still one of
        // the projects, and must contribute an empty set to the intersection so it blocks "shared".
        var shared = state.Projects.Count > 1
            ? state.Projects.Skip(1).Aggregate(
                new HashSet<string>(byProject.GetValueOrDefault(state.Projects.First(), []), StringComparer.Ordinal),
                (acc, proj) =>
                {
                    acc.IntersectWith(byProject.GetValueOrDefault(proj, []));
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

    /// <summary>Renders an audit variant: findings grouped by package, else a single clean line.</summary>
    /// <param name="state">The parsed output.</param>
    /// <param name="exitCode">The process exit code.</param>
    /// <param name="flag">The variant's flag, e.g. <c>--outdated</c>.</param>
    /// <param name="noun">Singular and plural forms of the finding noun.</param>
    /// <param name="detail">Renders the variant-specific detail for one entry.</param>
    private static string FormatAudit(
        ParseState state,
        int exitCode,
        string flag,
        (string Singular, string Plural) noun,
        Func<Entry, string> detail)
    {
        var projects = state.Projects.Count;

        if (state.Entries.Count == 0)
        {
            if (exitCode != 0)
            {
                return string.Empty;
            }

            var clean = string.Equals(flag, "--outdated", StringComparison.Ordinal)
                ? $"all {projects} project{Plural(projects)} up to date"
                : $"no {noun.Plural}, {projects} project{Plural(projects)}";
            return $"✓ dotnet list package {flag} ({clean})\n";
        }

        // Group by package plus its rendered detail, so rows that say different things never merge.
        var groups = state.Entries
            .GroupBy(e => (e.Id, Detail: detail(e)))
            .Select(g => (
                g.Key.Id,
                g.Key.Detail,
                Projects: g.Select(e => e.Project).Distinct(StringComparer.Ordinal).ToList()))
            .OrderBy(g => g.Id, StringComparer.Ordinal)
            .ThenBy(g => g.Detail, StringComparer.Ordinal)
            .ToList();

        var affected = state.Entries.Select(e => e.Project).Distinct(StringComparer.Ordinal).Count();
        var scope = affected == projects
            ? $"all {projects} project{Plural(projects)}"
            : $"{affected} of {projects} projects";

        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture,
            $"dotnet list package {flag}: {groups.Count} {(groups.Count == 1 ? noun.Singular : noun.Plural)} ({scope})");

        var omitted = 0;
        for (var i = 0; i < groups.Count; i++)
        {
            if (i >= MaxGroups)
            {
                omitted = groups.Count - MaxGroups;
                break;
            }

            var (id, det, projectNames) = groups[i];

            // A count alone would be useless for a single project, so name it.
            var where = projectNames.Count == 1 ? projectNames[0] : $"{projectNames.Count} projects";
            sb.AppendLine(CultureInfo.InvariantCulture, $"  {id} {det} ({where})");
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

        /// <summary>
        /// How many <c>&gt; </c> table rows were seen but could not be mapped onto a recognized
        /// header row. Non-zero means the output was not understood, which is a different thing
        /// from there being nothing to report.
        /// </summary>
        public int DroppedRows { get; set; }
    }
}
