using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DotnetTokenKiller.Application.Helpers;

namespace DotnetTokenKiller.Application.Filters;

/// <summary>
/// The diagnostics, project count and duration parsed from one MSBuild-driven run, rendered as the
/// error/warning summary <c>dotnet build</c>, <c>dotnet publish</c> and <c>dotnet pack</c> share.
/// All three print compiler and MSBuild diagnostics in the same shapes, so they are parsed and laid
/// out once, here; a filter adds only what is specific to its verb.
/// </summary>
internal sealed partial class MsBuildDiagnosticReport
{
    /// <summary>
    /// Longest message an error is rendered with. A compiler error carries what to change late in its text
    /// (CS1503 ends in the type it expected, CS0121 in its second overload), so this is generous.
    /// </summary>
    private const int ErrorMessageMaxLen = 400;

    /// <summary>Longest message a warning is rendered with: a warning is skimmed, and rarely worth more.</summary>
    private const int WarningMessageMaxLen = 160;

    /// <summary>A URL, up to the whitespace, closing bracket or quote that ends it.</summary>
    private const string UrlBody = @"https?://[^\s)\]'""<>]+";

    /// <summary>Stands in for the diagnostic code on MSBuild output that carries none (Exec tasks).</summary>
    internal const string NoCode = "(no code)";

    /// <summary>Groups diagnostics that name no source file, such as tool- and project-level errors.</summary>
    private const string NoFile = "(no file)";

    private const string ErrorLevel = "error";
    private const string WarningLevel = "warning";

