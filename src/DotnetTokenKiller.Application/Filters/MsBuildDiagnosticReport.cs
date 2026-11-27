using System.Collections.Frozen;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Domain.Files;
using DotnetTokenKiller.Domain.Filters;

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

    /// <summary>The words that introduce a link ("See", "Please refer to"), which are of no use without it.</summary>
    private const string LinkLeadIn = @"\b(?:please\s+)?(?:see|refer\s+to|visit)\b:?\s*";

    /// <summary>What a link's sentence goes on to say ("for more information about the support policy.").</summary>
    private const string LinkTail = @"(?:\s+for\s+(?:more\s+)?(?:information|details)\b(?:\s+(?:about|on|regarding)\s+[^.']*)?\.?)";

    /// <summary>Stands in for the diagnostic code on MSBuild output that carries none (Exec tasks).</summary>
    internal const string NoCode = "(no code)";

    /// <summary>Groups diagnostics that name no source file, such as tool- and project-level errors.</summary>
    private const string NoFile = "(no file)";

    private const string ErrorLevel = "error";
    private const string WarningLevel = "warning";

    /// <summary>The extensions of project files; a solution, which also ends a diagnostic's suffix, is not one.</summary>
    private static readonly FrozenSet<string> ProjectFileExtensions =
        new[] { ".csproj", ".fsproj", ".vbproj", ".proj" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// The extensions of the files, beside project files, that configure a build: MSBuild's imports
    /// (<c>Directory.Build.props</c>, <c>Directory.Packages.props</c>) and the analyzers' severities.
    /// </summary>
    private static readonly FrozenSet<string> BuildConfigurationExtensions =
        new[] { ".props", ".targets", ".globalconfig", ".ruleset" }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>The files, by name, that configure a build wherever they are.</summary>
    private static readonly FrozenSet<string> BuildConfigurationFileNames =
        new[] { ".editorconfig", "global.json", "nuget.config", "Directory.Build.rsp" }
            .ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    private readonly List<Diagnostic> _errors;
    private readonly List<Diagnostic> _warnings;
    private readonly string _context;
    private readonly int _repeatedErrors;
    private readonly int _repeatedWarnings;

    private MsBuildDiagnosticReport(ParsedOutput parsed)
    {
        _errors = [.. parsed.Diagnostics.Where(d => d.Level == ErrorLevel)];
        _warnings = [.. parsed.Diagnostics.Where(d => d.Level == WarningLevel)];
        _repeatedErrors = parsed.RepeatedErrors;
        _repeatedWarnings = parsed.RepeatedWarnings;
        _context = BuildContext(CountProjects(parsed), parsed.Elapsed);
        DeclaresCounts = parsed.DeclaredErrors is not null && parsed.DeclaredWarnings is not null;
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

    /// <summary>
    /// How many errors were parsed (after collapsing repeats), whatever MSBuild's own tally says. Zero with a
    /// non-zero <see cref="ErrorTotal"/> means the build declared errors that no pattern matched, so there is
    /// no cause to render.
    /// </summary>
    public int ParsedErrorCount => _errors.Count;

    /// <summary>Whether the output carried both of MSBuild's own tallies, "N Error(s)" and "N Warning(s)".</summary>
    public bool DeclaresCounts { get; }

    /// <summary>The larger of MSBuild's own "N Warning(s)" tally and the warnings parsed.</summary>
    public int WarningTotal { get; }

    /// <summary>
    /// Whether the report lists warnings, which it does when it parsed some and the build declared no error: the only
    /// report that the files the work in progress changed can shape, so a caller asks for them (git runs) only then. A
    /// failed build whose errors matched no pattern keeps every warning, the only body it has.
    /// </summary>
    public bool ListsWarnings => ErrorTotal == 0 && _warnings.Count > 0;

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

        var found = new DiagnosticSet();
        var outputProjects = new HashSet<string>(StringComparer.Ordinal);
        var elapsed = string.Empty;
        int? declaredErrors = null;
        int? declaredWarnings = null;

        var lines = strippedOutput.Split('\n');
        var takenThrough = -1;
        foreach (var (index, rawLine) in lines.Index())
        {
            // A "Switch:" line was joined into the diagnostic before it, which is all it is, and so was
            // the command-line echo between them.
            if (index <= takenThrough)
            {
                continue;
            }

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

            // "Build succeeded." / "Build FAILED." starts the console logger's echo of every diagnostic.
            if (NoiseBuildResultPattern().IsMatch(line))
            {
                found.EnterSummary();
                continue;
            }

            // Skip noise lines, then parse diagnostic lines with and without a file/line/col anchor
            if (!IsNoiseLine(line) && !TryAddDiagnosticLine(line, rootPath, found))
            {
                takenThrough = AddSimpleDiagnosticLine(lines, index, rootPath, found);
            }
        }

        return new MsBuildDiagnosticReport(new ParsedOutput(found.Items, outputProjects, elapsed, declaredErrors,
            declaredWarnings, found.RepeatedErrors, found.RepeatedWarnings));
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
    /// <param name="changes">
    /// The files the work in progress changed, when known. A report with no error then lists only the warnings of
    /// those files, counts the others on one line, and says in its status line how many are in changed files.
    /// </param>
    /// <returns>The report, or an empty string when the output could not be read.</returns>
    public string Render(string command, int exitCode, IReadOnlyList<string> outputs,
        WorkTreeChanges? changes = null)
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
            var counts = DeclaresCounts ? ": 0 errors, 0 warnings" : string.Empty;
            sb.AppendLine(CultureInfo.InvariantCulture, $"✓ {command}{counts}{_context}");
            return Normalize(sb);
        }

        AppendDiagnostics(sb, command, changes);
        AppendOutputs(sb, shownOutputs);
        sb.AppendLine(StatusLine(command, exitCode, changes));

        return Normalize(sb);
    }

    /// <summary>
    /// Renders the warnings of the files the work in progress changed, and those that name no file, laid out by file
    /// as <see cref="RenderDiagnostics"/> does, for a caller that reports the rest of the build its own way.
    /// </summary>
    /// <param name="changes">The changed files.</param>
    /// <returns>The lines, or an empty string when none of those files has a warning.</returns>
    public string RenderWarningsIn(WorkTreeChanges changes)
    {
        ArgumentNullException.ThrowIfNull(changes);

        var sb = new StringBuilder();
        AppendGroupedByFile(sb, WarningsIn(changes), WarningLevel);
        return Normalize(sb);
    }

    /// <summary>
    /// How many of the parsed warnings are in the files the work in progress changed; one that names no file, which
    /// <see cref="RenderWarningsIn"/> lists, is in none.
    /// </summary>
    /// <param name="changes">The changed files.</param>
    public int WarningCountIn(WorkTreeChanges changes)
    {
        ArgumentNullException.ThrowIfNull(changes);
        return CountInChangedFiles(changes);
    }

    /// <summary>
    /// The files the work in progress changed, from <paramref name="context"/>, when the report lists warnings
    /// (<see cref="ListsWarnings"/>); otherwise <see langword="null"/>, without working them out. A work tree with no
    /// change (a fresh clone, CI) singles nothing out either, and every warning is listed as without one; so does a
    /// change to the build's configuration (a project file, an imported <c>.props</c> or <c>.targets</c>,
    /// <c>.editorconfig</c>, <c>global.json</c>), which can raise a warning in any file.
    /// </summary>
    /// <param name="context">The run's context.</param>
    public WorkTreeChanges? ChangesFor(FilterContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return ListsWarnings && context.WorkTreeChanges?.Value is { Count: > 0 } changes
                             && !changes.Any(ConfiguresTheBuild)
            ? changes
            : null;
    }

    /// <summary>Whether a changed file configures the build rather than being one of its sources.</summary>
    /// <param name="path">The file's full path.</param>
    private static bool ConfiguresTheBuild(string path)
    {
        var name = Path.GetFileName(path);
        var extension = Path.GetExtension(name);
        return BuildConfigurationFileNames.Contains(name)
               || ProjectFileExtensions.Contains(extension)
               || BuildConfigurationExtensions.Contains(extension);
    }

    /// <summary>
    /// The warnings a report that knows the changes lists: those of the changed files, and those that name no file,
    /// which nothing says the change did not cause.
    /// </summary>
    /// <param name="changes">The changed files.</param>
    private List<Diagnostic> WarningsIn(WorkTreeChanges changes) => [.. _warnings.Where(d => IsListedWith(d, changes))];

    /// <summary>Whether a report that knows the changes lists <paramref name="diagnostic"/> (see <see cref="WarningsIn"/>).</summary>
    /// <param name="diagnostic">A warning.</param>
    /// <param name="changes">The changed files.</param>
    private static bool IsListedWith(Diagnostic diagnostic, WorkTreeChanges changes) =>
        diagnostic.FullPath.Length == 0 || changes.Contains(diagnostic.FullPath);

    /// <summary>How many warnings are in the changed files, which those naming no file are not.</summary>
    /// <param name="changes">The changed files.</param>
    private int CountInChangedFiles(WorkTreeChanges changes) => _warnings.Count(d => changes.Contains(d.FullPath));

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
        AppendDiagnostics(sb, string.Empty, null);
        return Normalize(sb);
    }

    /// <summary>Renders the diagnostics body: the errors, or else the warnings, laid out by file.</summary>
    /// <param name="sb">The rendered report.</param>
    /// <param name="command">The command the report is about, e.g. <c>dotnet build</c>, whose log the report names.</param>
    /// <param name="changes">The files the work in progress changed, when known.</param>
    private void AppendDiagnostics(StringBuilder sb, string command, WorkTreeChanges? changes)
    {
        // Errors and warnings are laid out the same way. A build with errors lists only those, so the
        // warnings do not bury them; it says how many it left out. Without errors, a run that knows which files
        // changed lists their warnings, which the change may have caused, and only counts the others.
        if (_errors.Count == 0 && changes is not null)
        {
            AppendGroupedByFile(sb, WarningsIn(changes), WarningLevel);
            AppendWarningsElsewhere(sb, [.. _warnings.Where(d => !IsListedWith(d, changes))], command);
            return;
        }

        if (_errors.Count == 0)
        {
            AppendGroupedByFile(sb, _warnings, WarningLevel);
            AppendTopCodes(sb, _warnings, WarningTotal);
            return;
        }

        AppendGroupedByFile(sb, _errors, ErrorLevel);
        AppendTopCodes(sb, _errors, ErrorTotal);
        // Stated from the declared count, so it holds even when MSBuild's warnings matched no line.
        if (WarningTotal > 0)
        {
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"{CountLabel(WarningTotal, WarningLevel)} not shown (dtk log)");
        }
    }

    /// <summary>
    /// The single status line a report with diagnostics ends with. A non-zero exit names itself, so a
    /// failed run whose errors were never matched (warnings only) still reads as the failure it is
    /// rather than as "0 errors"; the verdict is stated here once, never beside a second verdict line.
    /// An exit code of zero stays the verdict even when errors were counted, but such a line carries a
    /// warning glyph rather than a tick, which would sit misleadingly beside the error count. Warnings
    /// hidden behind listed errors are counted plainly, since the body already says they are not shown.
    /// </summary>
    /// <param name="command">The command named in the line.</param>
    /// <param name="exitCode">The process exit code; the sole source of truth for the verdict.</param>
    /// <param name="changes">The files the work in progress changed, when known.</param>
    private string StatusLine(string command, int exitCode, WorkTreeChanges? changes)
    {
        var warningTally = _errors.Count > 0
            ? CountLabel(WarningTotal, WarningLevel)
            : Tally(WarningTotal, _warnings.Count, _repeatedWarnings, WarningLevel, listsAll: changes is null)
              + InChangedFiles(changes);
        var tally = $"{Tally(ErrorTotal, _errors.Count, _repeatedErrors, ErrorLevel)}, {warningTally}";
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

    /// <summary>
    /// Parses a diagnostic without line/col (e.g., "MSBUILD : error MSB1001: message", or NuGet's
    /// "/r/App.csproj : warning NU1903: message"). What comes before the level names a file when it is a path (a project
    /// file, for NuGet), and a tool otherwise (<c>MSBUILD</c>, <c>CSC</c>, <c>EXEC</c>), which names no file. MSBuild names the
    /// argument it rejected on a line of its own after such a diagnostic (<c>Switch: nothere.csproj</c> after
    /// MSB1009), and that line is where the cause is: it joins the message. MSB1001 echoes the full command line
    /// and a <c>Switches appended by response files:</c> heading first; the <c>Switch:</c> line after them joins too.
    /// </summary>
    /// <param name="lines">The output's lines.</param>
    /// <param name="index">The index of the line to parse.</param>
    /// <param name="rootPath">The root the report shortens paths against.</param>
    /// <param name="found">The diagnostics parsed so far.</param>
    /// <returns>
    /// The index of the <c>Switch:</c> line taken into the message, through which the following lines are consumed,
    /// or <c>-1</c> when none was.
    /// </returns>
    private static int AddSimpleDiagnosticLine(string[] lines, int index, string rootPath, DiagnosticSet found)
    {
        var simpleDiagMatch = SimpleDiagnosticPattern().Match(lines[index].TrimEnd('\r'));
        if (!simpleDiagMatch.Success)
        {
            return -1;
        }

        var rawMessage = simpleDiagMatch.Groups["message"].Value.Trim();
        var switchIndex = FindSwitchLine(lines, index + 1);
        if (switchIndex >= 0)
        {
            rawMessage = $"{rawMessage} {lines[switchIndex].Trim()}";
        }

        var level = simpleDiagMatch.Groups["level"].Value;
        var origin = simpleDiagMatch.Groups["origin"].Value;
        var file = NamesAFile(origin) ? origin : string.Empty;
        var fullPath = file.Length == 0 ? string.Empty : FullPathOf(file, rootPath);
        var project = DropProperties(simpleDiagMatch.Groups["project"].Value);
        var simpleDiagnostic = new Diagnostic(
            fullPath.Length == 0 ? file : TextHelpers.ShortenPath(fullPath, rootPath),
            fullPath,
            string.Empty,
            string.Empty,
            level,
            simpleDiagMatch.Groups["code"].Value,
            RenderMessage(rawMessage, level),
            ProjectName(project.Length == 0 ? file : project));

        // Keyed off the message as printed for the same reason as the file-anchored diagnostics, and off what
        // reported it: two projects that reference the same vulnerable package raise the same NU1903, once each.
        found.Add(simpleDiagnostic, $"{origin}:{simpleDiagnostic.Code}:{rawMessage}", lines[index].Trim());

        return switchIndex;
    }

    /// <summary>
    /// Whether what precedes a position-less diagnostic's level is a file (<c>/r/App.csproj</c>, <c>src\\App.csproj</c>,
    /// <c>App.csproj</c>) rather than the tool that reported it (<c>MSBUILD</c>, <c>CSC</c>, <c>EXEC</c>).
    /// </summary>
    /// <param name="origin">The text before the level, empty when there is none.</param>
    private static bool NamesAFile(string origin) =>
        origin.IndexOfAny(['/', '\\']) >= 0 || Path.HasExtension(origin);

    /// <summary>
    /// Finds the <c>Switch:</c> line that names what a command-line diagnostic rejected: the line at
    /// <paramref name="start"/>, or the first after MSBuild's echo of the command line and its response-file heading.
    /// </summary>
    /// <param name="lines">The output's lines.</param>
    /// <param name="start">The index of the line after the diagnostic.</param>
    /// <returns>The index of the <c>Switch:</c> line, or <c>-1</c> when another line comes first.</returns>
    private static int FindSwitchLine(string[] lines, int start)
    {
        for (var i = start; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (SwitchContinuationPattern().IsMatch(line))
            {
                return i;
            }

            if (!CommandLineEchoPattern().IsMatch(line))
            {
                return -1;
            }
        }

        return -1;
    }

    private static bool TryAddDiagnosticLine(string line, string rootPath, DiagnosticSet found)
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

        var rawMessage = diagMatch.Groups["message"].Value.Trim();
        var level = diagMatch.Groups["level"].Value;
        var file = diagMatch.Groups["file"].Value.Trim();
        var diagnostic = new Diagnostic(
            TextHelpers.ShortenPath(file, rootPath),
            FullPathOf(file, rootPath),
            diagMatch.Groups["line"].Value,
            diagMatch.Groups["col"].Value,
            level,
            diagMatch.Groups["code"].Value,
            RenderMessage(rawMessage, level),
            ProjectName(projectPath));

        // Key off the message as MSBuild printed it, not as it is rendered. What collapses here is one
        // diagnostic reported again (once per target framework, or by a path that shortens to the same
        // relative one); two diagnostics that only render alike (advisories differing by their link,
        // messages differing past the cap) are two, and the report groups them with a count rather than
        // losing one. The message is in the key because the code is optional: without it, two unrelated
        // codeless diagnostics reported at the same position would collapse into one.
        found.Add(diagnostic,
            $"{projectPath}:{diagnostic.File}({diagnostic.Line},{diagnostic.Col}):{diagnostic.Code}:{rawMessage}",
            line.Trim());
        return true;
    }

    /// <summary>
    /// <paramref name="file"/> as a full path, resolved against <paramref name="rootPath"/> when relative, or an
    /// empty string when it is no valid path; it tells whether the file is one the work in progress changed.
    /// </summary>
    /// <param name="file">The file a diagnostic names, as MSBuild printed it.</param>
    /// <param name="rootPath">The root the report shortens paths against.</param>
    private static string FullPathOf(string file, string rootPath)
    {
        try
        {
            return Path.GetFullPath(file, Path.GetFullPath(rootPath));
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return string.Empty;
        }
    }

    /// <summary>
    /// The message as it is rendered: its URLs removed, the framework namespaces in its quoted names shortened,
    /// then cut at the cap of its level. The cap comes last, so a link or a namespace costs none of it.
    /// </summary>
    /// <param name="message">The message as MSBuild printed it, trimmed.</param>
    /// <param name="level">Either <c>error</c> or <c>warning</c>.</param>
    private static string RenderMessage(string message, string level)
    {
        var shortened = ShortenFrameworkNamespaces(StripUrls(message));
        return TextHelpers.Truncate(shortened, level == ErrorLevel ? ErrorMessageMaxLen : WarningMessageMaxLen);
    }

    /// <summary>
    /// Removes the URLs of a message, with the whitespace before each and the parentheses or brackets that held
    /// nothing else (<c>(https://aka.ms/dotnet-warnings/SYSLIB0014)</c>): a link is for a person with a browser,
    /// and an agent reading the line cannot follow it. What introduced the link goes with it, so no
    /// <c>See for more information.</c> or <c>(see)</c> is left behind: "See", "Please refer to" and "Visit" before
    /// it, and a "for more information" sentence after. A URL that ended the message also takes the
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
    /// quotes: <c>'System.Collections.Generic.IEnumerable&lt;int&gt;'</c> reads <c>'IEnumerable&lt;int&gt;'</c>. Only a
    /// quoted name that is a type expression is shortened; a text rule cannot tell a namespace from a type in a
    /// dotted name, so the shortening is limited to where one is safe (see <see cref="IsTypeExpression"/>), and
    /// never makes the names of one message alike: if two quoted names it tells apart would read the same
    /// (CS0104's <c>'System.Threading.Timer'</c> and <c>'System.Timers.Timer'</c>), the message is left as written.
    /// </summary>
    /// <param name="message">The message, with its URLs removed.</param>
    private static string ShortenFrameworkNamespaces(string message)
    {
        var spans = QuotedSpanPattern().Matches(message);
        var shortened = spans.Select(span => ShortenSpan(message, span)).ToList();
        if (shortened.Distinct(StringComparer.Ordinal).Count()
            != spans.Select(span => span.Value).Distinct(StringComparer.Ordinal).Count())
        {
            return message;
        }

        var next = 0;
        return QuotedSpanPattern().Replace(message, _ => shortened[next++]);
    }

    /// <summary>A quoted span with its framework namespaces dropped, or as it was when it is not a type expression.</summary>
    /// <param name="message">The whole message.</param>
    /// <param name="span">The quoted span in it, quotes included.</param>
    private static string ShortenSpan(string message, Match span)
    {
        return IsTypeExpression(message, span)
            ? FrameworkNameChainPattern().Replace(span.Value, chain => ShortenChain(chain, span.Value))
            : span.Value;
    }

    /// <summary>
    /// Whether a quoted span can be taken for a type. It cannot when it carries an assembly identity
    /// (<c>'System.Runtime, Version=4.2.2.0, …'</c>), a path or a file (<c>'/repo/Microsoft.Foo.dll'</c>,
    /// <c>'Microsoft.Foo.dll'</c>), or follows the word that says what it names: a package
    /// (<c>Package 'System.Text.Json' 8.0.0</c>), an assembly or a namespace
    /// (<c>the namespace 'System.Collections'</c>, where dropping "System." would name another).
    /// </summary>
    /// <param name="message">The whole message.</param>
    /// <param name="span">The quoted span in it, quotes included.</param>
    private static bool IsTypeExpression(string message, Match span)
    {
        var name = span.Value.AsSpan(1, span.Length - 2);
        if (name.Contains(", Version=", StringComparison.Ordinal)
            || name.IndexOfAny('/', '\\') >= 0
            || name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            || name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return !NamingWordPattern().IsMatch(message.AsSpan(0, span.Index));
    }

    /// <summary>
    /// What stays of a dotted <c>System.</c>/<c>Microsoft.</c> name: its last segment. The head of a member's
    /// signature (<c>'System.Linq.Enumerable.Select&lt;…&gt;(…)'</c>, <c>'System.Collections.Generic.List&lt;int&gt;.List(int)'</c>)
    /// is left whole: it ends in a member, and which of its dots end the namespace is not for a text rule to say.
    /// </summary>
    /// <param name="chain">The dotted name.</param>
    /// <param name="span">The quoted span the name is in, with its quotes.</param>
    private static string ShortenChain(Match chain, string span)
    {
        var atHeadOfSignature = chain.Index == 1 && span.Contains('(', StringComparison.Ordinal);
        return atHeadOfSignature ? chain.Value : chain.Value[(chain.Value.LastIndexOf('.') + 1)..];
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
    /// <param name="repeated">How many lines repeated an entry already parsed.</param>
    /// <param name="noun">The singular noun to render ("error" or "warning").</param>
    /// <param name="listsAll">
    /// Whether the report lists every entry parsed; one that lists only the warnings of changed files does not.
    /// </param>
    /// <remarks>
    /// "(N distinct)" when the lines that repeated an entry account for the difference: MSBuild counts a
    /// multi-targeted project's diagnostic once per target framework, so every one it counted is listed, once, and
    /// nothing is missing. "(N shown)" otherwise: some of what MSBuild counted matched no pattern. A report that
    /// lists only some of them says "(N parsed)", which says the same without contradicting its own
    /// "not shown" line.
    /// </remarks>
    private static string Tally(int total, int shown, int repeated, string noun, bool listsAll = true)
    {
        var label = CountLabel(total, noun);
        if (total == shown)
        {
            return label;
        }

        if (shown + repeated >= total)
        {
            return $"{label} ({shown} distinct)";
        }

        return listsAll ? $"{label} ({shown} shown)" : $"{label} ({shown} parsed)";
    }

    /// <summary>
    /// What the status line says of the warnings in changed files: <c>, 2 in changed files</c>, or
    /// <c>, none in changed files</c>. Nothing when the changes are unknown or no warning was parsed.
    /// </summary>
    /// <param name="changes">The files the work in progress changed, when known.</param>
    private string InChangedFiles(WorkTreeChanges? changes)
    {
        if (changes is null || _warnings.Count == 0)
        {
            return string.Empty;
        }

        var count = CountInChangedFiles(changes);
        return count == 0
            ? ", none in changed files"
            : string.Create(CultureInfo.InvariantCulture, $", {count} in changed files");
    }

    /// <summary>
    /// Counts the warnings outside the changed files on one line, with their most frequent codes and the command
    /// that lists them all, laid out as without changes:
    /// <c>164 warnings in unchanged files not shown (dtk log build --full | dtk pipe build): CA1822 (50x), …</c>.
    /// </summary>
    /// <param name="sb">The rendered report.</param>
    /// <param name="warnings">The warnings outside the changed files.</param>
    /// <param name="command">The command the report is about, e.g. <c>dotnet build</c>.</param>
    private static void AppendWarningsElsewhere(StringBuilder sb, List<Diagnostic> warnings, string command)
    {
        if (warnings.Count == 0)
        {
            return;
        }

        var codes = warnings
            .GroupBy(d => CodeLabel(d.Code))
            .Select(g => (Code: g.Key, Count: g.Count()))
            .OrderByDescending(c => c.Count)
            .ThenBy(c => c.Code, StringComparer.Ordinal)
            .ToList();
        var named = codes.Take(5).Select(c => $"{c.Code} ({c.Count}x)");
        var more = codes.Count > 5 ? ", …" : string.Empty;
        var verb = command.StartsWith("dotnet ", StringComparison.Ordinal) ? command["dotnet ".Length..] : command;
        sb.AppendLine(CultureInfo.InvariantCulture,
            $"{CountLabel(warnings.Count, WarningLevel)} in unchanged files not shown (dtk log {verb} --full | dtk pipe {verb}): {string.Join(", ", named)}{more}");
    }

    /// <summary>Renders a diagnostic count with its noun, singular for one: "1 warning", "6 warnings".</summary>
    /// <param name="total">The count the build reported.</param>
    /// <param name="noun">The singular noun to render ("error" or "warning").</param>
    private static string CountLabel(int total, string noun) => $"{total} {noun}{(total == 1 ? "" : "s")}";

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
    /// position once and in line and column order, whatever order the build reported them in (the first
    /// <see cref="TextHelpers.MaxPositionsPerLine"/> of them, the rest counted), and what they say, with the count
    /// of all of them when there are several.
    /// </summary>
    /// <param name="group">The diagnostics, in the order they were reported.</param>
    private static (string Positions, string Text) DescribeLine(IGrouping<(string Code, string Message), Diagnostic> group)
    {
        // Tool-level diagnostics (MSBUILD, CSC) carry neither position nor, sometimes, a code; both are
        // omitted rather than rendered as the empty "(,)" and ": " stubs. A diagnostic the same file reports
        // for two projects shares its position, which is printed once.
        var positions = TextHelpers.JoinCapped(
            [
                .. group.Where(d => d.Line.Length > 0)
                    .Select(d => (Line: TextHelpers.ParseNumberOrMax(d.Line), Col: TextHelpers.ParseNumberOrMax(d.Col),
                        Text: $"({d.Line},{d.Col})"))
                    .OrderBy(p => p.Line)
                    .ThenBy(p => p.Col)
                    .Select(p => p.Text)
                    .Distinct(StringComparer.Ordinal),
            ],
            " ");
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
        @"^\s*(?<origin>\S*)\s*:\s*(?<level>error|warning)\s+(?:(?<code>[A-Za-z]+\d+)\s*)?:\s*(?<message>.+?)(?:\s*\[(?<project>.+?)\])?\s*$")]
    private static partial Regex SimpleDiagnosticPattern();

    // The line MSBuild prints after a command-line diagnostic to name the argument it rejected:
    // "Switch: nothere.csproj".
    [GeneratedRegex(@"^Switch: \S.*$")]
    private static partial Regex SwitchContinuationPattern();

    // What MSBuild prints between MSB1001 and its "Switch:" line: the command line it was given, the heading of
    // the switches its response files added, and each of those ("'-p:Foo=Bar' came from '/r/Directory.Build.rsp'").
    [GeneratedRegex(@"^(?:\s+(?:Full command line: |Switches appended by response files:)|'.*' came from '.*'$)")]
    private static partial Regex CommandLineEchoPattern();

    // A URL with the whitespace before it: inside parentheses that hold nothing else (or only the word that
    // introduces it), inside brackets that hold nothing else, with its introduction and the sentence it
    // starts ("See https://… for more information."), or bare.
    [GeneratedRegex(
        @"\s*(?:\(\s*(?:" + LinkLeadIn + ")?" + UrlBody + @"\s*\)|\[\s*" + UrlBody + @"\s*\]|" + LinkLeadIn
        + UrlBody + LinkTail + "?|" + UrlBody + ")",
        RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();

    // The word before a quoted name that says what the name is: a package, an assembly or a namespace.
    [GeneratedRegex(@"(?<!\w)(?:package|assembly|namespace)\s\z", RegexOptions.IgnoreCase)]
    private static partial Regex NamingWordPattern();

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
    /// <param name="File">
    /// The source file, shortened against the root: the project file of a project-level diagnostic, which has no
    /// position; empty for a tool-level one (<c>MSBUILD : error …</c>).
    /// </param>
    /// <param name="FullPath">The source file as a full path; empty when <paramref name="File"/> is.</param>
    /// <param name="Line">The line, empty when the diagnostic has no position.</param>
    /// <param name="Col">The column, empty when the diagnostic has no position.</param>
    /// <param name="Level">Either <c>error</c> or <c>warning</c>.</param>
    /// <param name="Code">The diagnostic code, empty when it carried none.</param>
    /// <param name="Message">The message as rendered: URLs removed, framework namespaces shortened, cut at the cap of its level.</param>
    /// <param name="Project">The project's file name without extension, empty when the diagnostic named none.</param>
    private sealed record Diagnostic(
        string File,
        string FullPath,
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
    /// <param name="RepeatedErrors">How many error lines repeated one already parsed.</param>
    /// <param name="RepeatedWarnings">How many warning lines repeated one already parsed.</param>
    private sealed record ParsedOutput(
        List<Diagnostic> Diagnostics,
        IReadOnlyCollection<string> OutputProjects,
        string Elapsed,
        int? DeclaredErrors,
        int? DeclaredWarnings,
        int RepeatedErrors,
        int RepeatedWarnings);

    /// <summary>
    /// The distinct diagnostics parsed so far, and how many lines of each level repeated one of them, which MSBuild
    /// counts: a multi-targeted project's copy for another target framework, or a warning logged twice (NuGet reports a
    /// project's NU1903 at restore and again when the build reads the assets file, word for word). The console
    /// logger's echo of every diagnostic under <c>Build succeeded.</c> or <c>Build FAILED.</c> is no such repeat: a
    /// line it prints again word for word was counted once.
    /// </summary>
    private sealed class DiagnosticSet
    {
        private readonly HashSet<string> _keys = new(StringComparer.Ordinal);
        private readonly HashSet<string> _lines = new(StringComparer.Ordinal);
        private bool _inSummary;

        public List<Diagnostic> Items { get; } = [];

        public int RepeatedErrors { get; private set; }

        public int RepeatedWarnings { get; private set; }

        /// <summary>Notes that the lines from here on are the console logger's echo of the diagnostics.</summary>
        public void EnterSummary() => _inSummary = true;

        /// <summary>
        /// Adds a diagnostic, or counts it as a repeat of the one already added under its key unless it echoes, in the
        /// summary, a line already read.
        /// </summary>
        /// <param name="diagnostic">The diagnostic.</param>
        /// <param name="key">What makes it the same diagnostic as another.</param>
        /// <param name="line">The line it was read from, trimmed.</param>
        public void Add(Diagnostic diagnostic, string key, string line)
        {
            var newLine = _lines.Add(line);
            if (_keys.Add(key))
            {
                Items.Add(diagnostic);
                return;
            }

            // An echo of a line already read stands for nothing MSBuild counted twice.
            if (_inSummary && !newLine)
            {
                return;
            }

            if (diagnostic.Level == ErrorLevel)
            {
                RepeatedErrors++;
            }
            else
            {
                RepeatedWarnings++;
            }
        }
    }
}
