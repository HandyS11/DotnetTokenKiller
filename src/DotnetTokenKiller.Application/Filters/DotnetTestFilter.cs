namespace DotnetTokenKiller.Application.Filters;

using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Domain.Filters;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

public sealed partial class DotnetTestFilter(string? rootPath = null) : IOutputFilter
{
    private const int MaxFailures = 15;
    private const int MessageMaxLen = 200;

    private readonly string _rootPath = rootPath ?? Environment.CurrentDirectory;

    public string Apply(string rawOutput)
    {
        if (string.IsNullOrEmpty(rawOutput))
            return string.Empty;

        var stripped = AnsiStrip.Strip(rawOutput);
        var lines = stripped.Split('\n');

        var failures = new List<FailureInfo>();
        var totalPassed = 0;
        var totalFailed = 0;
        var projectCount = 0;
        double totalDurationMs = 0;
        var zeroTestsFound = false;

        var i = 0;
        while (i < lines.Length)
        {
            var line = lines[i].TrimEnd('\r');

            // Summary: "Passed! - Failed: 0, Passed: 17, ..., Duration: 89 ms - File.dll"
            var summaryMatch = SummaryPattern().Match(line);
            if (summaryMatch.Success)
            {
                totalFailed += int.Parse(summaryMatch.Groups["failed"].Value, CultureInfo.InvariantCulture);
                totalPassed += int.Parse(summaryMatch.Groups["passed"].Value, CultureInfo.InvariantCulture);
                totalDurationMs += double.Parse(summaryMatch.Groups["duration"].Value, CultureInfo.InvariantCulture);
                projectCount++;
                i++;
                continue;
            }

            // Explicit zero-tests pattern
            if (NoTestsPattern().IsMatch(line))
            {
                zeroTestsFound = true;
                i++;
                continue;
            }

            // Failed test header: "  Failed TestName [12 ms]"
            var failedHeaderMatch = FailedTestHeaderPattern().Match(line);
            if (failedHeaderMatch.Success)
            {
                var testName = failedHeaderMatch.Groups["name"].Value.Trim();
                var duration = failedHeaderMatch.Groups["duration"].Value;
                i++;

                // Skip "Error Message:" label
                if (i < lines.Length && ErrorMessageLabelPattern().IsMatch(lines[i].TrimEnd('\r')))
                    i++;

                // Collect message lines until "Stack Trace:", next failed test, or summary
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
                        msgLines.Add(trimmed);
                    i++;
                }

                // Skip "Stack Trace:" label
                if (i < lines.Length && StackTraceLabelPattern().IsMatch(lines[i].TrimEnd('\r')))
                    i++;

                // Find first stack frame with a .cs file reference
                var sourceRef = string.Empty;
                while (i < lines.Length)
                {
                    var current = lines[i].TrimEnd('\r');
                    if (FailedTestHeaderPattern().IsMatch(current) || SummaryPattern().IsMatch(current))
                        break;
                    if (string.IsNullOrEmpty(sourceRef))
                    {
                        var frameMatch = StackFrameFilePattern().Match(current);
                        if (frameMatch.Success)
                            sourceRef = $"{TextHelpers.ShortenPath(frameMatch.Groups["file"].Value, _rootPath)}:line {frameMatch.Groups["line"].Value}";
                    }

                    i++;
                }

                failures.Add(new FailureInfo(testName, duration, CompactMessage(msgLines), sourceRef));
                continue;
            }

            i++;
        }

        // Zero tests: explicit no-tests pattern or all summaries showed 0 tests
        if (zeroTestsFound || (projectCount > 0 && totalPassed == 0 && totalFailed == 0))
            return "✓ dotnet test: 0 tests found\n";

        if (projectCount == 0)
            return string.Empty;

        var elapsed = $"{totalDurationMs / 1000.0:F2}s";

        if (totalFailed == 0)
            return $"✓ dotnet test: {totalPassed} passed ({projectCount} project{(projectCount == 1 ? "" : "s")}, {elapsed})\n";

        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture, $"FAILURES ({totalFailed}):");

        foreach (var f in failures.Take(MaxFailures))
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"  {f.TestName} [{f.Duration} ms]")
              .AppendLine(CultureInfo.InvariantCulture, $"    {f.Message}");
            if (!string.IsNullOrEmpty(f.SourceRef))
                sb.AppendLine(CultureInfo.InvariantCulture, $"    at {f.SourceRef}");
        }

        if (failures.Count > MaxFailures)
            sb.AppendLine(CultureInfo.InvariantCulture, $"+{failures.Count - MaxFailures} more failures");

        sb.AppendLine(CultureInfo.InvariantCulture, $"dotnet test: {totalFailed} failed, {totalPassed} passed ({projectCount} project{(projectCount == 1 ? "" : "s")}, {elapsed})");

        return sb.ToString();
    }

    private sealed record FailureInfo(string TestName, string Duration, string Message, string SourceRef);

    private static string CompactMessage(List<string> lines)
    {
        if (lines.Count == 0)
            return string.Empty;

        // xUnit Assert.Equal multi-line: "Expected: ..." and "Actual: ..." on separate lines
        var expectedLine = lines.Find(l => l.StartsWith("Expected:", StringComparison.OrdinalIgnoreCase));
        var actualLine = lines.Find(l => l.StartsWith("Actual:", StringComparison.OrdinalIgnoreCase));
        if (expectedLine != null && actualLine != null)
            return TextHelpers.Truncate($"{expectedLine}, {actualLine}", MessageMaxLen);

        return TextHelpers.Truncate(string.Join(" ", lines), MessageMaxLen);
    }

    // Summary: "Passed! - Failed: 0, Passed: 17, Skipped: 0, Total: 17, Duration: 89 ms - File.dll"
    [GeneratedRegex(@"(?:Passed|Failed)!\s+-\s+Failed:\s+(?<failed>\d+),\s+Passed:\s+(?<passed>\d+),\s+Skipped:\s+\d+,\s+Total:\s+\d+,\s+Duration:\s+(?<duration>[\d.]+)\s+ms", RegexOptions.IgnoreCase)]
    private static partial Regex SummaryPattern();

    // "  Failed FullyQualifiedTestName [12 ms]"
    [GeneratedRegex(@"^\s+Failed\s+(?<name>.+?)\s+\[(?<duration>\d+)\s+ms\]\s*$")]
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
    [GeneratedRegex(@"No test matches the given testcase filter|No test is available", RegexOptions.IgnoreCase)]
    private static partial Regex NoTestsPattern();
}
