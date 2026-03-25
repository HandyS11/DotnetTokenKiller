using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Domain.Filters;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

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
    public string Apply(string rawOutput)
    {
        if (string.IsNullOrEmpty(rawOutput))
        {
            return string.Empty;
        }

        var lines = AnsiStrip.Strip(rawOutput).Split('\n');
        var state = ParseLines(lines);
        return FormatOutput(state);
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

    private static string FormatOutput(ParseState state)
    {
        // Zero tests: explicit no-tests pattern or all summaries showed 0 tests
        if (state.ZeroTestsFound || state is { ProjectCount: > 0, TotalPassed: 0, TotalFailed: 0 })
        {
            return "✓ dotnet test: 0 tests found\n";
        }

        if (state.ProjectCount == 0)
        {
            return string.Empty;
        }

        var elapsed = $"{state.TotalDurationMs / 1000.0:F2}s";

        if (state.TotalFailed == 0)
        {
            var skippedSuffix = state.TotalSkipped > 0
                ? $", {state.TotalSkipped} skipped"
                : string.Empty;
            return
                $"\u2713 dotnet test: {state.TotalPassed} passed{skippedSuffix} ({state.ProjectCount} project{(state.ProjectCount == 1 ? "" : "s")}, {elapsed})\n";
        }

        return FormatFailures(state, elapsed);
    }

    private static string FormatFailures(ParseState state, string elapsed)
    {
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"FAILURES ({state.TotalFailed}):");

        foreach (var f in state.Failures.Take(MaxFailures))
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"  {f.TestName} [{f.Duration} ms]")
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
            $"dotnet test: {state.TotalFailed} failed, {state.TotalPassed} passed{(state.TotalSkipped > 0 ? $", {state.TotalSkipped} skipped" : string.Empty)} ({state.ProjectCount} project{(state.ProjectCount == 1 ? "" : "s")}, {elapsed})");

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

    // "  Failed FullyQualifiedTestName [12 ms]" or "  Failed FullyQualifiedTestName [< 1 ms]"
    [GeneratedRegex(@"^\s+Failed\s+(?<name>.+?)\s+\[(?<duration>(?:< )?\d+)\s+ms\]\s*$")]
    private static partial Regex FailedTestHeaderPattern();

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
