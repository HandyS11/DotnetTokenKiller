using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Domain.Filters;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DotnetTokenKiller.Application.Filters;

public sealed partial class DotnetPackFilter(string? rootPath = null) : IOutputFilter
{
    private const string Separator = "---";
    private const int MessageMaxLen = 120;

    private readonly string _rootPath = rootPath ?? Environment.CurrentDirectory;

    public string Apply(string rawOutput)
    {
        if (string.IsNullOrEmpty(rawOutput))
            return string.Empty;

        var stripped = AnsiStrip.Strip(rawOutput);
        var lines = stripped.Split('\n');

        var diagnostics = new List<Diagnostic>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var projectCount = 0;
        var elapsed = string.Empty;
        var nupkgFilename = string.Empty;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');

            // Capture .nupkg filename (last match wins)
            var packMatch = PackOutputPattern().Match(line);
            if (packMatch.Success)
            {
                nupkgFilename = Path.GetFileName(packMatch.Groups["path"].Value.Trim());
                continue;
            }

            // Count project compile output lines (.dll / .exe)
            if (ProjectOutputPattern().IsMatch(line))
            {
                projectCount++;
                continue;
            }

            // Parse elapsed time
            var elapsedMatch = TimeElapsedPattern().Match(line);
            if (elapsedMatch.Success)
            {
                elapsed = FormatElapsed(elapsedMatch.Value);
                continue;
            }

            // Parse diagnostic lines (errors / warnings)
            var diagMatch = DiagnosticPattern().Match(line);
            if (!diagMatch.Success)
                continue;

            var key = $"{diagMatch.Groups["file"].Value}({diagMatch.Groups["line"].Value},{diagMatch.Groups["col"].Value}):{diagMatch.Groups["code"].Value}";
            if (!seen.Add(key))
                continue;

            diagnostics.Add(new Diagnostic(
                TextHelpers.ShortenPath(diagMatch.Groups["file"].Value.Trim(), _rootPath),
                Line: diagMatch.Groups["line"].Value,
                Col: diagMatch.Groups["col"].Value,
                Level: diagMatch.Groups["level"].Value,
                Code: diagMatch.Groups["code"].Value,
                Message: TextHelpers.Truncate(diagMatch.Groups["message"].Value.Trim(), MessageMaxLen)));
        }

        var errors = diagnostics.FindAll(d => d.Level == "error");
        var warnings = diagnostics.FindAll(d => d.Level == "warning");

        if (errors.Count > 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine(CultureInfo.InvariantCulture, $"dotnet pack: {errors.Count} error{(errors.Count == 1 ? "" : "s")}, {warnings.Count} warning{(warnings.Count == 1 ? "" : "s")}")
              .AppendLine(Separator);
            AppendGroupedByFile(sb, errors);
            AppendTopCodes(sb, errors);
            if (warnings.Count > 0)
                sb.AppendLine(CultureInfo.InvariantCulture, $"{warnings.Count} warning{(warnings.Count == 1 ? "" : "s")} suppressed (use -v to see)");
            return sb.ToString();
        }

        if (warnings.Count > 0)
        {
            var sb = new StringBuilder();
            sb.AppendLine(CultureInfo.InvariantCulture, $"dotnet pack: 0 errors, {warnings.Count} warning{(warnings.Count == 1 ? "" : "s")}{BuildContext(projectCount, elapsed, nupkgFilename)}")
              .AppendLine(Separator);
            AppendGroupedByCode(sb, warnings);
            return sb.ToString();
        }

