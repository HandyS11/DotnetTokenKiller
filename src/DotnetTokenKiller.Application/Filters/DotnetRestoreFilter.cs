using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Domain.Filters;

namespace DotnetTokenKiller.Application.Filters;

/// <summary>Condenses dotnet restore output to a concise summary.</summary>
/// <param name="rootPath">Optional root path used to shorten file paths in error messages.</param>
public sealed partial class DotnetRestoreFilter(string? rootPath = null) : IOutputFilter
{
    private const int MessageMaxLen = 200;

    private string RootPath => rootPath ?? Environment.CurrentDirectory;

    /// <summary>Applies the filter to the raw restore output.</summary>
    /// <param name="rawOutput">The raw restore output to filter.</param>
    /// <param name="exitCode">The process exit code; the sole source of truth for the success/failure verdict.</param>
    public string Apply(string rawOutput, int exitCode)
    {
        if (string.IsNullOrEmpty(rawOutput))
        {
            return string.Empty;
        }

        var lines = AnsiStrip.Strip(rawOutput).Split(["\r\n", "\n"], StringSplitOptions.None);
        var state = new ParseState();
        foreach (var rawLine in lines)
        {
            ProcessLine(rawLine.Trim(), state);
        }

        return FormatOutput(state, exitCode);
    }

    private void ProcessLine(string line, ParseState state)
    {
        var restoredMatch = RestoredPattern().Match(line);
        if (restoredMatch.Success)
        {
            state.RestoredCount++;
            // Track the slowest single restore, not the sum: restores run in parallel, so summing
            // the per-project times wildly overstates the actual wall-clock elapsed.
            var ms = ParseDurationToMs(restoredMatch.Groups["dur"].Value);
            if (ms > state.MaxDurationMs)
            {
                state.MaxDurationMs = ms;
            }

            return;
        }

        if (AllUpToDatePattern().IsMatch(line))
        {
            state.AllUpToDate = true;
            return;
        }

        var partialMatch = PartialUpToDatePattern().Match(line);
        if (partialMatch.Success)
        {
            if (int.TryParse(partialMatch.Groups["count"].Value, CultureInfo.InvariantCulture, out var count))
            {
                state.UpToDateCount = count;
            }

            return;
        }

        if (!TryParseProjectFirstError(line, state.Errors))
        {
            TryParseStandardError(line, state.Errors);
        }
    }

    private bool TryParseProjectFirstError(string line, List<NuGetError> errors)
    {
        // "/path/proj.csproj : error NU1101: message" (project path before error code)
        var match = NuGetErrorProjectFirstPattern().Match(line);
        if (!match.Success)
        {
            return false;
        }

        var proj = TextHelpers.ShortenPath(match.Groups["proj"].Value.Trim(), RootPath);
        errors.Add(new NuGetError(
            match.Groups["code"].Value,
            TextHelpers.Truncate(match.Groups["message"].Value.Trim(), MessageMaxLen),
            proj));
        return true;
    }

    private void TryParseStandardError(string line, List<NuGetError> errors)
    {
        // "error NU1101: message [/path/proj.csproj]" (standard NuGet error format)
        var match = NuGetErrorStandardPattern().Match(line);
        if (!match.Success)
        {
            return;
        }

        var projRaw = match.Groups["proj"].Value.Trim();
        var proj = string.IsNullOrEmpty(projRaw)
            ? string.Empty
            : TextHelpers.ShortenPath(projRaw, RootPath);
        errors.Add(new NuGetError(
            match.Groups["code"].Value,
            TextHelpers.Truncate(match.Groups["message"].Value.Trim(), MessageMaxLen),
            proj));
    }

    private static double ParseDurationToMs(string duration)
    {
        var match = DurationPattern().Match(duration.Trim());
        if (!match.Success)
        {
            return 0;
        }

        if (match.Groups["min"].Success)
        {
            var minutes = double.Parse(match.Groups["min"].Value, CultureInfo.InvariantCulture);
            var seconds = match.Groups["minsec"].Success
                ? double.Parse(match.Groups["minsec"].Value, CultureInfo.InvariantCulture)
                : 0;
            return (minutes * 60_000) + (seconds * 1_000);
        }

        if (match.Groups["sec"].Success)
        {
            return double.Parse(match.Groups["sec"].Value, CultureInfo.InvariantCulture) * 1_000;
        }

        return double.Parse(match.Groups["ms"].Value, CultureInfo.InvariantCulture);
    }

