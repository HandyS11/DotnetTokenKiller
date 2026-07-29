using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Domain.Filters;
using DotnetTokenKiller.Domain.Text;

namespace DotnetTokenKiller.Application.Filters;

/// <summary>Condenses dotnet clean output to a concise summary.</summary>
/// <param name="rootPath">Optional root path used to shorten file paths in error messages.</param>
public sealed partial class DotnetCleanFilter(string? rootPath = null) : IOutputFilter
{
    private const int MaxErrorLines = 5;

    private string RootPath => rootPath ?? Environment.CurrentDirectory;

    /// <summary>Applies the filter to the raw clean output.</summary>
    /// <param name="rawOutput">The raw clean output to filter.</param>
    /// <param name="exitCode">The process exit code; the sole source of truth for the success/failure verdict.</param>
    public string Apply(string rawOutput, int exitCode)
    {
        if (string.IsNullOrEmpty(rawOutput))
        {
            return string.Empty;
        }

        if (exitCode == 0)
        {
            return "✓ dotnet clean\n";
        }

        var stripped = AnsiStrip.Strip(rawOutput);
        var lines = stripped.Split('\n');

        var sb = new StringBuilder();
        var count = 0;
        var totalErrors = 0;
        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');
            if (!line.Contains("error", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (NoiseSummaryPattern().IsMatch(line))
            {
                continue;
            }

            totalErrors++;
            if (count >= MaxErrorLines)
            {
                continue;
            }

            sb.AppendLine(TextHelpers.ShortenPath(line, RootPath));
            count++;
        }

        if (totalErrors > MaxErrorLines)
        {
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"... and {totalErrors - MaxErrorLines} more error{(totalErrors - MaxErrorLines == 1 ? "" : "s")}");
        }

        // Normalize to '\n' so output stays identical across platforms; AppendLine emits '\r\n' on Windows.
        return sb.ToString().ReplaceLineEndings("\n");
    }

    // Noise: "    0 Warning(s)" and "    6 Error(s)" summary lines
    [GeneratedRegex(@"^\s+\d+ (Warning|Error)\(s\)\s*$")]
    private static partial Regex NoiseSummaryPattern();
}
