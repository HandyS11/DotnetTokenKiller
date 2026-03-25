using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Domain.Filters;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace DotnetTokenKiller.Application.Filters;

/// <summary>Condenses dotnet build output to a concise error/warning summary.</summary>
/// <param name="rootPath">Optional root path used to shorten file paths in diagnostics.</param>
public sealed partial class DotnetBuildFilter(string? rootPath = null) : IOutputFilter
{
    private const string Separator = "---";
    private const int MessageMaxLen = 120;

    private string RootPath => rootPath ?? Environment.CurrentDirectory;

    /// <summary>Applies the filter to the raw build output.</summary>
    /// <param name="rawOutput">The raw build output to filter.</param>
    public string Apply(string rawOutput)
    {
        if (string.IsNullOrEmpty(rawOutput))
        {
            return string.Empty;
        }

        var (diagnostics, projectCount, elapsed) = ParseLines(AnsiStrip.Strip(rawOutput).Split('\n'));
        var errors = diagnostics.Where(d => d.Level == "error").ToList();
        var warnings = diagnostics.Where(d => d.Level == "warning").ToList();
        var context = BuildContext(projectCount, elapsed);

        if (errors.Count == 0 && warnings.Count == 0)
        {
            return $"✓ dotnet build{context}\n";
        }

        return FormatDiagnostics(errors, warnings, context);
    }

    private (List<Diagnostic> Diagnostics, int ProjectCount, string Elapsed) ParseLines(string[] lines)
    {
        var diagnostics = new List<Diagnostic>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var projectCount = 0;
        var elapsed = string.Empty;

        foreach (var rawLine in lines)
        {
            var line = rawLine.TrimEnd('\r');

            // Count project output lines (e.g., "  MyProject -> /path/to/bin/Debug/...")
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

            // Skip noise lines
            if (IsNoiseLine(line))
            {
                continue;
            }

            // Parse diagnostic lines
            if (TryAddDiagnosticLine(line, seen, diagnostics))
            {
                continue;
            }

            // Parse diagnostic lines without file/line/col (e.g., "MSBUILD : error MSB1001: message")
            var simpleDiagMatch = SimpleDiagnosticPattern().Match(line);
            if (!simpleDiagMatch.Success)
            {
                continue;
            }

            var simpleKey = $"{simpleDiagMatch.Groups["code"].Value}:{simpleDiagMatch.Groups["message"].Value}";
            if (!seen.Add(simpleKey))
            {
                continue;
            }

            diagnostics.Add(new Diagnostic(
                string.Empty,
                string.Empty,
                string.Empty,
                simpleDiagMatch.Groups["level"].Value,
                simpleDiagMatch.Groups["code"].Value,
                TextHelpers.Truncate(simpleDiagMatch.Groups["message"].Value.Trim(), MessageMaxLen)));
        }

        return (diagnostics, projectCount, elapsed);
    }

    private bool TryAddDiagnosticLine(string line, HashSet<string> seen, List<Diagnostic> diagnostics)
    {
        var diagMatch = DiagnosticPattern().Match(line);
        if (!diagMatch.Success)
        {
            return false;
        }

        var key =
            $"{diagMatch.Groups["file"].Value}({diagMatch.Groups["line"].Value},{diagMatch.Groups["col"].Value}):{diagMatch.Groups["code"].Value}";
        if (seen.Add(key))
        {
            diagnostics.Add(new Diagnostic(
                TextHelpers.ShortenPath(diagMatch.Groups["file"].Value.Trim(), RootPath),
                diagMatch.Groups["line"].Value,
                diagMatch.Groups["col"].Value,
                diagMatch.Groups["level"].Value,
                diagMatch.Groups["code"].Value,
                TextHelpers.Truncate(diagMatch.Groups["message"].Value.Trim(), MessageMaxLen)));
        }

        return true;
    }