    private static string FormatOutput(ParseState state, int exitCode)
    {
        if (state.Errors.Count == 0 && exitCode != 0)
        {
            // Failed run with nothing parsed (crashed process, localized SDK, garbled output):
            // degrade to blank so FilteredRunUseCase's raw-tail fallback surfaces the real output
            // instead of a misleadingly clean "0 errors" header.
            return string.Empty;
        }

        if (state.Errors.Count > 0)
        {
            return FormatErrors(state.Errors);
        }

        var totalProjects = state.RestoredCount + state.UpToDateCount;

        if (totalProjects == 0 && state.AllUpToDate)
        {
            return "✓ dotnet restore (all up-to-date)\n";
        }

        if (totalProjects == 0)
        {
            return string.Empty;
        }

        var elapsed = $"{state.MaxDurationMs / 1000.0:F2}s";
        return $"✓ dotnet restore ({totalProjects} project{(totalProjects == 1 ? "" : "s")}, {elapsed})\n";
    }

    private static string FormatErrors(List<NuGetError> errors)
    {
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture,
            $"dotnet restore: {errors.Count} error{(errors.Count == 1 ? "" : "s")}");
        foreach (var e in errors)
        {
            if (string.IsNullOrEmpty(e.Project))
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"  {e.Code}: {e.Message}");
            }
            else
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"  {e.Code}: {e.Message} ({e.Project})");
            }
        }

        return sb.ToString();
    }

    // "  Restored /path/Project.csproj (in 123 ms)." — duration may be ms, seconds, or minutes:
    // "(in 123 ms)", "(in 1.02 sec)", "(in 1 min)", "(in 1 min 5 sec)".
    [GeneratedRegex(
        @"^\s*Restored .+\.[a-z]+proj \(in (?<dur>[\d.]+ (?:ms|sec)|(?:\d+ min)(?: \d+(?:\.\d+)? sec)?)\)",
        RegexOptions.IgnoreCase)]
    private static partial Regex RestoredPattern();

    // Parses a captured restore duration ("123 ms", "1.02 sec", "1 min", "1 min 5 sec") into groups.
    [GeneratedRegex(
        @"^(?:(?<min>\d+) min(?: (?<minsec>[\d.]+) sec)?|(?<sec>[\d.]+) sec|(?<ms>[\d.]+) ms)$",
        RegexOptions.IgnoreCase)]
    private static partial Regex DurationPattern();

    // "All projects are up-to-date for restore."
    [GeneratedRegex("All projects are up-to-date for restore", RegexOptions.IgnoreCase)]
    private static partial Regex AllUpToDatePattern();

    // "3 of 5 projects are up-to-date for restore."
    [GeneratedRegex(@"(?<count>\d+) of \d+ projects are up-to-date for restore", RegexOptions.IgnoreCase)]
    private static partial Regex PartialUpToDatePattern();

    // "/path/proj.csproj : error NU1101: message here" (NU or MSB class)
    [GeneratedRegex(@"^\s*(?<proj>.+?\.[a-z]+proj)\s*:\s*error\s+(?<code>(?:NU|MSB)\d+):\s+(?<message>.+?)\s*$",
        RegexOptions.IgnoreCase)]
    private static partial Regex NuGetErrorProjectFirstPattern();

    // "error NU1101: message text [/path/proj.csproj]" (NU or MSB class)
    [GeneratedRegex(@"error\s+(?<code>(?:NU|MSB)\d+):\s+(?<message>[^\[]+)(?:\s*\[(?<proj>[^\]]+)\])?\s*$",
        RegexOptions.IgnoreCase)]
    private static partial Regex NuGetErrorStandardPattern();

    private sealed class ParseState
    {
        public List<NuGetError> Errors { get; } = [];
        public int RestoredCount { get; set; }
        public int UpToDateCount { get; set; }
        public bool AllUpToDate { get; set; }
        public double MaxDurationMs { get; set; }
    }

    private sealed record NuGetError(string Code, string Message, string Project);
}