        var context = BuildContext(projectCount, elapsed, nupkgFilename);
        return $"✓ dotnet pack{context}\n";
    }

    private sealed record Diagnostic(string File, string Line, string Col, string Level, string Code, string Message);

    private static string BuildContext(int projectCount, string elapsed, string nupkgFilename)
    {
        var namePart = string.IsNullOrEmpty(nupkgFilename) ? string.Empty : $" → {nupkgFilename}";
        var projectSuffix = projectCount == 1 ? "" : "s";
        var countPart = projectCount > 0 ? $"{projectCount} project{projectSuffix}" : string.Empty;
        var timePart = string.IsNullOrEmpty(elapsed) ? string.Empty : elapsed;

        var details = string.Join(", ", new[] { countPart, timePart }.Where(s => !string.IsNullOrEmpty(s)));
        return string.IsNullOrEmpty(details)
            ? namePart
            : $"{namePart} ({details})";
    }

    private static string FormatElapsed(string timeElapsedLine)
    {
        var match = TimeSpanValuePattern().Match(timeElapsedLine);
        if (!match.Success)
            return string.Empty;

        if (TimeSpan.TryParse(match.Value, CultureInfo.InvariantCulture, out var ts))
            return $"{ts.TotalSeconds:F2}s";

        return string.Empty;
    }

    private static void AppendGroupedByCode(StringBuilder sb, List<Diagnostic> diags)
    {
        foreach (var group in diags.GroupBy(d => d.Code).OrderBy(g => g.Key))
        {
            var items = group.ToList();
            sb.AppendLine(CultureInfo.InvariantCulture, $"{group.Key} ({items.Count}x)");
            foreach (var d in items)
                sb.AppendLine(CultureInfo.InvariantCulture, $"  {d.File}:{d.Line} — {d.Message}");
        }
    }

    private static void AppendGroupedByFile(StringBuilder sb, List<Diagnostic> errors)
    {
        foreach (var group in errors.GroupBy(d => d.File).OrderByDescending(g => g.Count()))
        {
            var items = group.ToList();
            sb.AppendLine(CultureInfo.InvariantCulture, $"{group.Key} ({items.Count} error{(items.Count == 1 ? "" : "s")})");
            foreach (var d in items)
                sb.AppendLine(CultureInfo.InvariantCulture, $"  ({d.Line},{d.Col}) {d.Code}: {d.Message}");
        }
    }

    private static void AppendTopCodes(StringBuilder sb, List<Diagnostic> errors)
    {
        var topCodes = errors
            .GroupBy(d => d.Code)
            .OrderByDescending(g => g.Count())
            .Take(5)
            .Select(g => $"{g.Key} ({g.Count()}x)")
            .ToList();

        if (topCodes.Count > 0)
            sb.AppendLine(CultureInfo.InvariantCulture, $"Top codes: {string.Join(", ", topCodes)}");
    }

    // "Successfully created package '/path/to/MyProject.1.0.0.nupkg'."
    [GeneratedRegex(@"Successfully created package '(?<path>[^']+)'", RegexOptions.IgnoreCase)]
    private static partial Regex PackOutputPattern();

    // "  MyProject -> /path/to/bin/Debug/net10.0/MyProject.dll"
    [GeneratedRegex(@"^\s+\S+ -> .+\.(dll|exe)\s*$")]
    private static partial Regex ProjectOutputPattern();

    // Matches: /path/file.cs(10,5): error CS0001: message [project.csproj]
    [GeneratedRegex(@"^\s*(?<file>[^()]+)\((?<line>\d+),(?<col>\d+)\):\s+(?<level>error|warning)\s+(?<code>[A-Z]+\d+):\s+(?<message>[^\[]+?)(?:\s*\[.+?\])?\s*$")]
    private static partial Regex DiagnosticPattern();

    // Matches "Time Elapsed HH:MM:SS.ff"
    [GeneratedRegex(@"Time Elapsed \d{2}:\d{2}:\d{2}\.\d+")]
    private static partial Regex TimeElapsedPattern();

    // Extracts the HH:MM:SS.ff value from a Time Elapsed line
    [GeneratedRegex(@"\d{2}:\d{2}:\d{2}\.\d+")]
    private static partial Regex TimeSpanValuePattern();
}
