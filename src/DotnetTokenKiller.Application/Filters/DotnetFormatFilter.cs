using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Domain.Filters;
using DotnetTokenKiller.Domain.Text;

namespace DotnetTokenKiller.Application.Filters;

/// <summary>Condenses dotnet format output to a concise summary.</summary>
/// <remarks>
/// When <c>dotnet format</c> finds nothing to change, it produces no output at all.
/// This filter treats empty or whitespace-only input on a zero exit code as a confirmed
/// success signal and synthesises a <c>✓ dotnet format (nothing to format)</c> confirmation
/// line so that AI agents receive an explicit positive signal instead of silence.
/// </remarks>
/// <param name="rootPath">Optional root path used to shorten file paths in violation messages.</param>
public sealed partial class DotnetFormatFilter(string? rootPath = null) : IOutputFilter
{
    private const int MaxViolationLines = 10;

    private string RootPath => rootPath ?? Environment.CurrentDirectory;

    /// <summary>Applies the filter to the raw format output.</summary>
    /// <param name="rawOutput">The raw format output to filter.</param>
    /// <param name="exitCode">The process exit code; the sole source of truth for the success/failure verdict.</param>
    public string Apply(string rawOutput, int exitCode)
    {
        var stripped = string.IsNullOrEmpty(rawOutput) ? string.Empty : AnsiStrip.Strip(rawOutput);

        // Heuristic: dotnet format produces no output when nothing needs formatting.
        // Synthesise a positive confirmation so agents receive an explicit success signal —
        // but only when the process actually exited successfully.
        if (string.IsNullOrWhiteSpace(stripped))
        {
            return exitCode == 0 ? "✓ dotnet format (nothing to format)\n" : string.Empty;
        }

        var lines = stripped.Split('\n');

        // Matched once and carried, rather than testing with IsMatch here and re-matching per line
        // when formatting: the second match can only reproduce the first, so keeping it would mean
        // running the pattern twice per violation and writing an unreachable "no match" branch.
        var violations = Array.ConvertAll(lines, l => ViolationPattern().Match(l))
            .Where(m => m.Success)
            .ToArray();
        if (violations.Length == 0 && exitCode != 0)
        {
            // Failed run with nothing parsed (crashed process, localized SDK, garbled output):
            // degrade to blank so FilteredRunUseCase's raw-tail fallback surfaces the real output
            // instead of a misleadingly clean "0 violations" header.
            return string.Empty;
        }

        if (violations.Length > 0)
        {
            return BuildViolationOutput(violations);
        }

        var elapsedStr = ParseElapsedStr(lines);
        var formattedCount = Array.FindAll(lines, l => FormattedFilePattern().IsMatch(l)).Length;
        if (formattedCount > 0)
        {
            var suffix = elapsedStr is null ? "" : $", {elapsedStr}";
            return $"✓ dotnet format ({formattedCount} file{(formattedCount == 1 ? "" : "s")} formatted{suffix})\n";
        }

        var elapsedPart = elapsedStr is null ? "" : $", {elapsedStr}";
        return $"✓ dotnet format (nothing to format{elapsedPart})\n";
    }

    private string BuildViolationOutput(Match[] violations)
    {
        var sb = new StringBuilder();
        sb.AppendLine(CultureInfo.InvariantCulture,
            $"dotnet format: {violations.Length} violation{(violations.Length == 1 ? "" : "s")}");
        var shown = Math.Min(violations.Length, MaxViolationLines);
        for (var i = 0; i < shown; i++)
        {
            sb.AppendLine(FormatViolationLine(violations[i]));
        }

        if (violations.Length > MaxViolationLines)
        {
            var extra = violations.Length - MaxViolationLines;
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"... and {extra} more violation{(extra == 1 ? "" : "s")}");
        }

        // Normalize to '\n' so output stays identical across platforms; AppendLine emits '\r\n' on Windows.
        return sb.ToString().ReplaceLineEndings("\n");
    }

    private string FormatViolationLine(Match match)
    {
        var shortPath = TextHelpers.ShortenPath(match.Groups["path"].Value.Trim(), RootPath);
        return
            $"{shortPath}({match.Groups["lineCol"].Value}): {match.Groups["level"].Value} {match.Groups["rest"].Value.Trim()}";
    }

    private static string? ParseElapsedStr(string[] lines)
    {
        foreach (var line in lines)
        {
            var match = FormatCompletePattern().Match(line);
            if (!match.Success)
            {
                continue;
            }

            if (double.TryParse(match.Groups["ms"].Value, CultureInfo.InvariantCulture, out var ms))
            {
                return $"{ms / 1000.0:F2}s";
            }
        }

        return null;
    }

    // "  Formatted code file '/path/to/file.cs'."
    [GeneratedRegex(@"Formatted code file '", RegexOptions.IgnoreCase)]
    private static partial Regex FormattedFilePattern();

    // "Format complete in 2345ms."
    [GeneratedRegex(@"Format complete in (?<ms>[\d.]+)ms", RegexOptions.IgnoreCase)]
    private static partial Regex FormatCompletePattern();

    // "/path/to/file.cs(1,1): error whitespace: Fix whitespace formatting."
    [GeneratedRegex(@"(?<path>.+?)\((?<lineCol>\d+,\d+)\):\s+(?<level>error|warning)\s+(?<rest>\w+:.+)",
        RegexOptions.IgnoreCase)]
    private static partial Regex ViolationPattern();
}
