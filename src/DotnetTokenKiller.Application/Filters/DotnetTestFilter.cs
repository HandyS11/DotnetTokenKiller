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

    private string RootPath => rootPath ?? Environment.CurrentDirectory;

    /// <summary>Applies the filter to the raw test output.</summary>
    /// <param name="rawOutput">The raw test output to filter.</param>
    /// <param name="exitCode">The process exit code; the sole source of truth for the success/failure verdict.</param>
    public string Apply(string rawOutput, int exitCode)
    {
        if (string.IsNullOrEmpty(rawOutput))
        {
            return string.Empty;
        }

        var lines = AnsiStrip.Strip(rawOutput).Split('\n');
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

            var mtpSummaryMatch = MtpSummaryPattern().Match(line);
            if (mtpSummaryMatch.Success)
            {
                AccumulateMtpSummary(mtpSummaryMatch, state);
                i++;
                continue;
            }

            if (NoTestsPattern().IsMatch(line))
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
        var duration = failedHeaderMatch.Groups["duration"].Value;
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
        var duration = failedMatch.Groups["duration"].Value.Trim();
        i++;

        // MTP has no "Error Message:"/"Stack Trace:" labels — collect the indented continuation
        // lines that follow the failure header until the next failure line, a summary, or a
        // non-indented line, pulling a source reference out of the first stack frame we see.
        var msgLines = new List<string>();
        var sourceRef = string.Empty;
        while (i < lines.Length)
        {
            var current = lines[i].TrimEnd('\r');
            if (current.Length == 0 || !char.IsWhiteSpace(current[0])
                                    || MtpFailedTestPattern().IsMatch(current)
                                    || FailedTestHeaderPattern().IsMatch(current)
                                    || MtpSummaryPattern().IsMatch(current)
                                    || SummaryPattern().IsMatch(current))
            {
                break;
            }

            if (string.IsNullOrEmpty(sourceRef))
            {
                var frameMatch = StackFrameFilePattern().Match(current);
                if (frameMatch.Success)
                {
                    sourceRef =
                        $"{TextHelpers.ShortenPath(frameMatch.Groups["file"].Value, RootPath)}:line {frameMatch.Groups["line"].Value}";
                }
            }

            var trimmed = current.Trim();
            if (!string.IsNullOrEmpty(trimmed) && !StackFrameFilePattern().IsMatch(current))
            {
                msgLines.Add(trimmed);
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
                var frameMatch = StackFrameFilePattern().Match(current);
                if (frameMatch.Success)
                {
                    sourceRef =
                        $"{TextHelpers.ShortenPath(frameMatch.Groups["file"].Value, RootPath)}:line {frameMatch.Groups["line"].Value}";
                }
            }

            i++;
        }

        return (sourceRef, i);
    }

    private static void AccumulateSummary(Match summaryMatch, ParseState state)
    {
        state.TotalFailed += int.Parse(summaryMatch.Groups["failed"].Value, CultureInfo.InvariantCulture);
        state.TotalPassed += int.Parse(summaryMatch.Groups["passed"].Value, CultureInfo.InvariantCulture);
        state.TotalSkipped += int.Parse(summaryMatch.Groups["skipped"].Value, CultureInfo.InvariantCulture);
        state.TotalDurationMs += NormalizeDurationToMs(
            double.Parse(summaryMatch.Groups["duration"].Value, CultureInfo.InvariantCulture),
            summaryMatch.Groups["unit"].Value);
        state.ProjectCount++;
    }

    private static void AccumulateMtpSummary(Match summaryMatch, ParseState state)
    {
        state.TotalFailed += int.Parse(summaryMatch.Groups["failed"].Value, CultureInfo.InvariantCulture);
        state.TotalPassed += int.Parse(summaryMatch.Groups["passed"].Value, CultureInfo.InvariantCulture);
        state.TotalSkipped += int.Parse(summaryMatch.Groups["skipped"].Value, CultureInfo.InvariantCulture);
        var durationGroup = summaryMatch.Groups["duration"];
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

        // Any parsed failure header or a non-zero summary failure count always renders — even without
        // a full summary (crashed host) — so a failed run can never silently collapse to empty.
        if (state.TotalFailed > 0 || state.Failures.Count > 0)
        {
            return FormatFailures(state, elapsed);
        }

        // Skipped-only run: tests were discovered but none executed. This is a success, but it is
        // not "0 tests found" — surface the skipped count so the distinction isn't lost.
        if (exitCode == 0 && state is { TotalPassed: 0, TotalSkipped: > 0 })
        {
            return $"✓ dotnet test: {state.TotalSkipped} skipped, 0 executed\n";
        }

        // Genuine "nothing to run": explicit no-tests pattern or an all-zero summary.
        var zeroTestsSignal = state.ZeroTestsFound || state is { ProjectCount: > 0, TotalPassed: 0 };
        if (exitCode == 0 && zeroTestsSignal)
        {
            return "✓ dotnet test: 0 tests found\n";
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

        sb.AppendLine(CultureInfo.InvariantCulture,
            $"dotnet test: {failedCount} failed, {state.TotalPassed} passed{(state.TotalSkipped > 0 ? $", {state.TotalSkipped} skipped" : string.Empty)} ({state.ProjectCount} project{(state.ProjectCount == 1 ? "" : "s")}, {elapsed})");

        return sb.ToString();
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
    // Duration unit can be ms, s, m, or h
    [GeneratedRegex(
        @"(?:Passed|Failed)!\s+-\s+Failed:\s+(?<failed>\d+),\s+Passed:\s+(?<passed>\d+),\s+Skipped:\s+(?<skipped>\d+),\s+Total:\s+\d+,\s+Duration:\s+(?<duration>[\d.]+)\s+(?<unit>ms|s|m|h)",
        RegexOptions.IgnoreCase)]
    private static partial Regex SummaryPattern();

    // "  Failed FullyQualifiedTestName [12 ms]", "[< 1 ms]", "[1 s]", or "[1 m 30 s]".
    // The full duration (number + unit) is captured so slow, second/minute-scale tests survive.
    [GeneratedRegex(@"^\s+Failed\s+(?<name>.+?)\s+\[(?<duration>(?:< )?[\d.]+ (?:ms|s|m(?: \d+ s)?))\]\s*$")]
    private static partial Regex FailedTestHeaderPattern();

    // .NET 9 Microsoft.Testing.Platform failure line: "failed FullyQualifiedTestName (12ms)"
    [GeneratedRegex(@"^failed\s+(?<name>\S+)(?:\s+\((?<duration>[^)]+)\))?")]
    private static partial Regex MtpFailedTestPattern();

    // .NET 9 MTP summary: "Test summary: total: 10, failed: 1, succeeded: 9, skipped: 0, duration: 2.3s"
    [GeneratedRegex(
        @"^Test summary: total: (?<total>\d+), failed: (?<failed>\d+), succeeded: (?<passed>\d+), skipped: (?<skipped>\d+)(?:, duration: (?<duration>[\d.]+)\s*(?<unit>ms|s|m|h))?",
        RegexOptions.IgnoreCase)]
    private static partial Regex MtpSummaryPattern();

    // Stack frame with CS file: "   at Class.Method() in /path/to/File.cs:line 42"
    [GeneratedRegex(@"in (?<file>.+\.cs):line (?<line>\d+)")]
    private static partial Regex StackFrameFilePattern();

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
    }

    private sealed record FailureInfo(string TestName, string Duration, string Message, string SourceRef);
}