    /// <summary>The extensions of project files; a solution, which also ends a diagnostic's suffix, is not one.</summary>
    private static readonly FrozenSet<string> ProjectFileExtensions =
        new[] { ".csproj", ".fsproj", ".vbproj", ".proj" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private readonly List<Diagnostic> _errors;
    private readonly List<Diagnostic> _warnings;
    private readonly string _context;

    private MsBuildDiagnosticReport(ParsedOutput parsed)
    {
        _errors = [.. parsed.Diagnostics.Where(d => d.Level == ErrorLevel)];
        _warnings = [.. parsed.Diagnostics.Where(d => d.Level == WarningLevel)];
        _context = BuildContext(CountProjects(parsed), parsed.Elapsed);
        ErrorTotal = Math.Max(parsed.DeclaredErrors ?? 0, _errors.Count);
        WarningTotal = Math.Max(parsed.DeclaredWarnings ?? 0, _warnings.Count);
        FailedProjectCount = _errors
            .Where(d => d.Project.Length > 0)
            .Select(d => d.Project)
            .Distinct(StringComparer.Ordinal)
            .Count();
        WarningCodes =
        [
            .. _warnings
                .GroupBy(d => CodeLabel(d.Code))
                .Select(g => (Code: g.Key, Count: g.Count()))
                .OrderByDescending(c => c.Count)
                .ThenBy(c => c.Code, StringComparer.Ordinal)
        ];
    }

    /// <summary>
    /// The larger of MSBuild's own "N Error(s)" tally and the errors parsed. The tally is the
    /// authority on how many diagnostics the build raised: it counts ones this report deliberately
    /// collapses (the same error reported once per target framework) and ones the regexes never
    /// matched, so taking the larger stops the status line from quietly reporting fewer problems than
    /// the build actually found.
    /// </summary>
    public int ErrorTotal { get; }

    /// <summary>The larger of MSBuild's own "N Warning(s)" tally and the warnings parsed.</summary>
    public int WarningTotal { get; }

    /// <summary>
    /// How many distinct projects reported an error, named by the project file without its extension
    /// from the diagnostic's trailing <c>[path/X.csproj]</c> suffix; a target framework or other
    /// property after <c>::</c> is dropped, so one multi-targeted project counts once. A suffix that is
    /// not a project file (a solution's) names none. Zero when no error carries a project.
    /// </summary>
    public int FailedProjectCount { get; }

    /// <summary>
    /// The parsed warnings' codes with how many of each, the most frequent first and equal counts in
    /// ordinal code order. A warning that carries no code is listed as <c>(no code)</c>, as the
    /// rendered report does, so the counts add up to the warnings parsed.
    /// </summary>
    public IReadOnlyList<(string Code, int Count)> WarningCodes { get; }

    /// <summary>Parses the output of an MSBuild-driven run in a single pass.</summary>
    /// <param name="strippedOutput">The run's output, with ANSI escape sequences already stripped.</param>
    /// <param name="rootPath">The root diagnostic file paths are shortened against.</param>
    /// <param name="claimLine">
    /// Offered every line first, with its trailing <c>'\r'</c> removed. Returning <see langword="true"/>
    /// takes the line out of diagnostic parsing, so a caller can read the verb-specific lines (a
    /// publish directory, a created package) that this report does not know about.
    /// </param>
    public static MsBuildDiagnosticReport Parse(string strippedOutput, string rootPath,
        Func<string, bool>? claimLine = null)
    {
        ArgumentNullException.ThrowIfNull(strippedOutput);

        var diagnostics = new List<Diagnostic>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var outputProjects = new HashSet<string>(StringComparer.Ordinal);
        var elapsed = string.Empty;
        int? declaredErrors = null;
        int? declaredWarnings = null;

        foreach (var rawLine in strippedOutput.Split('\n'))
        {
            var line = rawLine.TrimEnd('\r');

            if (claimLine?.Invoke(line) == true)
            {
                continue;
            }

            // Note the project of an output line (e.g., "  MyProject -> /path/to/bin/Debug/..."). A project
            // built for several target frameworks prints one per framework, under the same name.
            var outputMatch = ProjectOutputPattern().Match(line);
            if (outputMatch.Success)
            {
                outputProjects.Add(outputMatch.Groups["name"].Value);
                continue;
            }

            // Parse elapsed time
            var elapsedMatch = TimeElapsedPattern().Match(line);
            if (elapsedMatch.Success)
            {
                elapsed = FormatElapsed(elapsedMatch.Value);
                continue;
            }

            // MSBuild's end-of-build tally ("    3 Error(s)"). Read before it is discarded as
            // noise: it is the only count in the output that is not derived from what we parsed.
            if (TryReadDeclaredCount(line, ref declaredErrors, ref declaredWarnings))
            {
                continue;
            }

            // Skip noise lines, then parse diagnostic lines with and without a file/line/col anchor
            if (!IsNoiseLine(line) && !TryAddDiagnosticLine(line, rootPath, seen, diagnostics))
            {
                AddSimpleDiagnosticLine(line, seen, diagnostics);
            }
        }

        return new MsBuildDiagnosticReport(
            new ParsedOutput(diagnostics, outputProjects, elapsed, declaredErrors, declaredWarnings));
    }

    /// <summary>
    /// Renders the report with its verdict last, so <c>| tail -1</c> always carries it: the
    /// diagnostics body (see <see cref="RenderDiagnostics"/>), the lines naming what a successful run
    /// produced, then one status line. With nothing to report that line is a clean tick on its own.
    /// </summary>
    /// <param name="command">The command named in the status line, e.g. <c>dotnet build</c>.</param>
    /// <param name="exitCode">The process exit code; the sole source of truth for the success/failure verdict.</param>
    /// <param name="outputs">
    /// Lines naming what a successful run produced, rendered directly above the status line. Ignored
    /// when <paramref name="exitCode"/> is non-zero: a failed run's report is the same as a failed build's.
    /// </param>
    /// <returns>The report, or an empty string when the output could not be read.</returns>
    public string Render(string command, int exitCode, IReadOnlyList<string> outputs)
    {
        ArgumentNullException.ThrowIfNull(outputs);

        var sb = new StringBuilder();
        var shownOutputs = exitCode == 0 ? outputs : [];

        if (_errors.Count == 0 && _warnings.Count == 0)
        {
            // Nothing parsed (crashed process, localized SDK, garbled output). A clean tick is only
            // honest when MSBuild's summary agrees there was nothing to report; otherwise degrade to
            // blank so FilteredRunUseCase's raw-tail fallback surfaces the output we failed to read.
            if (exitCode != 0 || ErrorTotal != 0 || WarningTotal != 0)
            {
                return string.Empty;
            }

            AppendOutputs(sb, shownOutputs);
            sb.AppendLine(CultureInfo.InvariantCulture, $"✓ {command}{_context}");
            return Normalize(sb);
        }

        AppendDiagnostics(sb);
        AppendOutputs(sb, shownOutputs);
        sb.AppendLine(StatusLine(command, exitCode));

        return Normalize(sb);
    }

    /// <summary>
    /// Renders the diagnostics alone, for a caller that supplies its own status line: the errors
    /// grouped by file (see <see cref="AppendGroupedByFile"/>), with a note of the warnings left out, or, with no
    /// errors, the warnings grouped by file in the same layout. A list of top codes follows when it adds
    /// something. It carries no status line.
    /// </summary>
    /// <returns>The body, or an empty string when no diagnostic was parsed.</returns>
    public string RenderDiagnostics()
    {
        var sb = new StringBuilder();
        AppendDiagnostics(sb);
        return Normalize(sb);
    }

    private void AppendDiagnostics(StringBuilder sb)
    {
        // Errors and warnings are laid out the same way. A build with errors lists only those, so the
        // warnings do not bury them; it says how many it left out.
        if (_errors.Count == 0)
        {
            AppendGroupedByFile(sb, _warnings, WarningLevel);
            AppendTopCodes(sb, _warnings, WarningTotal);
            return;
        }

        AppendGroupedByFile(sb, _errors, ErrorLevel);
        AppendTopCodes(sb, _errors, ErrorTotal);
        if (_warnings.Count > 0)
        {
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"{Tally(WarningTotal, _warnings.Count, WarningLevel)} suppressed (use --vv to see)");
        }
    }

