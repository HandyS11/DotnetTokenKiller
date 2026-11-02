using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Domain.Filters;

namespace DotnetTokenKiller.Application.Filters;

/// <summary>
/// Condenses dotnet test output to a concise pass/fail summary. A test project that fails to compile
/// gets the grouped diagnostics <c>dotnet build</c> prints for it, ahead of the other projects' results,
/// and the status line of a run that compiled with warnings ends in their count and top codes.
/// </summary>
/// <param name="rootPath">Optional root path used to shorten file paths in stack traces and diagnostics.</param>
public sealed partial class DotnetTestFilter(string? rootPath = null) : IOutputFilter
{
    private const int MaxFailures = 15;
    private const int MessageMaxLen = 300;

    /// <summary>How many warning codes the status line names before it trails off.</summary>
    private const int MaxWarningCodes = 3;

    /// <summary>Cap on the innermost exception of a nested message, so that it survives a long outer one.</summary>
    private const int InnerExceptionMaxLen = 200;

    /// <summary>What a failure reads when Reqnroll found no binding for a step, followed by the steps.</summary>
    private const string ReqnrollPendingPrefix = "No matching step definition: ";

    /// <summary>Microsoft.Testing.Platform's exit code for a run in which zero tests ran.</summary>
    private const int MtpZeroTestsExitCode = 8;

    /// <summary>Name of the regex capture group holding a test/summary duration.</summary>
    private const string DurationGroup = "duration";

    private string RootPath => rootPath ?? Environment.CurrentDirectory;

    /// <summary>Applies the filter to the test output.</summary>
    /// <param name="strippedOutput">The test output to filter, with ANSI escape sequences already stripped.</param>
    /// <param name="exitCode">
    /// The process exit code; the sole source of truth for the success/failure verdict, except that
    /// Microsoft.Testing.Platform's exit code 8 with its "Zero tests ran" verdict reads as a zero-tests warning.
    /// </param>
    public string Apply(string strippedOutput, int exitCode)
    {
        if (string.IsNullOrEmpty(strippedOutput))
        {
            return string.Empty;
        }

        var lines = strippedOutput.Split('\n');
        var state = ParseLines(lines);

        // The build `dotnet test` runs prints the same diagnostics `dotnet build` does, so they are read the
        // same way: a project that fails to compile, and the warnings of the ones that do, on any exit code.
        var buildReport = MsBuildDiagnosticReport.Parse(strippedOutput, RootPath, IsTestTextDiagnostic);
        return FormatOutput(state, exitCode, buildReport);
    }

    /// <summary>
    /// Whether <paramref name="line"/> only looks like a compiler diagnostic: it has the
    /// <c>file(line,col): error …</c> shape but is not closed by MSBuild's <c>[project.csproj]</c> suffix,
    /// which every diagnostic of a <c>dotnet test</c> build carries. A test's own text has the shape
    /// without the suffix, wherever the runner prints it: a failure message, xUnit's repeat of it behind
    /// its <c>[xUnit.net hh:mm:ss.ff]</c> prefix, or the test app's standard output. Tool-level
    /// diagnostics (<c>MSBUILD : error …</c>) have no file position and are not judged here.
    /// </summary>
    /// <remarks>
    /// The cost: under the terminal logger (<c>--tl:on</c>) compile errors print without the suffix, so
    /// they are not read and such a run falls back to the raw tail, and its warnings are not counted.
    /// </remarks>
    /// <param name="line">One line of output, without its carriage return.</param>
    private static bool IsTestTextDiagnostic(string line)
    {
        return FileAnchoredDiagnosticPattern().IsMatch(line) && !ProjectSuffixPattern().IsMatch(line);
    }

    private ParseState ParseLines(string[] lines)
    {
        var state = new ParseState();
        var i = 0;
        while (i < lines.Length)
        {
            i = ParseLine(lines, i, state);
        }

        ResolveTotals(state);
        return state;
    }

    /// <summary>Parses the line at <paramref name="i"/>, and any lines that belong to it.</summary>
    /// <param name="lines">The output's lines.</param>
    /// <param name="i">The index of the line to parse.</param>
    /// <param name="state">The state accumulated so far.</param>
    /// <returns>The index of the next line to parse.</returns>
    private int ParseLine(string[] lines, int i, ParseState state)
    {
        var line = lines[i].TrimEnd('\r');

        if (TryParseSingleLine(line, state) || TryParseConsoleLoggerLine(line, state) || TryParseListLine(line, state))
        {
            return i + 1;
        }

        var mtpRunSummaryMatch = MtpRunSummaryHeaderPattern().Match(line);
        if (mtpRunSummaryMatch.Success)
        {
            return ParseMtpRunSummary(lines, i, mtpRunSummaryMatch, state);
        }

        var failedHeaderMatch = FailedTestHeaderPattern().Match(line);
        if (failedHeaderMatch.Success)
        {
            return ParseFailure(lines, i, failedHeaderMatch, state);
        }

        var mtpFailedMatch = MtpFailedTestPattern().Match(line);
        return mtpFailedMatch.Success ? ParseMtpFailure(lines, i, mtpFailedMatch, state) : i + 1;
    }

