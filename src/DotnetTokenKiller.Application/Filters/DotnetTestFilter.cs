using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Domain.Filters;

namespace DotnetTokenKiller.Application.Filters;

/// <summary>Condenses dotnet test output to a concise pass/fail summary.</summary>
/// <param name="rootPath">Optional root path used to shorten file paths in stack traces.</param>
public sealed partial class DotnetTestFilter(string? rootPath = null) : IOutputFilter
{
    private const int MaxFailures = 15;
    private const int MessageMaxLen = 200;

    /// <summary>Name of the regex capture group holding a test/summary duration.</summary>
    private const string DurationGroup = "duration";

    private string RootPath => rootPath ?? Environment.CurrentDirectory;

    /// <summary>Applies the filter to the test output.</summary>
    /// <param name="strippedOutput">The test output to filter, with ANSI escape sequences already stripped.</param>
    /// <param name="exitCode">The process exit code; the sole source of truth for the success/failure verdict.</param>
    public string Apply(string strippedOutput, int exitCode)
    {
        if (string.IsNullOrEmpty(strippedOutput))
        {
            return string.Empty;
        }

        var lines = strippedOutput.Split('\n');
        var state = ParseLines(lines);
        return FormatOutput(state, exitCode);
    }

    private ParseState ParseLines(string[] lines)
    {
        var state = new ParseState();
        var i = 0;
        while (i < lines.Length)
        {
            var line = lines[i].TrimEnd('\r');

            var summaryMatch = SummaryPattern().Match(line);
            if (summaryMatch.Success)
            {
                AccumulateSummary(summaryMatch, state);
                i++;
                continue;
            }

            var terminalLoggerSummaryMatch = TerminalLoggerSummaryPattern().Match(line);
            if (terminalLoggerSummaryMatch.Success)
            {
                AccumulateTerminalLoggerSummary(terminalLoggerSummaryMatch, state);
                i++;
                continue;
            }

            var mtpRunSummaryMatch = MtpRunSummaryHeaderPattern().Match(line);
            if (mtpRunSummaryMatch.Success)
            {
                i = ParseMtpRunSummary(lines, i, mtpRunSummaryMatch, state);
                continue;
            }

            if (MtpRunningTestsPattern().IsMatch(line))
            {
                state.MtpAssembliesRun++;
                i++;
                continue;
            }

            if (NoTestsPattern().IsMatch(line) || MtpAssemblyZeroTestsPattern().IsMatch(line))
            {
                state.ZeroTestsFound = true;
                i++;
                continue;
            }

            var failedHeaderMatch = FailedTestHeaderPattern().Match(line);
            if (failedHeaderMatch.Success)
            {
                i = ParseFailure(lines, i, failedHeaderMatch, state);
                continue;
            }

            var mtpFailedMatch = MtpFailedTestPattern().Match(line);
            if (mtpFailedMatch.Success)
            {
                i = ParseMtpFailure(lines, i, mtpFailedMatch, state);
                continue;
            }

            i++;
        }

        return state;
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

        var (sourceRef, afterStack) = FindSourceRef(lines, i);
        i = afterStack;

        state.Failures.Add(new FailureInfo(testName, duration, CompactMessage(msgLines), sourceRef));
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
        // MTP prints the message, then "from <assembly> (<tfm>|<arch>)", then the message again
        // above the stack trace: everything after the "from" line is read for frames only.
        var msgLines = new List<string>();
        var sourceRef = string.Empty;
        var messageEnded = false;
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
            if (StackTraceLabelPattern().IsMatch(current)
                || FailedTestHeaderPattern().IsMatch(current)
                || SummaryPattern().IsMatch(current))
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

    private (string SourceRef, int NextIndex) FindSourceRef(string[] lines, int i)
    {
        var sourceRef = string.Empty;
        while (i < lines.Length)
        {
            var current = lines[i].TrimEnd('\r');
            if (FailedTestHeaderPattern().IsMatch(current) || SummaryPattern().IsMatch(current))
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
        state.TotalDurationMs += ParseDurationToMs(summaryMatch.Groups[DurationGroup].Value);
        state.ProjectCount++;
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
        state.TotalFailed += int.Parse(summaryMatch.Groups["failed"].Value, CultureInfo.InvariantCulture);
        state.TotalPassed += int.Parse(summaryMatch.Groups["passed"].Value, CultureInfo.InvariantCulture);
        state.TotalSkipped += int.Parse(summaryMatch.Groups["skipped"].Value, CultureInfo.InvariantCulture);
        var durationGroup = summaryMatch.Groups[DurationGroup];
        if (durationGroup.Success)
        {
            state.TotalDurationMs += NormalizeDurationToMs(
                double.Parse(durationGroup.Value, CultureInfo.InvariantCulture),
                summaryMatch.Groups["unit"].Value);
        }

        state.ProjectCount++;
    }

    private static string FormatOutput(ParseState state, int exitCode)
    {
        var elapsed = $"{state.TotalDurationMs / 1000.0:F2}s";

        // A failed run (non-zero exit) with any parsed failure — from a summary or from failure
        // headers alone (crashed host) — always renders its failures, so it can never silently
        // collapse to empty. The exit code stays the sole verdict: on a zero exit we never emit a
        // FAILURES report just because a stray line happened to match a failure-shaped pattern.
        if (exitCode != 0 && (state.TotalFailed > 0 || state.Failures.Count > 0))
        {
            return FormatFailures(state, elapsed);
        }

        // Skipped-only run: tests were discovered but none executed. This is a success, but it is
        // not "0 tests found" — surface the skipped count so the distinction isn't lost.
        if (exitCode == 0 && state is { TotalPassed: 0, TotalSkipped: > 0 })
        {
            return $"✓ dotnet test: {state.TotalSkipped} skipped, 0 executed\n";
        }

        // A "nothing ran" verdict requires that no assembly produced evidence of a test.
        // ZeroTestsFound is per-assembly: in a multi-project run, one assembly matching nothing must
        // never override another's real results. Stated in full — rather than relying on the earlier
        // skipped-only and failure branches — so the guard is self-contained and survives a future
        // reordering of those branches. TotalSkipped: 0 is currently shadowed by the skipped-only
        // branch above (it always returns first when TotalSkipped > 0), so that conjunct is defensive,
        // not load-bearing.
        var noTestEvidence = state is { TotalPassed: 0, TotalFailed: 0, TotalSkipped: 0 }
                             && state.Failures.Count == 0;
        // MTP exits non-zero (8) when zero tests ran, so its run-level "Zero tests ran" verdict stands
        // in for the zero exit: that exit code means exactly this outcome, not a failure to hide.
        if ((exitCode == 0 || state.MtpZeroTestsVerdict) && noTestEvidence
                                                         && (state.ZeroTestsFound || state.ProjectCount > 0))
        {
            return state.ZeroTestsFound
                ? "⚠ dotnet test: 0 tests found (no assembly matched)\n"
                : "⚠ dotnet test: 0 tests found\n";
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

        var skippedSuffix = state.TotalSkipped > 0
            ? $", {state.TotalSkipped} skipped"
            : string.Empty;
        return
            $"\u2713 dotnet test: {state.TotalPassed} passed{skippedSuffix} ({state.ProjectCount} project{(state.ProjectCount == 1 ? "" : "s")}, {elapsed})\n";
    }

    private static string FormatFailures(ParseState state, string elapsed)
    {
        // When a summary is present, its failed count is authoritative; when the host crashed before
        // printing one, fall back to the number of failure headers we actually parsed.
        var failedCount = Math.Max(state.TotalFailed, state.Failures.Count);

        var sb = new StringBuilder();
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

        // Only show the "(N projects, elapsed)" context when a summary was actually parsed. Without
        // one (crashed host), ProjectCount and elapsed are both 0, and "(0 projects, 0.00s)" reads
        // as a real — but bogus — measurement.
        var context = string.Empty;
        if (state.ProjectCount > 0)
        {
            var projectWord = state.ProjectCount == 1 ? "project" : "projects";
            context = $" ({state.ProjectCount} {projectWord}, {elapsed})";
        }

        sb.AppendLine(CultureInfo.InvariantCulture,
            $"dotnet test: {failedCount} failed, {state.TotalPassed} passed{(state.TotalSkipped > 0 ? $", {state.TotalSkipped} skipped" : string.Empty)}{context}");

        // Normalize to '\n' so failure output matches the success paths (which use explicit '\n')
        // and stays identical across platforms; StringBuilder.AppendLine emits '\r\n' on Windows.
        return sb.ToString().ReplaceLineEndings("\n");
    }

    private static string CompactMessage(List<string> lines)
    {
        if (lines.Count == 0)
        {
            return string.Empty;
        }

        // xUnit Assert.Equal multi-line: "Expected: ..." and "Actual: ..." on separate lines
        var expectedLine = lines.Find(l => l.StartsWith("Expected:", StringComparison.OrdinalIgnoreCase));
        var actualLine = lines.Find(l => l.StartsWith("Actual:", StringComparison.OrdinalIgnoreCase));
        if (expectedLine != null && actualLine != null)
        {
            return TextHelpers.Truncate($"{expectedLine}, {actualLine}", MessageMaxLen);
        }

        return TextHelpers.Truncate(string.Join(" ", lines), MessageMaxLen);
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

    // Summary: "Passed! - Failed: 0, Passed: 17, Skipped: 0, Total: 17, Duration: 89 ms - File.dll"
    // Duration may be a single unit ("89 ms") or multi-part ("1 m 2 s", "1 h 3 m 4 s").
    [GeneratedRegex(
        @"(?:Passed|Failed)!\s+-\s+Failed:\s+(?<failed>\d+),\s+Passed:\s+(?<passed>\d+),\s+Skipped:\s+(?<skipped>\d+),\s+Total:\s+\d+,\s+Duration:\s+(?<duration>[\d.]+ (?:ms|s|m|h)(?: [\d.]+ (?:ms|s|m|h))*)",
        RegexOptions.IgnoreCase)]
    private static partial Regex SummaryPattern();

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

    // MTP: "Running tests from /path/Tests.dll (net10.0|x64)", one per test assembly run.
    [GeneratedRegex(@"^Running tests from\s")]
    private static partial Regex MtpRunningTestsPattern();

    // MTP: an assembly that ran nothing, "/path/Tests.dll (net10.0|x64) Zero tests ran (160ms)".
    [GeneratedRegex(@"\.(?:dll|exe)\s\([^)]*\)\s+Zero tests ran\b", RegexOptions.IgnoreCase)]
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

    // Zero tests: "No test matches the given testcase filter" or "No test is available"
    [GeneratedRegex("No test matches the given testcase filter|No test is available", RegexOptions.IgnoreCase)]
    private static partial Regex NoTestsPattern();

    private sealed class ParseState
    {
        public List<FailureInfo> Failures { get; } = [];
        public int TotalPassed { get; set; }
        public int TotalFailed { get; set; }
        public int TotalSkipped { get; set; }
        public int ProjectCount { get; set; }
        public double TotalDurationMs { get; set; }
        public bool ZeroTestsFound { get; set; }

        /// <summary>Test assemblies an MTP run announced with "Running tests from".</summary>
        public int MtpAssembliesRun { get; set; }

        /// <summary>The MTP run summary's verdict was "Zero tests ran".</summary>
        public bool MtpZeroTestsVerdict { get; set; }
    }

    private sealed record FailureInfo(string TestName, string Duration, string Message, string SourceRef);
}