    /// <summary>
    /// The single status line a report with diagnostics ends with. A non-zero exit names itself, so a
    /// failed run whose errors were never matched (warnings only) still reads as the failure it is
    /// rather than as "0 errors"; the verdict is stated here once, never beside a second verdict line.
    /// An exit code of zero stays the verdict even when errors were counted, but such a line carries a
    /// warning glyph rather than a tick, which would sit misleadingly beside the error count.
    /// </summary>
    /// <param name="command">The command named in the line.</param>
    /// <param name="exitCode">The process exit code; the sole source of truth for the verdict.</param>
    private string StatusLine(string command, int exitCode)
    {
        var tally =
            $"{Tally(ErrorTotal, _errors.Count, ErrorLevel)}, {Tally(WarningTotal, _warnings.Count, WarningLevel)}";
        if (exitCode != 0)
        {
            return $"✗ {command} failed (exit {exitCode.ToString(CultureInfo.InvariantCulture)}): {tally}{_context}";
        }

        var glyph = ErrorTotal > 0 ? '⚠' : '✓';
        return $"{glyph} {command}: {tally}{_context}";
    }

    /// <summary>
    /// Converts an MSBuild "Time Elapsed" line to a compact duration, e.g. <c>2.34s</c> or <c>1m03s</c>.
    /// The line gives hundredths of a second, so the figure is exact.
    /// </summary>
    /// <param name="timeElapsedLine">The "Time Elapsed HH:MM:SS.ff" text.</param>
    /// <returns>The formatted duration, or an empty string when no duration can be read.</returns>
    internal static string FormatElapsed(string timeElapsedLine)
    {
        var match = TimeSpanValuePattern().Match(timeElapsedLine);
        if (!match.Success)
        {
            return string.Empty;
        }

        if (TimeSpan.TryParse(match.Value, CultureInfo.InvariantCulture, out var ts))
        {
            return DurationFormat.Format(ts.TotalMilliseconds, exact: true);
        }

        return string.Empty;
    }

    /// <summary>Normalizes to '\n' so output stays identical across platforms; AppendLine emits '\r\n' on Windows.</summary>
    /// <param name="sb">The rendered summary.</param>
    private static string Normalize(StringBuilder sb) => sb.ToString().ReplaceLineEndings("\n");