    /// <summary>Records <paramref name="line"/> when it is one of the single-line shapes: a summary or a test count.</summary>
    /// <param name="line">The line, without its carriage return.</param>
    /// <param name="state">The state accumulated so far.</param>
    /// <returns>Whether the line was recognised.</returns>
    private static bool TryParseSingleLine(string line, ParseState state)
    {
        var summaryMatch = SummaryPattern().Match(line);
        if (summaryMatch.Success)
        {
            AccumulateSummary(summaryMatch, state);
            return true;
        }

        var terminalLoggerSummaryMatch = TerminalLoggerSummaryPattern().Match(line);
        if (terminalLoggerSummaryMatch.Success)
        {
            AccumulateTerminalLoggerSummary(terminalLoggerSummaryMatch, state);
            return true;
        }

        if (MtpRunningTestsPattern().IsMatch(line))
        {
            state.MtpAssembliesRun++;
            return true;
        }

        var noTestsMatch = NoTestsPattern().Match(line);
        if (!noTestsMatch.Success)
        {
            noTestsMatch = MtpAssemblyZeroTestsPattern().Match(line);
        }

        if (noTestsMatch.Success)
        {
            RecordNoTests(noTestsMatch, state);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Records an assembly that matched or ran no test, with what the line says about it: the filter
    /// expression (VSTest's "No test matches" line) and the assembly's file name. A run keeps the first
    /// filter it sees and each assembly once, in order of appearance.
    /// </summary>
    /// <param name="match">A match of <see cref="NoTestsPattern"/> or <see cref="MtpAssemblyZeroTestsPattern"/>.</param>
    /// <param name="state">The state accumulated so far.</param>
    private static void RecordNoTests(Match match, ParseState state)
    {
        state.ZeroTestsFound = true;

        if (match.Groups["filter"] is { Success: true } filter)
        {
            state.ZeroTestsFilter ??= filter.Value;
        }

        if (match.Groups["sources"] is not { Success: true } sources)
        {
            return;
        }

        var names = SourceFileNamePattern().Matches(sources.Value).Select(m => m.Value.Trim());
        state.ZeroTestsAssemblies.AddRange(names.Except(state.ZeroTestsAssemblies));
    }

    /// <summary>
    /// Records <paramref name="line"/> when it belongs to the normal/detailed console logger's run summary:
    /// <c>Test Run Successful.</c> (or <c>Failed.</c>) and, below it, <c>Passed:</c>, <c>Failed:</c>,
    /// <c>Skipped:</c> and <c>Total time:</c>. That logger prints no "Passed!" line, so the block is the
    /// assembly's only summary. Each line is read on its own, not as one contiguous block, because the
    /// block does not arrive intact: parallel projects interleave their output line by line, and VSTest
    /// writes <c>Test Run Failed.</c> to stderr, which dtk's capture appends after stdout, so a failed
    /// run's header comes after its counts. The counts are kept apart and only used once a header was seen.
    /// </summary>
    /// <param name="line">The line, without its carriage return.</param>
    /// <param name="state">The state accumulated so far.</param>
    /// <returns>Whether the line was recognised.</returns>
    private static bool TryParseConsoleLoggerLine(string line, ParseState state)
    {
        if (TestRunBlockHeaderPattern().IsMatch(line))
        {
            state.TestRunBlocks++;
            state.ProjectCount++;
            return true;
        }

        var countMatch = TestRunCountPattern().Match(line);
        if (countMatch.Success)
        {
            AccumulateTestRunCount(countMatch, state);
            return true;
        }

        var timeMatch = TestRunTotalTimePattern().Match(line);
        if (!timeMatch.Success)
        {
            return false;
        }

        state.ConsoleLogger.DurationMs += TotalTimeToMs(
            double.Parse(timeMatch.Groups["value"].Value, CultureInfo.InvariantCulture),
            timeMatch.Groups["unit"].Value);
        return true;
    }

    private static void AccumulateTestRunCount(Match countMatch, ParseState state)
    {
        var count = int.Parse(countMatch.Groups["count"].Value, CultureInfo.InvariantCulture);
        switch (countMatch.Groups["key"].Value)
        {
            case "Passed":
                state.ConsoleLogger.Passed += count;
                break;
            case "Failed":
                state.ConsoleLogger.Failed += count;
                break;
            default:
                state.ConsoleLogger.Skipped += count;
                break;
        }
    }

    /// <summary>
    /// Records <paramref name="line"/> when it is part of a <c>--list-tests</c> listing: the
    /// <c>The following Tests are available:</c> header (one per project) or an indented test name. Parallel
    /// projects print their headers first and their names afterwards, with other lines in between, so a
    /// name is recognised by its indent anywhere after the first header, not only directly below one.
    /// </summary>
    /// <param name="line">The line, without its carriage return.</param>
    /// <param name="state">The state accumulated so far.</param>
    /// <returns>Whether the line was recognised.</returns>
    private static bool TryParseListLine(string line, ParseState state)
    {
        if (TestListHeaderPattern().IsMatch(line))
        {
            state.ListedProjects++;
            return true;
        }

        if (state.ListedProjects == 0)
        {
            return false;
        }

        var nameMatch = ListedTestNamePattern().Match(line);
        if (!nameMatch.Success)
        {
            return false;
        }

        state.ListedTests.Add(nameMatch.Groups["name"].Value);
        return true;
    }

    private int ParseFailure(string[] lines, int i, Match failedHeaderMatch, ParseState state)
    {
        var testName = failedHeaderMatch.Groups["name"].Value.Trim();
        var duration = failedHeaderMatch.Groups[DurationGroup].Value;
        i++;

        // Skip "Error Message:" label
        if (i < lines.Length && ErrorMessageLabelPattern().IsMatch(lines[i].TrimEnd('\r')))
        {
            i++;
        }

        var (msgLines, afterMsg) = CollectMessageLines(lines, i);
        i = afterMsg;

        // Skip "Stack Trace:" label
        if (i < lines.Length && StackTraceLabelPattern().IsMatch(lines[i].TrimEnd('\r')))
        {
            i++;
        }

        var detailStart = i;
        var (sourceRef, afterStack) = FindSourceRef(lines, i);
        i = afterStack;

        var message = ReqnrollPendingMessage(msgLines, lines, detailStart, afterStack) ?? CompactMessage(msgLines);
        state.Failures.Add(new FailureInfo(testName, duration, message, sourceRef));
        return i;
    }

    private int ParseMtpFailure(string[] lines, int i, Match failedMatch, ParseState state)
    {
        var testName = failedMatch.Groups["name"].Value.Trim();
        var duration = failedMatch.Groups[DurationGroup].Value.Trim();
        i++;

        // MTP has no "Error Message:"/"Stack Trace:" labels — collect the indented continuation
        // lines that follow the failure header until the next failure line, a summary, or a
        // non-indented line, pulling a source reference out of the first user stack frame we see.
        // MSTest prints the message, then "from <assembly> (<tfm>|<arch>)", then the message again
        // above the stack trace: everything after the "from" line is read for frames only. xUnit v3
        // prints "from" first and the message once after it, so a "from" line that precedes any
        // message line leaves the message open until the first stack frame, a "--- End of … ---"
        // marker or the test's captured "Standard output"/"Error output" (a timed-out test has no frame).
        var msgLines = new List<string>();
        var sourceRef = string.Empty;
        var messageEnded = false;
        var messageFollowsFrom = false;
        while (i < lines.Length)
        {
            var current = lines[i].TrimEnd('\r');
            if (current.Length == 0 || !char.IsWhiteSpace(current[0])
                                    || MtpFailedTestPattern().IsMatch(current)
                                    || FailedTestHeaderPattern().IsMatch(current)
                                    || TerminalLoggerSummaryPattern().IsMatch(current)
                                    || SummaryPattern().IsMatch(current))
            {
                break;
            }

            if (string.IsNullOrEmpty(sourceRef))
            {
                sourceRef = TryGetSourceRef(current);
            }

            if (MtpFromAssemblyPattern().IsMatch(current))
            {
                messageFollowsFrom = msgLines.Count == 0;
                messageEnded = !messageFollowsFrom;
            }
            else if (messageFollowsFrom && (StackFrameLinePattern().IsMatch(current)
                                            || MtpMessageEndPattern().IsMatch(current)))
            {
                messageEnded = true;
            }
            else if (!messageEnded && !StackFrameLinePattern().IsMatch(current)
                                   && !string.IsNullOrWhiteSpace(current))
            {
                msgLines.Add(current.Trim());
            }

            i++;
        }

        state.Failures.Add(new FailureInfo(testName, duration, CompactMessage(msgLines), sourceRef));
        return i;
    }

    private static (List<string> Lines, int NextIndex) CollectMessageLines(string[] lines, int i)
    {
        var msgLines = new List<string>();
        while (i < lines.Length)
        {
            var current = lines[i].TrimEnd('\r');
            if (StackTraceLabelPattern().IsMatch(current) || EndsVsTestFailure(current))
            {
                break;
            }

            var trimmed = current.Trim();
            if (!string.IsNullOrEmpty(trimmed))
            {
                msgLines.Add(trimmed);
            }

            i++;
        }

        return (msgLines, i);
    }

    /// <summary>
    /// Reads the failure of a Reqnroll scenario with undefined steps. Its message is only "Test pending: No
    /// matching step definition found for one or more steps." followed by the generated binding snippet, which
    /// says nothing about which steps are missing; the steps are in the failure's standard output, each
    /// followed by a "-> No matching step definition found for the step." line.
    /// </summary>
    /// <param name="msgLines">The trimmed, non-empty lines of the failure's message.</param>
    /// <param name="lines">The output's lines.</param>
    /// <param name="from">The index of the first line after the message.</param>
    /// <param name="to">The index of the first line after the failure.</param>
    /// <returns>The message to report, or <see langword="null"/> when this is no such failure or no step was read.</returns>
    private static string? ReqnrollPendingMessage(List<string> msgLines, string[] lines, int from, int to)
    {
        if (!msgLines.Exists(l => ReqnrollPendingMessagePattern().IsMatch(l)))
        {
            return null;
        }

        var steps = new List<string>();
        string? step = null;
        var inStandardOutput = false;
        for (var i = from; i < to; i++)
        {
            var line = lines[i].TrimEnd('\r');
            if (!inStandardOutput)
            {
                inStandardOutput = StandardOutputLabelPattern().IsMatch(line);
                continue;
            }

            var stepMatch = ReqnrollStepLinePattern().Match(line);
            if (stepMatch.Success)
            {
                step = stepMatch.Groups["step"].Value.TrimEnd();
            }
            else if (step is not null && ReqnrollUndefinedStepResultPattern().IsMatch(line))
            {
                steps.Add(step);
                step = null;
            }
        }

        return steps.Count == 0
            ? null
            : TextHelpers.Truncate(ReqnrollPendingPrefix + string.Join("; ", steps), MessageMaxLen);
    }

    private (string SourceRef, int NextIndex) FindSourceRef(string[] lines, int i)
    {
        var sourceRef = string.Empty;
        while (i < lines.Length)
        {
            var current = lines[i].TrimEnd('\r');
            if (EndsVsTestFailure(current))
            {
                break;
            }

            if (string.IsNullOrEmpty(sourceRef))
            {
                sourceRef = TryGetSourceRef(current);
            }

            i++;
        }

        return (sourceRef, i);
    }

    /// <summary>
    /// Whether <paramref name="line"/> starts something other than the failure being read: the next
    /// test's result line, or a summary. The minimal logger's failure is always followed by its
    /// "Failed!" line; the normal and detailed loggers follow it with more results, or with the
    /// "Test Run" block, which a failure parser reading on to the end of the output would swallow. The
    /// block's own first line ("Total tests:") ends it too, because a failed run's header is not above it.
    /// </summary>
    /// <param name="line">One line of output, without its carriage return.</param>
    private static bool EndsVsTestFailure(string line)
    {
        return FailedTestHeaderPattern().IsMatch(line)
               || PassedOrSkippedTestHeaderPattern().IsMatch(line)
               || SummaryPattern().IsMatch(line)
               || TestRunBlockHeaderPattern().IsMatch(line)
               || TestRunTotalTestsPattern().IsMatch(line);
    }

    /// <summary>
    /// Returns "<c>path:line N</c>" for a stack frame in user code, or an empty string. Frames whose
    /// file sits under "<c>/_/</c>" are skipped: that is the deterministic source root packages are
    /// built with (MSTest's own assertion frames), a path that exists on no machine.
    /// </summary>
    /// <param name="line">One line of a stack trace.</param>
    private string TryGetSourceRef(string line)
    {
        var frameMatch = StackFrameFilePattern().Match(line);
        if (!frameMatch.Success || frameMatch.Groups["file"].Value.StartsWith("/_/", StringComparison.Ordinal))
        {
            return string.Empty;
        }

        return $"{TextHelpers.ShortenPath(frameMatch.Groups["file"].Value, RootPath)}:line {frameMatch.Groups["line"].Value}";
    }

    private static int ParseMtpRunSummary(string[] lines, int i, Match headerMatch, ParseState state)
    {
        state.MtpZeroTestsVerdict = headerMatch.Groups["verdict"].Value
            .StartsWith("Zero tests ran", StringComparison.OrdinalIgnoreCase);
        i++;

        // The block's fields ("  total: 7", …, "  duration: 343ms") follow the header, after any
        // per-assembly result lines and blank lines; it ends at the first non-indented line.
        while (i < lines.Length)
        {
            var current = lines[i].TrimEnd('\r');
            if (current.Length > 0 && !char.IsWhiteSpace(current[0]))
            {
                break;
            }

            var countMatch = MtpSummaryCountPattern().Match(current);
            if (countMatch.Success)
            {
                var count = int.Parse(countMatch.Groups["count"].Value, CultureInfo.InvariantCulture);
                switch (countMatch.Groups["key"].Value.ToLowerInvariant())
                {
                    case "failed":
                        state.TotalFailed += count;
                        break;
                    case "succeeded":
                        state.TotalPassed += count;
                        break;
                    case "skipped":
                        state.TotalSkipped += count;
                        break;
                }
            }

            var durationMatch = MtpSummaryDurationPattern().Match(current);
            if (durationMatch.Success)
            {
                state.TotalDurationMs += ParseDurationToMs(durationMatch.Groups[DurationGroup].Value);
            }

            i++;
        }

        // One block covers the whole run, so the project count is the number of assemblies the run
        // announced ("Running tests from …"), at least one.
        state.ProjectCount += Math.Max(1, state.MtpAssembliesRun);
        return i;
    }

    private static void AccumulateSummary(Match summaryMatch, ParseState state)
    {
        state.TotalFailed += int.Parse(summaryMatch.Groups["failed"].Value, CultureInfo.InvariantCulture);
        state.TotalPassed += int.Parse(summaryMatch.Groups["passed"].Value, CultureInfo.InvariantCulture);
        state.TotalSkipped += int.Parse(summaryMatch.Groups["skipped"].Value, CultureInfo.InvariantCulture);
        var duration = summaryMatch.Groups[DurationGroup].Value;
        state.TotalDurationMs += ParseDurationToMs(duration);

        // VSTest prints whole units ("1 s", "1 m 40 s"), so only milliseconds or a decimal point tell the
        // figure is known more finely than that; a total is exact only if each summary it sums was.
        state.DurationExact &= IsExactDuration(duration);
        state.ProjectCount++;
    }

    /// <summary>
    /// Whether every part of a duration is known below the second: given in milliseconds or with a decimal
    /// point. VSTest's <c>1 s</c> and <c>1 m 40 s</c> are not; they may stand for anything up to a unit more.
    /// </summary>
    /// <param name="duration">The text of the duration, one or several parts.</param>
    private static bool IsExactDuration(string duration)
    {
        return DurationPartPattern().Matches(duration)
            .All(part => IsExactReading(part.Groups["value"].Value, part.Groups["unit"].Value));
    }

    private static bool IsExactReading(string number, string unit)
    {
        return unit.Equals("ms", StringComparison.OrdinalIgnoreCase) || number.Contains('.', StringComparison.Ordinal);
    }

    private static double ParseDurationToMs(string duration)
    {
        return DurationPartPattern().Matches(duration)
            .Sum(part => NormalizeDurationToMs(
                double.Parse(part.Groups["value"].Value, CultureInfo.InvariantCulture),
                part.Groups["unit"].Value));
    }

    private static void AccumulateTerminalLoggerSummary(Match summaryMatch, ParseState state)
    {
        var terminal = state.Terminal ??= new RunTotals();
        terminal.Failed += int.Parse(summaryMatch.Groups["failed"].Value, CultureInfo.InvariantCulture);
        terminal.Passed += int.Parse(summaryMatch.Groups["passed"].Value, CultureInfo.InvariantCulture);
        terminal.Skipped += int.Parse(summaryMatch.Groups["skipped"].Value, CultureInfo.InvariantCulture);
        var durationGroup = summaryMatch.Groups[DurationGroup];
        if (durationGroup.Success)
        {
            terminal.DurationMs += NormalizeDurationToMs(
                double.Parse(durationGroup.Value, CultureInfo.InvariantCulture),
                summaryMatch.Groups["unit"].Value);
            terminal.DurationExact &= IsExactReading(durationGroup.Value, summaryMatch.Groups["unit"].Value);
        }
    }

    /// <summary>
    /// Settles the run's totals once every line has been read. The console logger's counts are added
    /// only when a "Test Run" header was seen, so a stray "Passed: 3" in a test's own output never counts.
    /// The terminal logger's whole-run totals then stand for the run: its "Test summary:" counts every
    /// assembly, so it describes the same tests as the per-assembly summaries that can appear beside it
    /// (VSTest's "Passed!" line under an explicit console logger, its "Test Run" block under the normal
    /// or detailed one), and adding both counts each test twice. The project count stays the number of
    /// per-assembly summaries seen, and is at least one.
    /// </summary>
    /// <param name="state">The parsed run.</param>
    private static void ResolveTotals(ParseState state)
    {
        if (state.TestRunBlocks > 0)
        {
            state.TotalPassed += state.ConsoleLogger.Passed;
            state.TotalFailed += state.ConsoleLogger.Failed;
            state.TotalSkipped += state.ConsoleLogger.Skipped;
            state.TotalDurationMs += state.ConsoleLogger.DurationMs;
        }

        if (state.Terminal is not { } terminal)
        {
            return;
        }

        state.TotalPassed = terminal.Passed;
        state.TotalFailed = terminal.Failed;
        state.TotalSkipped = terminal.Skipped;
        if (terminal.DurationMs > 0)
        {
            state.TotalDurationMs = terminal.DurationMs;
            state.DurationExact = terminal.DurationExact;
        }

        state.ProjectCount = Math.Max(1, state.ProjectCount);
    }

    private static string FormatOutput(ParseState state, int exitCode, MsBuildDiagnosticReport buildReport)
    {
        var elapsed = DurationFormat.Format(state.TotalDurationMs, state.DurationExact);
        var warnings = DescribeBuildWarnings(buildReport);

        // A project that did not compile never ran its tests, and says why in the same diagnostics a
        // build prints; that comes first, with whatever the other projects' tests reported after it. Only
        // a failed run can have a build failure: on a zero exit a diagnostic-shaped line is never one.
        if (exitCode != 0 && buildReport.ErrorTotal > 0)
        {
            return FormatBuildFailure(state, buildReport, elapsed, warnings);
        }

        // A failed run (non-zero exit) with any parsed failure — from a summary or from failure
        // headers alone (crashed host) — always renders its failures, so it can never silently
        // collapse to empty. The exit code stays the sole verdict (the one exception, MTP's exit 8 with
        // its "Zero tests ran" verdict, is below): on a zero exit we never emit a FAILURES report just
        // because a stray line happened to match a failure-shaped pattern.
        if (exitCode != 0 && (state.TotalFailed > 0 || state.Failures.Count > 0))
        {
            return FormatFailures(state, elapsed, warnings: warnings);
        }

        // A listing that found names wins even beside a "No test matches" line: that one is another
        // project's, and says nothing about the names that were listed.
        if (exitCode == 0 && state.ListedTests.Count > 0)
        {
            return FormatTestList(state);
        }

        if (FormatZeroTests(state, exitCode) is { } zeroTests)
        {
            return zeroTests;
        }

        if (state.ProjectCount == 0)
        {
            // No summary and no failures parsed: nothing meaningful to condense. A non-zero exit
            // degrades to blank so FilteredRunUseCase's raw-tail fallback surfaces the real reason.
            return string.Empty;
        }

        if (exitCode != 0)
        {
            // Non-zero exit but the summary reported zero failures (e.g. crash after a pass summary):
            // blank so the raw-tail fallback fires instead of printing a clean-looking report.
            return string.Empty;
        }

        // A skipped-only run (VSTest's "Skipped!" summary) lands here too: "0 passed, N skipped".
        var skippedSuffix = state.TotalSkipped > 0
            ? $", {state.TotalSkipped} skipped"
            : string.Empty;
        return
            $"\u2713 dotnet test: {state.TotalPassed} passed{skippedSuffix} ({state.ProjectCount} project{(state.ProjectCount == 1 ? "" : "s")}, {elapsed}){warnings}\n";
    }

    /// <summary>
    /// What the status line of a run adds for the compiler warnings its build raised:
    /// <c> · 3 build warnings (CS0219×2, CS8602)</c>. The list names at most <see cref="MaxWarningCodes"/>
    /// codes, the most frequent first, with a count only where a code occurs more than once, and trails off
    /// with <c>, …</c> when more codes exist. A warning that carries no code counts toward the total but is
    /// not named, and the list is left out when no warning carries one. A run without warnings adds nothing.
    /// A <c>--list-tests</c> listing and a zero-tests verdict are no verdict on the build and do not carry it.
    /// </summary>
    /// <param name="report">The run's build diagnostics.</param>
    private static string DescribeBuildWarnings(MsBuildDiagnosticReport report)
    {
        var total = report.WarningTotal;
        if (total == 0)
        {
            return string.Empty;
        }

        var codes = report.WarningCodes
            .Where(c => c.Code != MsBuildDiagnosticReport.NoCode)
            .ToList();
        var counted = string.Create(CultureInfo.InvariantCulture,
            $" \u00b7 {total} build warning{(total == 1 ? "" : "s")}");
        if (codes.Count == 0)
        {
            return counted;
        }

        var named = codes.Take(MaxWarningCodes).Select(c =>
            c.Count == 1 ? c.Code : string.Create(CultureInfo.InvariantCulture, $"{c.Code}\u00d7{c.Count}"));
        var more = codes.Count > MaxWarningCodes ? ", \u2026" : string.Empty;
        return $"{counted} ({string.Join(", ", named)}{more})";
    }

    /// <summary>The names a <c>--list-tests</c> run listed, one per line in listed order, then its status line.</summary>
    /// <param name="state">The parsed run.</param>
    private static string FormatTestList(ParseState state)
    {
        var sb = new StringBuilder();
        foreach (var name in state.ListedTests)
        {
            sb.Append(name).Append('\n');
        }

        return sb.Append(CultureInfo.InvariantCulture,
                $"\u2713 dotnet test --list-tests: {state.ListedTests.Count} test{(state.ListedTests.Count == 1 ? "" : "s")} ({state.ListedProjects} project{(state.ListedProjects == 1 ? "" : "s")})\n")
            .ToString();
    }

    /// <summary>The "0 tests found" verdict, or <see langword="null"/> when the run is not one.</summary>
    /// <param name="state">The parsed run.</param>
    /// <param name="exitCode">The run's exit code.</param>
    private static string? FormatZeroTests(ParseState state, int exitCode)
    {
        // A "nothing ran" verdict requires that no assembly produced evidence of a test.
        // ZeroTestsFound is per-assembly: in a multi-project run, one assembly matching nothing must
        // never override another's real results. Stated in full — rather than relying on the earlier
        // failure branch — so the guard is self-contained and survives a future reordering of the branches.
        // TotalSkipped: 0 is what keeps a skipped-only run (tests found, none executed) from being reported
        // as "0 tests found": its skips are evidence, and FormatOutput renders it as "0 passed, N skipped".
        var noTestEvidence = state is { TotalPassed: 0, TotalFailed: 0, TotalSkipped: 0 }
                             && state.Failures.Count == 0;
        // MTP exits 8 when zero tests ran, so exit 8 together with its run-level "Zero tests ran"
        // verdict stands in for the zero exit: that exit code means exactly this outcome, not a failure
        // to hide. Any other non-zero exit is a real failure and falls through to the raw-tail fallback.
        var zeroTestsExit = exitCode == 0 || (exitCode == MtpZeroTestsExitCode && state.MtpZeroTestsVerdict);
        if (!zeroTestsExit || !noTestEvidence || (!state.ZeroTestsFound && state.ProjectCount == 0))
        {
            return null;
        }

        return state.ZeroTestsFound
            ? $"⚠ dotnet test: {DescribeNoTests(state)}\n"
            : "⚠ dotnet test: 0 tests found\n";
    }

    /// <summary>
    /// What the zero-tests warning says for a run whose output named a "no test" line: the filter that
    /// matched nothing, or that no test was found, plus the assemblies, whichever of them the output gave.
    /// </summary>
    /// <param name="state">The parsed run, with <see cref="ParseState.ZeroTestsFound"/> set.</param>
    private static string DescribeNoTests(ParseState state)
    {
        var assemblies = state.ZeroTestsAssemblies.Count > 0
            ? $" ({string.Join(", ", state.ZeroTestsAssemblies)})"
            : string.Empty;
        if (state.ZeroTestsFilter is { } filter)
        {
            return $"no test matched filter {filter}{assemblies}";
        }

        return assemblies.Length > 0
            ? $"no test found{assemblies}"
            : "0 tests found (no assembly matched)";
    }

    /// <summary>
    /// The report of a build that failed: the diagnostics a <c>dotnet build</c> would print, then the
    /// failed tests of the projects that did build, if any, and the status line, last.
    /// </summary>
    /// <param name="state">The parsed run.</param>
    /// <param name="report">The build's diagnostics, with at least one error.</param>
    /// <param name="elapsed">The test run's duration.</param>
    /// <param name="warnings">What the status line says of the build's warnings, from <see cref="DescribeBuildWarnings"/>.</param>
    private static string FormatBuildFailure(ParseState state, MsBuildDiagnosticReport report, string elapsed,
        string warnings)
    {
        var diagnostics = report.RenderDiagnostics();
        var buildFailure = DescribeBuildFailure(report);
        if (state.ProjectCount > 0 || state.TotalFailed > 0 || state.Failures.Count > 0)
        {
            return diagnostics + FormatFailures(state, elapsed, buildFailure, warnings);
        }

        return $"{diagnostics}\u2717 dotnet test: {buildFailure}{warnings}\n";
    }

    /// <summary>What did not build, as the status line says it: <c>1 project did not build (34 errors)</c>.</summary>
    /// <param name="report">The build's diagnostics, with at least one error.</param>
    private static string DescribeBuildFailure(MsBuildDiagnosticReport report)
    {
        var errors = $"{report.ErrorTotal} error{(report.ErrorTotal == 1 ? "" : "s")}";

        // An error that names no project (a tool-level one) leaves nothing to count, and "0 projects did
        // not build" would read as a contradiction.
        if (report.FailedProjectCount == 0)
        {
            return $"build failed ({errors})";
        }

        return $"{report.FailedProjectCount} project{(report.FailedProjectCount == 1 ? "" : "s")} did not build ({errors})";
    }

    /// <summary>
    /// The failed tests, then the status line. When <paramref name="buildFailure"/> is given, the run
    /// also had a project that did not build: it joins the counts on the status line, and a run in which
    /// no test failed has no failures block.
    /// </summary>
    /// <param name="state">The parsed run.</param>
    /// <param name="elapsed">The test run's duration.</param>
    /// <param name="buildFailure">What did not build, or <see langword="null"/> when everything did.</param>
    /// <param name="warnings">What the status line says of the build's warnings, from <see cref="DescribeBuildWarnings"/>.</param>
    private static string FormatFailures(ParseState state, string elapsed, string? buildFailure = null,
        string warnings = "")
    {
        // When a summary is present, its failed count is authoritative; when the host crashed before
        // printing one, fall back to the number of failure headers we actually parsed.
        var failedCount = Math.Max(state.TotalFailed, state.Failures.Count);

        var sb = new StringBuilder();
        if (failedCount > 0)
        {
            AppendFailureBlock(sb, state, failedCount);
        }

        // Only show the "(N projects, elapsed)" context when a summary was actually parsed. Without
        // one (crashed host), ProjectCount and elapsed are both 0, and "(0 projects, 0.00s)" reads
        // as a real — but bogus — measurement.
        var context = string.Empty;
        if (state.ProjectCount > 0)
        {
            var projectWord = state.ProjectCount == 1 ? "project" : "projects";
            context = $" ({state.ProjectCount} {projectWord}, {elapsed})";
        }

        var buildSuffix = buildFailure is null ? string.Empty : $" \u00b7 {buildFailure}";
        sb.AppendLine(CultureInfo.InvariantCulture,
            $"dotnet test: {failedCount} failed, {state.TotalPassed} passed{(state.TotalSkipped > 0 ? $", {state.TotalSkipped} skipped" : string.Empty)}{buildSuffix}{context}{warnings}");

        // Normalize to '\n' so failure output matches the success paths (which use explicit '\n')
        // and stays identical across platforms; StringBuilder.AppendLine emits '\r\n' on Windows.
        return sb.ToString().ReplaceLineEndings("\n");
    }

    private static void AppendFailureBlock(StringBuilder sb, ParseState state, int failedCount)
    {
        sb.AppendLine(CultureInfo.InvariantCulture, $"FAILURES ({failedCount}):");

        foreach (var f in state.Failures.Take(MaxFailures))
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"  {f.TestName} [{f.Duration}]")
                .AppendLine(CultureInfo.InvariantCulture, $"    {f.Message}");
            if (!string.IsNullOrEmpty(f.SourceRef))
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"    at {f.SourceRef}");
            }
        }

        if (state.Failures.Count > MaxFailures)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"+{state.Failures.Count - MaxFailures} more failures");
        }
    }

    /// <summary>
    /// Joins a failure's message lines into the one line the report prints. An xUnit assertion with an
    /// <c>Expected:</c>/<c>Actual:</c> pair keeps its <c>Assert.…</c> header and its <c>↓ (pos N)</c> pointer; a
    /// nested exception keeps its innermost <c>---- Type : message</c> segment, the usual real cause, whole.
    /// </summary>
    /// <param name="lines">The trimmed, non-empty lines of the message.</param>
    private static string CompactMessage(List<string> lines)
    {
        if (lines.Count == 0)
        {
            return string.Empty;
        }

        var expectedLine = lines.Find(l => l.StartsWith("Expected:", StringComparison.OrdinalIgnoreCase));
        var actualLine = lines.Find(l => l.StartsWith("Actual:", StringComparison.OrdinalIgnoreCase));
        if (expectedLine != null && actualLine != null)
        {
            return CompactAssertion(lines, expectedLine, actualLine);
        }

        return CompactNested(lines);
    }

    /// <summary>
    /// Renders <c>{header} at pos N — Expected: …, Actual: …</c>. The header is the first line when it starts
    /// with <c>Assert.</c>; the position comes from xUnit's <c>↓ (pos N)</c> line and is only kept with a header.
    /// The padding xUnit puts after <c>Actual:</c> to line the values up is collapsed to one space.
    /// </summary>
    /// <param name="lines">The message lines.</param>
    /// <param name="expectedLine">The line starting with <c>Expected:</c>.</param>
    /// <param name="actualLine">The line starting with <c>Actual:</c>.</param>
    private static string CompactAssertion(List<string> lines, string expectedLine, string actualLine)
    {
        var pair = $"{CollapsePadding(expectedLine)}, {CollapsePadding(actualLine)}";
        if (!lines[0].StartsWith("Assert.", StringComparison.Ordinal))
        {
            return TextHelpers.Truncate(pair, MessageMaxLen);
        }

        var pointer = lines.Select(l => AssertionPositionPattern().Match(l)).FirstOrDefault(m => m.Success);
        var position = pointer is null ? string.Empty : $" at pos {pointer.Groups["pos"].Value}";
        return TextHelpers.Truncate($"{lines[0]}{position} — {pair}", MessageMaxLen);
    }

    private static string CollapsePadding(string labelledLine) =>
        LabelPaddingPattern().Replace(labelledLine, "${label}: ");

    /// <summary>
    /// Joins the lines with spaces. A message longer than the budget that nests exceptions (xUnit's
    /// <c>---- Type : message</c> lines) keeps its last such segment whole, capped at
    /// <see cref="InnerExceptionMaxLen"/>, and truncates what comes before it to the rest of the budget.
    /// </summary>
    /// <param name="lines">The message lines.</param>
    private static string CompactNested(List<string> lines)
    {
        var joined = string.Join(" ", lines);
        if (joined.Length <= MessageMaxLen)
        {
            return joined;
        }

        var innerIndex = lines.FindLastIndex(l => InnerExceptionLinePattern().IsMatch(l));
        if (innerIndex <= 0)
        {
            return TextHelpers.Truncate(joined, MessageMaxLen);
        }

        var inner = TextHelpers.Truncate(string.Join(" ", lines.Skip(innerIndex)), InnerExceptionMaxLen);
        var outer = TextHelpers.Truncate(string.Join(" ", lines.Take(innerIndex)), MessageMaxLen - inner.Length - 1);
        return $"{outer} {inner}";
    }

    private static double NormalizeDurationToMs(double value, string unit)
    {
        return unit.ToLowerInvariant() switch
        {
            "ms" => value,
            "s" => value * 1_000,
            "m" => value * 60_000,
            "h" => value * 3_600_000,
            _ => value
        };
    }

    /// <summary>Converts the console logger's <c>Total time: 0.4813 Seconds</c> to milliseconds.</summary>
    /// <param name="value">The number.</param>
    /// <param name="unit">The logger's unit word: Seconds, Minutes, Hours or Days.</param>
    private static double TotalTimeToMs(double value, string unit)
    {
        return unit switch
        {
            "Seconds" => value * 1_000,
            "Minutes" => value * 60_000,
            "Hours" => value * 3_600_000,
            _ => value * 86_400_000
        };
    }

    // Summary: "Passed! - Failed: 0, Passed: 17, Skipped: 0, Total: 17, Duration: 89 ms - File.dll"
    // VSTest opens it with "Skipped!" when every test of the project was skipped.
    // Duration may be a single unit ("89 ms") or multi-part ("1 m 2 s", "1 h 3 m 4 s").
    [GeneratedRegex(
        @"(?:Passed|Failed|Skipped)!\s+-\s+Failed:\s+(?<failed>\d+),\s+Passed:\s+(?<passed>\d+),\s+Skipped:\s+(?<skipped>\d+),\s+Total:\s+\d+,\s+Duration:\s+(?<duration>[\d.]+ (?:ms|s|m|h)(?: [\d.]+ (?:ms|s|m|h))*)",
        RegexOptions.IgnoreCase)]
    private static partial Regex SummaryPattern();

    // The normal and detailed console loggers print no "Passed!" line. Their run summary is
    //   Test Run Successful.        (or "Test Run Failed.")
    //   Total tests: 5
    //        Passed: 4
    //        Failed: 1              (only when there are failures)
    //       Skipped: 1              (only when there are skips)
    //    Total time: 0.4813 Seconds
    // "Total tests" is not read: the three counts below it already sum to it.
    [GeneratedRegex(@"^Test Run (?:Successful|Failed)\.\s*$")]
    private static partial Regex TestRunBlockHeaderPattern();

    [GeneratedRegex("^Total tests:")]
    private static partial Regex TestRunTotalTestsPattern();

    [GeneratedRegex(@"^\s+(?<key>Passed|Failed|Skipped):\s+(?<count>\d+)\s*$")]
    private static partial Regex TestRunCountPattern();

    // The logger picks one unit by magnitude; Seconds is the only one a normal run reaches.
    [GeneratedRegex(@"^\s*Total time:\s+(?<value>\d+(?:\.\d+)?)\s+(?<unit>Days|Hours|Minutes|Seconds)\s*$")]
    private static partial Regex TestRunTotalTimePattern();

    // "  Passed Namespace.Class.Method [4 ms]" or "  Skipped Namespace.Class.Method [1 ms]": a finished
    // test's result line under the normal and detailed console loggers (the same duration shapes as a failure's).
    [GeneratedRegex(@"^\s+(?:Passed|Skipped)\s+.+?\s+\[(?:< )?[\d.]+ (?:ms|s|m(?: \d+ s)?)\]\s*$")]
    private static partial Regex PassedOrSkippedTestHeaderPattern();

    // `--list-tests`: "The following Tests are available:" once per project, then each test's name indented
    // by four spaces. Parallel projects print all their headers before any name.
    [GeneratedRegex(@"^The following Tests are available:\s*$")]
    private static partial Regex TestListHeaderPattern();

    [GeneratedRegex(@"^ {4}(?<name>\S.*?)\s*$")]
    private static partial Regex ListedTestNamePattern();

    // A single "<number> <unit>" pair within a possibly multi-part duration string: VSTest spaces
    // the unit ("1 m 2 s"), MTP does not ("1m 02s 500ms").
    [GeneratedRegex(@"(?<value>[\d.]+)\s*(?<unit>ms|s|m|h)", RegexOptions.IgnoreCase)]
    private static partial Regex DurationPartPattern();

    // "  Failed FullyQualifiedTestName [12 ms]", "[< 1 ms]", "[1 s]", or "[1 m 30 s]".
    // The full duration (number + unit) is captured so slow, second/minute-scale tests survive.
    [GeneratedRegex(@"^\s+Failed\s+(?<name>.+?)\s+\[(?<duration>(?:< )?[\d.]+ (?:ms|s|m(?: \d+ s)?))\]\s*$")]
    private static partial Regex FailedTestHeaderPattern();

    // Microsoft.Testing.Platform failure line: "failed TestDisplayName (12ms)". The display name
    // may hold spaces and parentheses (a data row: "failed Add (1, 2) (3ms)"); the duration is the
    // trailing parenthesized group that starts with a digit.
    [GeneratedRegex(@"^failed\s+(?<name>.+?)(?:\s+\((?<duration>\d[\dhms. ]*)\))?\s*$")]
    private static partial Regex MtpFailedTestPattern();

    // MTP: the "from <assembly> (<tfm>|<arch>)" line between a failure's message and its stack trace.
    [GeneratedRegex(@"^\s+from\s.+\.(?:dll|exe)(?:\s\([^)]*\))?\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex MtpFromAssemblyPattern();

    // MTP, xUnit v3: what ends a failure message that follows the "from" line besides a stack frame: the
    // "--- End of stack trace from previous location ---" marker and the "Standard output"/"Error output"
    // section headers of the test's captured output.
    [GeneratedRegex(@"^\s+(?:--- End of .+ ---|(?:Standard|Error) output)\s*$")]
    private static partial Regex MtpMessageEndPattern();

    // MTP: "Running tests from /path/Tests.dll (net10.0|x64)", one per test assembly run.
    [GeneratedRegex(@"^Running tests from\s")]
    private static partial Regex MtpRunningTestsPattern();

    // MTP: an assembly that ran nothing, "/path/Tests.dll (net10.0|x64) Zero tests ran (160ms)". The
    // sources group holds the file name, which may hold spaces; starting only at the line start or after
    // a separator keeps the scan linear on a long line.
    [GeneratedRegex(@"(?:^|[/\\])(?<sources>[^/\\]+\.(?:dll|exe))\s\([^)]*\)\s+Zero tests ran\b", RegexOptions.IgnoreCase)]
    private static partial Regex MtpAssemblyZeroTestsPattern();

    // MTP run summary header: "Test run summary: Passed!", "Failed!" or "Zero tests ran". Its fields
    // follow on indented lines.
    [GeneratedRegex(@"^Test run summary:\s*(?<verdict>.*)$", RegexOptions.IgnoreCase)]
    private static partial Regex MtpRunSummaryHeaderPattern();

    // MTP run summary count field: "  failed: 1", "  succeeded: 5", "  skipped: 1".
    [GeneratedRegex(@"^\s+(?<key>failed|succeeded|skipped):\s*(?<count>\d+)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex MtpSummaryCountPattern();

    // MTP run summary duration field: "  duration: 343ms", "  duration: 1m 02s 500ms".
    [GeneratedRegex(@"^\s+duration:\s*(?<duration>\d.*?)\s*$", RegexOptions.IgnoreCase)]
    private static partial Regex MtpSummaryDurationPattern();

    // VSTest-mode `dotnet test` under the MSBuild terminal logger (--tl:on, or an interactive
    // terminal) prints a one-line summary; confirmed with the .NET 10.0.401 SDK:
    // "Test summary: total: 10, failed: 1, succeeded: 9, skipped: 0, duration: 2.3s"
    [GeneratedRegex(
        @"^Test summary: total: (?<total>\d+), failed: (?<failed>\d+), succeeded: (?<passed>\d+), skipped: (?<skipped>\d+)(?:, duration: (?<duration>[\d.]+)\s*(?<unit>ms|s|m|h))?",
        RegexOptions.IgnoreCase)]
    private static partial Regex TerminalLoggerSummaryPattern();

    // The position and level of a compiler diagnostic, "(7,32): error": the shape of "file(7,32): error CS0029: ...".
    [GeneratedRegex(@"\(\d+,\d+\):\s+(?:error|warning)\s")]
    private static partial Regex FileAnchoredDiagnosticPattern();

    // MSBuild closes every diagnostic with the project that raised it: "... [/path/A.csproj]", or
    // "... [/path/A.csproj::TargetFramework=net8.0]" when a multi-targeted build re-reports it. Also
    // .fsproj, .vbproj and .proj.
    [GeneratedRegex(@"\.[A-Za-z]*proj(?:::[^\]]*)?\]\s*$")]
    private static partial Regex ProjectSuffixPattern();

    // Stack frame with CS file: "   at Class.Method() in /path/to/File.cs:line 42" (VSTest) or
    // "    at Class.Method() in /path/to/File.cs:42" (MTP).
    [GeneratedRegex(@"in (?<file>.+\.cs):(?:line )?(?<line>\d+)")]
    private static partial Regex StackFrameFilePattern();

    // Any stack frame line, with or without a source file: "    at Class.Method()".
    [GeneratedRegex(@"^\s+at\s+\S")]
    private static partial Regex StackFrameLinePattern();

    // "  Error Message:"
    [GeneratedRegex(@"^\s+Error Message:\s*$")]
    private static partial Regex ErrorMessageLabelPattern();

    // "  Stack Trace:"
    [GeneratedRegex(@"^\s+Stack Trace:\s*$")]
    private static partial Regex StackTraceLabelPattern();

    // "  Standard Output Messages:", the section of a failure that holds the test's own output.
    [GeneratedRegex(@"^\s+Standard Output Messages:\s*$")]
    private static partial Regex StandardOutputLabelPattern();

    // Reqnroll's failure message for a scenario with steps that no binding matches, behind the exception type:
    // "Reqnroll.xUnit.ReqnrollPlugin.XUnitPendingStepException : Test pending: No matching step definition found
    // for one or more steps."
    [GeneratedRegex(@"\bNo matching step definition found for one or more steps\b")]
    private static partial Regex ReqnrollPendingMessagePattern();

    // A step in Reqnroll's trace of the scenario, as written in the feature, with the keyword: " When the numbers
    // are added". One space of indent; its table or doc string argument has three, and its result line " -> …"
    // starts with the arrow.
    [GeneratedRegex(@"^ (?<step>(?!->)\S.*)$")]
    private static partial Regex ReqnrollStepLinePattern();

    // The result line of a step no binding matches: " -> No matching step definition found for the step. Use the
    // following code to create one:".
    [GeneratedRegex(@"^\s*->\s+No matching step definition found for the step\b")]
    private static partial Regex ReqnrollUndefinedStepResultPattern();

    // xUnit's pointer above the Expected/Actual pair of a collection or string diff: "↓ (pos 9)". The arrow
    // below the pair ("↑ (pos 9)") repeats it and is not read.
    [GeneratedRegex(@"^\u2193\s*\(pos (?<pos>\d+)\)$")]
    private static partial Regex AssertionPositionPattern();

    // The padding xUnit puts after "Actual:" so that its value lines up with Expected's.
    [GeneratedRegex(@"^(?<label>Expected|Actual):[ \t]+", RegexOptions.IgnoreCase)]
    private static partial Regex LabelPaddingPattern();

    // xUnit's nested exception: "---- Inner : message", with four more dashes per level of nesting.
    [GeneratedRegex(@"^-{4,} \S")]
    private static partial Regex InnerExceptionLinePattern();

    // Zero tests: VSTest's "No test matches the given testcase filter `<expression>` in <assemblies>" or
    // "No test is available in <assemblies>. Make sure that test discoverer & executors are registered…".
    // The filter and sources groups are optional: the bare phrases still count as a zero-tests line.
    [GeneratedRegex(
        @"No test matches the given testcase filter(?:\s+`(?<filter>.+)`\s+in\s+(?<sources>.+))?|No test is available(?:\s+in\s+(?<sources>.+?)(?:\.\s+Make sure|\.?$))?",
        RegexOptions.IgnoreCase)]
    private static partial Regex NoTestsPattern();

    // An assembly's file name in the paths a VSTest line names, "/dir/A.Tests.dll" or "C:\dir\A.Tests.dll":
    // what follows the last path separator, up to the extension (a comma ends one path in a list).
    [GeneratedRegex(@"[^/\\,]+\.(?:dll|exe)", RegexOptions.IgnoreCase)]
    private static partial Regex SourceFileNamePattern();

    private sealed class ParseState
    {
        public List<FailureInfo> Failures { get; } = [];
        public int TotalPassed { get; set; }
        public int TotalFailed { get; set; }
        public int TotalSkipped { get; set; }
        public int ProjectCount { get; set; }
        public double TotalDurationMs { get; set; }

        /// <summary>
        /// Whether <see cref="TotalDurationMs"/> is known below the second. The sources that print whole units
        /// (VSTest's minimal summary) clear it; MTP's <c>duration: 1m 02s 500ms</c>, which always carries its
        /// milliseconds below a minute, and the console logger's <c>Total time: 0.4814 Seconds</c>, rounded to four
        /// decimals rather than truncated, never do.
        /// </summary>
        public bool DurationExact { get; set; } = true;

        public bool ZeroTestsFound { get; set; }

        /// <summary>The first filter expression a "No test matches" line quoted, or <see langword="null"/>.</summary>
        public string? ZeroTestsFilter { get; set; }

        /// <summary>The file names of the assemblies that matched or ran no test, distinct, in order of appearance.</summary>
        public List<string> ZeroTestsAssemblies { get; } = [];

        /// <summary>Test assemblies an MTP run announced with "Running tests from".</summary>
        public int MtpAssembliesRun { get; set; }

        /// <summary>The MTP run summary's verdict was "Zero tests ran".</summary>
        public bool MtpZeroTestsVerdict { get; set; }

        /// <summary>Console-logger "Test Run Successful."/"Test Run Failed." blocks seen.</summary>
        public int TestRunBlocks { get; set; }

        /// <summary>"The following Tests are available:" headers seen, one per project of a <c>--list-tests</c> run.</summary>
        public int ListedProjects { get; set; }

        /// <summary>The test names a <c>--list-tests</c> run listed, in listed order.</summary>
        public List<string> ListedTests { get; } = [];

        /// <summary>The counts the console logger's "Test Run" block lines gave; used only if a header was seen.</summary>
        public RunTotals ConsoleLogger { get; } = new();

        /// <summary>The terminal logger's whole-run totals, or <see langword="null"/> when it printed none.</summary>
        public RunTotals? Terminal { get; set; }
    }

    private sealed class RunTotals
    {
        public int Passed { get; set; }
        public int Failed { get; set; }
        public int Skipped { get; set; }
        public double DurationMs { get; set; }

        /// <summary>Whether <see cref="DurationMs"/> is known below the second; see <see cref="ParseState.DurationExact"/>.</summary>
        public bool DurationExact { get; set; } = true;
    }

    private sealed record FailureInfo(string TestName, string Duration, string Message, string SourceRef);
}