    private static string FormatDiagnostics(List<Diagnostic> errors, List<Diagnostic> warnings, string context)
    {
        var sb = new StringBuilder();

        if (errors.Count == 0)
        {
            sb.AppendLine(CultureInfo.InvariantCulture,
                    $"dotnet build: 0 errors, {warnings.Count} warning{(warnings.Count == 1 ? "" : "s")}{context}")
                .AppendLine(Separator);
            AppendGroupedByCode(sb, warnings);
        }
        else
        {
            sb.AppendLine(CultureInfo.InvariantCulture,
                    $"dotnet build: {errors.Count} error{(errors.Count == 1 ? "" : "s")}, {warnings.Count} warning{(warnings.Count == 1 ? "" : "s")}")
                .AppendLine(Separator);
            AppendGroupedByFile(sb, errors);
            AppendTopCodes(sb, errors);
            if (warnings.Count > 0)
            {
                sb.AppendLine(CultureInfo.InvariantCulture,
                    $"{warnings.Count} warning{(warnings.Count == 1 ? "" : "s")} suppressed (use -v to see)");
            }
        }

        return sb.ToString();
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

    private static string FormatElapsed(string timeElapsedLine)
    {
        var match = TimeSpanValuePattern().Match(timeElapsedLine);
        if (!match.Success)
        {
            return string.Empty;
        }

        if (TimeSpan.TryParse(match.Value, CultureInfo.InvariantCulture, out var ts))
        {
            return $"{ts.TotalSeconds:F2}s";
        }

        return string.Empty;
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
               || NoiseBuildResultPattern().IsMatch(line)
               || NoiseCountPattern().IsMatch(line)
               || NoiseTimeElapsedPattern().IsMatch(line)
               || NoiseProjectOutputPattern().IsMatch(line);
    }

    private static void AppendGroupedByCode(StringBuilder sb, List<Diagnostic> diags)
    {
        foreach (var group in diags.GroupBy(d => d.Code).OrderBy(g => g.Key))
        {
            var items = group.ToList();
            sb.AppendLine(CultureInfo.InvariantCulture, $"{group.Key} ({items.Count}x)");
            foreach (var d in items)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"  {d.File}:{d.Line} — {d.Message}");
            }
        }
    }

    private static void AppendGroupedByFile(StringBuilder sb, List<Diagnostic> errors)
    {
        foreach (var group in errors.GroupBy(d => d.File).OrderByDescending(g => g.Count()))
        {
            var items = group.ToList();
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"{group.Key} ({items.Count} error{(items.Count == 1 ? "" : "s")})");
            foreach (var d in items)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"  ({d.Line},{d.Col}) {d.Code}: {d.Message}");
            }
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
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"Top codes: {string.Join(", ", topCodes)}");
        }
    }

    // Matches: /path/file.cs(10,5): error CS0001: message [project.csproj]
    [GeneratedRegex(
        @"^\s*(?<file>[^()]+)\((?<line>\d+),(?<col>\d+)\):\s+(?<level>error|warning)\s+(?<code>[A-Z]+\d+):\s+(?<message>[^\[]+?)(?:\s*\[.+?\])?\s*$")]
    private static partial Regex DiagnosticPattern();

    // Matches: "MSBUILD : error MSB1001: message" or "CSC : error CS2012: message" (no file/line/col)
    [GeneratedRegex(
        @"^\s*\S*\s*:\s*(?<level>error|warning)\s+(?<code>[A-Z]+\d+):\s+(?<message>.+?)(?:\s*\[.+?\])?\s*$")]
    private static partial Regex SimpleDiagnosticPattern();

    // Matches: "  MyProject -> /path/to/bin/MyProject.dll" (or .exe)
    [GeneratedRegex(@"^\s+\S+ -> .+\.(dll|exe)\s*$")]
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

    // Noise: "    0 Warning(s)" and "    0 Error(s)"
    [GeneratedRegex(@"^\s+\d+ (Warning|Error)\(s\)\s*$")]
    private static partial Regex NoiseCountPattern();

    // Noise: "Time Elapsed ..." line itself
    [GeneratedRegex("^Time Elapsed")]
    private static partial Regex NoiseTimeElapsedPattern();

    // Noise: project output redirect (-> dll/exe) - fallback for IsNoiseLine
    [GeneratedRegex(@"^\s+\S+ -> .+\.(dll|exe)\s*$")]
    private static partial Regex NoiseProjectOutputPattern();

    private sealed record Diagnostic(string File, string Line, string Col, string Level, string Code, string Message);
}