    private static void AppendOutputs(StringBuilder sb, IReadOnlyList<string> outputs)
    {
        foreach (var output in outputs)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"  {output}");
        }
    }

    /// <summary>Reads MSBuild's "    3 Error(s)" / "    0 Warning(s)" tally line into the matching count.</summary>
    /// <param name="line">The output line.</param>
    /// <param name="declaredErrors">Set to the error tally when <paramref name="line"/> declares it.</param>
    /// <param name="declaredWarnings">Set to the warning tally when <paramref name="line"/> declares it.</param>
    /// <returns>Whether <paramref name="line"/> is a tally line, parsed or not.</returns>
    private static bool TryReadDeclaredCount(string line, ref int? declaredErrors, ref int? declaredWarnings)
    {
        var countMatch = DeclaredCountPattern().Match(line);
        if (!countMatch.Success)
        {
            return false;
        }

        if (int.TryParse(countMatch.Groups["count"].Value, NumberStyles.None,
                CultureInfo.InvariantCulture, out var declared))
        {
            if (countMatch.Groups["kind"].Value == "Error")
            {
                declaredErrors = declared;
            }
            else
            {
                declaredWarnings = declared;
            }
        }

        return true;
    }

    /// <summary>Parses a diagnostic without file/line/col (e.g., "MSBUILD : error MSB1001: message").</summary>
    /// <param name="line">The output line.</param>
    /// <param name="seen">The keys of the diagnostics already added.</param>
    /// <param name="diagnostics">The diagnostics parsed so far.</param>
    private static void AddSimpleDiagnosticLine(string line, HashSet<string> seen, List<Diagnostic> diagnostics)
    {
        var simpleDiagMatch = SimpleDiagnosticPattern().Match(line);
        if (!simpleDiagMatch.Success)
        {
            return;
        }

        var simpleDiagnostic = new Diagnostic(
            string.Empty,
            string.Empty,
            string.Empty,
            simpleDiagMatch.Groups["level"].Value,
            simpleDiagMatch.Groups["code"].Value,
            RenderMessage(simpleDiagMatch.Groups["message"].Value, simpleDiagMatch.Groups["level"].Value),
            ProjectName(DropProperties(simpleDiagMatch.Groups["project"].Value)));

        // Keyed off the rendered message for the same reason as the file-anchored diagnostics.
        if (seen.Add($"{simpleDiagnostic.Code}:{simpleDiagnostic.Message}"))
        {
            diagnostics.Add(simpleDiagnostic);
        }
    }

    private static bool TryAddDiagnosticLine(string line, string rootPath, HashSet<string> seen,
        List<Diagnostic> diagnostics)
    {
        var diagMatch = DiagnosticPattern().Match(line);
        if (!diagMatch.Success)
        {
            return false;
        }

        // The trailing "[project]" suffix can carry extra MSBuild properties after "::", most
        // commonly the TFM a multi-targeted build re-reports the same diagnostic for (e.g.
        // "/p/A.csproj::TargetFramework=net8.0"). Drop that part for the key so those repeats still
        // collapse, but keep the project path itself: two different projects can report an identical
        // file/line/code/message (a linked GlobalUsings.cs shared via <Compile Include>, for
        // instance) and must not be merged into a single entry that hides one project's failure.
        var projectPath = DropProperties(diagMatch.Groups["project"].Value);

        var diagnostic = new Diagnostic(
            TextHelpers.ShortenPath(diagMatch.Groups["file"].Value.Trim(), rootPath),
            diagMatch.Groups["line"].Value,
            diagMatch.Groups["col"].Value,
            diagMatch.Groups["level"].Value,
            diagMatch.Groups["code"].Value,
            RenderMessage(diagMatch.Groups["message"].Value, diagMatch.Groups["level"].Value),
            ProjectName(projectPath));

        // Key off what gets rendered, not the raw capture. Two diagnostics differing only past the
        // truncation limit, or whose paths shorten to the same relative path, render as identical
        // lines — keyed on the raw text both survive and the reader sees the same error twice. The
        // message is in the key because the code is optional: without it, two unrelated codeless
        // diagnostics reported at the same position would collapse into one.
        if (seen.Add(
                $"{projectPath}:{diagnostic.File}({diagnostic.Line},{diagnostic.Col}):{diagnostic.Code}:{diagnostic.Message}"))
        {
            diagnostics.Add(diagnostic);
        }

        return true;
    }

    /// <summary>
    /// The message as it is rendered: its URLs removed, the framework namespaces in its quoted names shortened,
    /// then cut at the cap of its level. The cap comes last, so a link or a namespace costs none of it.
    /// </summary>
    /// <param name="message">The message as MSBuild printed it.</param>
    /// <param name="level">Either <c>error</c> or <c>warning</c>.</param>
    private static string RenderMessage(string message, string level)
    {
        var shortened = ShortenFrameworkNamespaces(StripUrls(message.Trim()));
        return TextHelpers.Truncate(shortened, level == ErrorLevel ? ErrorMessageMaxLen : WarningMessageMaxLen);
    }

    /// <summary>
    /// Removes the URLs of a message, with the whitespace before each and the parentheses or brackets that held
    /// nothing else (<c>(https://aka.ms/dotnet-warnings/SYSLIB0014)</c>): a link is for a person with a browser,
    /// and an agent reading the line cannot follow it. A URL that ended the message also takes the
    /// <c>,</c> or <c>:</c> that led into it.
    /// </summary>
    /// <param name="message">The message, trimmed.</param>
    private static string StripUrls(string message)
    {
        var stripped = UrlPattern().Replace(message, string.Empty);
        return stripped.Length == message.Length ? message : stripped.TrimEnd(' ', ',', ';', ':');
    }

    /// <summary>
    /// Drops the <c>System.</c> and <c>Microsoft.</c> namespaces from the types a message names inside single
    /// quotes: <c>'System.Collections.Generic.IEnumerable&lt;int&gt;'</c> reads <c>'IEnumerable&lt;int&gt;'</c>. A
    /// namespace that is the user's own is kept, as is one outside quotes, and so is a quoted name that follows
    /// the word "namespace": there <c>'System.Collections'</c> is a namespace, and what remains would name
    /// another. A chain at the head of a member's signature keeps the member's type as well, since
    /// <c>'System.Linq.Enumerable.Select&lt;…&gt;(…)'</c> ends in a member, not a type.
    /// </summary>
    /// <param name="message">The message, with its URLs removed.</param>
    private static string ShortenFrameworkNamespaces(string message)
    {
        return QuotedSpanPattern().Replace(message, span =>
            message.AsSpan(0, span.Index).EndsWith("namespace ", StringComparison.Ordinal)
                ? span.Value
                : FrameworkNameChainPattern().Replace(span.Value, chain => ShortenChain(chain, span.Value)));
    }

    /// <summary>The part of a dotted <c>System.</c>/<c>Microsoft.</c> name that stays: its last segment, or its last two at a signature's head.</summary>
    /// <param name="chain">The dotted name.</param>
    /// <param name="span">The quoted span the name is in, with its quotes.</param>
    private static string ShortenChain(Match chain, string span)
    {
        var atHeadOfSignature = chain.Index == 1 && span.Contains('(', StringComparison.Ordinal);
        return string.Join('.', chain.Value.Split('.').TakeLast(atHeadOfSignature ? 2 : 1));
    }

    /// <summary>Drops the extra MSBuild properties (<c>::TargetFramework=net8.0</c>) from a <c>[project]</c> suffix.</summary>
    /// <param name="project">The text inside the diagnostic's trailing brackets, empty when it has none.</param>
    private static string DropProperties(string project)
    {
        var separatorIndex = project.IndexOf("::", StringComparison.Ordinal);
        return separatorIndex < 0 ? project : project[..separatorIndex];
    }

    /// <summary>
    /// Names a project by its file, without directory or extension; either path separator splits. Only a
    /// project file names a project: the suffix of a solution-level diagnostic is the solution
    /// (<c>[/r/Sol.slnx]</c>), and that, like any other text, yields no name.
    /// </summary>
    /// <param name="projectPath">The text in the diagnostic's brackets, empty when it had none.</param>
    /// <returns>The project's name, or an empty string when <paramref name="projectPath"/> is no project file.</returns>
    private static string ProjectName(string projectPath)
    {
        var fileName = projectPath[(projectPath.LastIndexOfAny(['/', '\\']) + 1)..];
        return ProjectFileExtensions.Contains(Path.GetExtension(fileName))
            ? Path.GetFileNameWithoutExtension(fileName)
            : string.Empty;
    }

    /// <summary>
    /// Renders a diagnostic count, annotating it with "(N shown)" when this report can display
    /// fewer entries than the build reported — duplicates collapsed across target frameworks or
    /// projects, or shapes the regexes did not match. Without the annotation that shortfall is
    /// invisible and the status line reads as an authoritative, complete count.
    /// </summary>
    /// <param name="total">The count the build reported.</param>
    /// <param name="shown">The number of distinct entries rendered above the status line.</param>
    /// <param name="noun">The singular noun to render ("error" or "warning").</param>
    private static string Tally(int total, int shown, string noun)
    {
        var label = $"{total} {noun}{(total == 1 ? "" : "s")}";
        return total == shown ? label : $"{label} ({shown} shown)";
    }

    /// <summary>Renders a diagnostic code, standing in for codeless MSBuild output.</summary>
    /// <param name="code">The parsed code, empty when the diagnostic carried none.</param>
    private static string CodeLabel(string code)
    {
        return code.Length == 0 ? NoCode : code;
    }

    /// <summary>
    /// How many projects the run involved: those with an output line (<c>X -&gt; …dll</c>) together with
    /// those that reported a diagnostic, named by their project file. A build that failed prints no output
    /// line for the project that failed, so its diagnostics are the only place that project appears; a
    /// project in both counts once, as does one built for several target frameworks.
    /// </summary>
    /// <param name="parsed">The parsed run.</param>
    private static int CountProjects(ParsedOutput parsed)
    {
        var projects = new HashSet<string>(parsed.OutputProjects, StringComparer.Ordinal);
        projects.UnionWith(parsed.Diagnostics.Where(d => d.Project.Length > 0).Select(d => d.Project));
        return projects.Count;
    }

    private static string BuildContext(int projectCount, string elapsed)
    {
        if (projectCount == 0 && string.IsNullOrEmpty(elapsed))
        {
            return string.Empty;
        }

        if (string.IsNullOrEmpty(elapsed))
        {
            return $" ({projectCount} project{(projectCount == 1 ? "" : "s")})";
        }

        if (projectCount == 0)
        {
            return $" ({elapsed})";
        }

        return $" ({projectCount} project{(projectCount == 1 ? "" : "s")}, {elapsed})";
    }

    private static bool IsNoiseLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return true;
        }

        return NoiseMsbuildVersionPattern().IsMatch(line)
               || NoiseRestoringPattern().IsMatch(line)
               || NoiseRestoredPattern().IsMatch(line)
               || NoiseBuildStartedPattern().IsMatch(line)
               || NoiseBuildResultPattern().IsMatch(line);
    }

    /// <summary>
    /// Lays diagnostics out by file, the file with the most first. A diagnostic reads <c>(line,col) CODE: message</c>,
    /// and those of one file with the same code and message read once, with each of their positions and a count:
    /// <c>(96,59) (99,59) CS1503 ×2: message</c>. A file that comes to one line is printed on it, led by the file:
    /// <c>file(96,59) (99,59) CS1503 ×2: message</c>. A file with several lines gets a heading of its own,
    /// <c>file (3 errors)</c>, which counts the diagnostics, not the lines. Errors and warnings both read this way.
    /// </summary>
    /// <param name="sb">The rendered report.</param>
    /// <param name="diagnostics">The diagnostics of one level.</param>
    /// <param name="noun">The singular noun in the heading ("error" or "warning").</param>
    private static void AppendGroupedByFile(StringBuilder sb, List<Diagnostic> diagnostics, string noun)
    {
        foreach (var fileGroup in diagnostics.GroupBy(d => d.File).OrderByDescending(g => g.Count()))
        {
            var file = fileGroup.Key.Length == 0 ? NoFile : fileGroup.Key;
            var lines = fileGroup.GroupBy(d => (d.Code, d.Message)).Select(DescribeLine).ToList();

            if (lines.Count == 1)
            {
                var (positions, text) = lines[0];
                sb.AppendLine(positions.Length == 0 ? $"{file} {text}" : $"{file}{positions} {text}");
                continue;
            }

            sb.AppendLine(CultureInfo.InvariantCulture, $"{file} ({fileGroup.Count()} {noun}s)");
            foreach (var (positions, text) in lines)
            {
                sb.AppendLine(positions.Length == 0 ? $"  {text}" : $"  {positions} {text}");
            }
        }
    }

    /// <summary>
    /// One rendered line for the diagnostics of a file that share a code and a message: where they are, each
    /// position once, and what they say, with a count when there are several.
    /// </summary>
    /// <param name="group">The diagnostics, in the order they were reported.</param>
    private static (string Positions, string Text) DescribeLine(IGrouping<(string Code, string Message), Diagnostic> group)
    {
        // Tool-level diagnostics (MSBUILD, CSC) carry neither position nor, sometimes, a code; both are
        // omitted rather than rendered as the empty "(,)" and ": " stubs. A diagnostic the same file reports
        // for two projects shares its position, which is printed once.
        var positions = string.Join(' ',
            group.Where(d => d.Line.Length > 0).Select(d => $"({d.Line},{d.Col})").Distinct(StringComparer.Ordinal));
        var count = group.Count();
        var times = count > 1 ? string.Create(CultureInfo.InvariantCulture, $"×{count}") : string.Empty;
        var label = string.Join(' ', new[] { group.Key.Code, times }.Where(part => part.Length > 0));
        return (positions, label.Length == 0 ? group.Key.Message : $"{label}: {group.Key.Message}");
    }

    /// <summary>
    /// Adds <c>Top codes: CS1002 (2x), CS0103 (1x)</c> when the build reported more diagnostics than are
    /// listed (repeats collapsed, shapes not matched) and more than one code is listed: it then ranks the
    /// listed diagnostics by code, which says which codes dominate a result too long to read line by line.
    /// Its counts are of the diagnostics listed (each position of a grouped line counts), not of the diagnostics
    /// reported. With every diagnostic listed, or with a single code, it only repeats what the lines show.
    /// </summary>
    /// <param name="sb">The rendered report.</param>
    /// <param name="diagnostics">The diagnostics of one level, one line each.</param>
    /// <param name="total">The tally of that level, which exceeds <paramref name="diagnostics"/> when lines were collapsed.</param>
    private static void AppendTopCodes(StringBuilder sb, List<Diagnostic> diagnostics, int total)
    {
        if (total <= diagnostics.Count)
        {
            return;
        }

        var codes = diagnostics
            .GroupBy(d => d.Code)
            .OrderByDescending(g => g.Count())
            .ToList();
        if (codes.Count < 2)
        {
            return;
        }

        var topCodes = codes.Take(5).Select(g => $"{CodeLabel(g.Key)} ({g.Count()}x)");
        sb.AppendLine(CultureInfo.InvariantCulture, $"Top codes: {string.Join(", ", topCodes)}");
    }

    // Matches: /path/file.cs(10,5): error CS0001: message [project.csproj]
    // - file matches lazily up to the (line,col) anchor so paths containing parens (e.g. "Program Files (x86)") survive
    // - code allows lower-case prefixes (xUnit1013, IDE0055) in addition to CS/MSB, and is optional
    //   because MSBuild tasks may report "error : message" with no code at all
    // - message captures the full text and only the trailing "[project]" suffix is peeled off at end-of-line,
    //   so messages containing brackets (e.g. "'string[]'") are kept intact
    [GeneratedRegex(
        @"^\s*(?<file>.+?)\((?<line>\d+),(?<col>\d+)\):\s+(?<level>error|warning)\s+(?:(?<code>[A-Za-z]+\d+)\s*)?:\s*(?<message>.+?)(?:\s+\[(?<project>[^\]]+)\])?\s*$")]
    private static partial Regex DiagnosticPattern();

    // Matches: "MSBUILD : error MSB1001: message" or "CSC : error CS2012: message" (no file/line/col).
    // The code is optional: Exec tasks emit "EXEC : warning : could not lock config file", and
    // requiring a code dropped those from the listing and the count alike.
    [GeneratedRegex(
        @"^\s*\S*\s*:\s*(?<level>error|warning)\s+(?:(?<code>[A-Za-z]+\d+)\s*)?:\s*(?<message>.+?)(?:\s*\[(?<project>.+?)\])?\s*$")]
    private static partial Regex SimpleDiagnosticPattern();

    // A URL with the whitespace before it, and the parentheses or brackets that hold nothing else.
    [GeneratedRegex(@"\s*(?:\(\s*" + UrlBody + @"\s*\)|\[\s*" + UrlBody + @"\s*\]|" + UrlBody + ")",
        RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();

    // A span between single quotes: where a compiler names a type, a member or a namespace.
    [GeneratedRegex("'[^']*'")]
    private static partial Regex QuotedSpanPattern();

    // A dotted System./Microsoft. name, its last segment included; not one that follows a dot or a word
    // character, so a user's own MyApp.System.X and SystemClock.Now are left alone.
    [GeneratedRegex(@"(?<![\w.])(?:System|Microsoft)(?:\.[A-Za-z_]\w*)+")]
    private static partial Regex FrameworkNameChainPattern();

    // Matches: "  MyProject -> /path/to/bin/MyProject.dll" (or .exe), capturing the project's name
    [GeneratedRegex(@"^\s+(?<name>\S+) -> .+\.(dll|exe)\s*$")]
    private static partial Regex ProjectOutputPattern();

    // Matches "Time Elapsed HH:MM:SS.ff"
    [GeneratedRegex(@"Time Elapsed \d{2}:\d{2}:\d{2}\.\d+")]
    private static partial Regex TimeElapsedPattern();

    // Extracts the HH:MM:SS.ff value from a Time Elapsed line
    [GeneratedRegex(@"\d{2}:\d{2}:\d{2}\.\d+")]
    private static partial Regex TimeSpanValuePattern();

    // Noise: MSBuild version header
    [GeneratedRegex("MSBuild version", RegexOptions.IgnoreCase)]
    private static partial Regex NoiseMsbuildVersionPattern();

    // Noise: Restore progress lines
    [GeneratedRegex("Determining projects to restore|All projects are up-to-date for restore")]
    private static partial Regex NoiseRestoringPattern();

    // Noise: "Restored /path/Project.csproj (in N ms)."
    [GeneratedRegex(@"^\s*Restored .+\.csproj")]
    private static partial Regex NoiseRestoredPattern();

    // Noise: "Build started ..."
    [GeneratedRegex("Build started")]
    private static partial Regex NoiseBuildStartedPattern();

    // Noise: "Build succeeded." or "Build FAILED."
    [GeneratedRegex(@"^Build (succeeded|FAILED)\.?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex NoiseBuildResultPattern();

    // MSBuild's end-of-build tally: "    0 Warning(s)" and "    3 Error(s)". Not noise — it is the
    // only authoritative diagnostic count in the output, so it is captured rather than discarded.
    [GeneratedRegex(@"^\s+(?<count>\d+) (?<kind>Warning|Error)\(s\)\s*$")]
    private static partial Regex DeclaredCountPattern();

    /// <summary>One diagnostic as it is rendered, plus the project that reported it.</summary>
    /// <param name="File">The source file, shortened against the root; empty for tool- and project-level diagnostics.</param>
    /// <param name="Line">The line, empty when the diagnostic has no position.</param>
    /// <param name="Col">The column, empty when the diagnostic has no position.</param>
    /// <param name="Level">Either <c>error</c> or <c>warning</c>.</param>
    /// <param name="Code">The diagnostic code, empty when it carried none.</param>
    /// <param name="Message">The message as rendered: URLs removed, framework namespaces shortened, cut at the cap of its level.</param>
    /// <param name="Project">The project's file name without extension, empty when the diagnostic named none.</param>
    private sealed record Diagnostic(
        string File,
        string Line,
        string Col,
        string Level,
        string Code,
        string Message,
        string Project);

    /// <summary>Everything a single pass over the raw output yields.</summary>
    /// <param name="Diagnostics">The distinct diagnostics parsed, in the order encountered.</param>
    /// <param name="OutputProjects">The names of the projects that printed an output line, each once.</param>
    /// <param name="Elapsed">The formatted build duration, empty when absent.</param>
    /// <param name="DeclaredErrors">MSBuild's own error tally, null when the output carried none.</param>
    /// <param name="DeclaredWarnings">MSBuild's own warning tally, null when the output carried none.</param>
    private sealed record ParsedOutput(
        List<Diagnostic> Diagnostics,
        IReadOnlyCollection<string> OutputProjects,
        string Elapsed,
        int? DeclaredErrors,
        int? DeclaredWarnings);
}
