using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Domain.Filters;
using DotnetTokenKiller.Domain.Text;

namespace DotnetTokenKiller.Application.Filters;

/// <summary>Condenses dotnet build output to a concise error/warning summary.</summary>
/// <param name="rootPath">Optional root path used to shorten file paths in diagnostics.</param>
public sealed partial class DotnetBuildFilter(string? rootPath = null) : IOutputFilter
{
    private const string Separator = "---";
    private const int MessageMaxLen = 120;

    /// <summary>Stands in for the diagnostic code on MSBuild output that carries none (Exec tasks).</summary>
    private const string NoCode = "(no code)";

    /// <summary>Groups diagnostics that name no source file, such as tool- and project-level errors.</summary>
    private const string NoFile = "(no file)";

    private string RootPath => rootPath ?? Environment.CurrentDirectory;

    /// <summary>Applies the filter to the raw build output.</summary>
    /// <param name="rawOutput">The raw build output to filter.</param>
    /// <param name="exitCode">The process exit code; the sole source of truth for the success/failure verdict.</param>
    public string Apply(string rawOutput, int exitCode)
    {
        if (string.IsNullOrEmpty(rawOutput))
        {
            return string.Empty;
        }

        var parsed = ParseLines(AnsiStrip.Strip(rawOutput).Split('\n'));
        var errors = parsed.Diagnostics.Where(d => d.Level == "error").ToList();
        var warnings = parsed.Diagnostics.Where(d => d.Level == "warning").ToList();
        var context = BuildContext(parsed.ProjectCount, parsed.Elapsed);

        // MSBuild's own "N Error(s)" tally is the authority on how many diagnostics the build
        // raised. It counts ones this filter deliberately collapses (the same error reported once
        // per target framework) and ones the regexes never matched, so taking the larger of the two
        // stops the header from quietly reporting fewer problems than the build actually found.
        var errorTotal = Math.Max(parsed.DeclaredErrors ?? 0, errors.Count);
        var warningTotal = Math.Max(parsed.DeclaredWarnings ?? 0, warnings.Count);

        if (errors.Count == 0 && warnings.Count == 0)
        {
            // Nothing parsed (crashed process, localized SDK, garbled output). A clean tick is only
            // honest when MSBuild's summary agrees there was nothing to report; otherwise degrade to
            // blank so FilteredRunUseCase's raw-tail fallback surfaces the output we failed to read.
            return exitCode == 0 && errorTotal == 0 && warningTotal == 0
                ? $"✓ dotnet build{context}\n"
                : string.Empty;
        }

        return FormatDiagnostics(errors, warnings, context, exitCode, errorTotal, warningTotal);
    }

    private ParsedOutput ParseLines(string[] lines)
    {
        var diagnostics = new List<Diagnostic>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var projectCount = 0;
        var elapsed = string.Empty;
        int? declaredErrors = null;
        int? declaredWarnings = null;

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

            // MSBuild's end-of-build tally ("    3 Error(s)"). Read before it is discarded as
            // noise: it is the only count in the output that is not derived from what we parsed.
            var countMatch = DeclaredCountPattern().Match(line);
            if (countMatch.Success)
            {
                if (int.TryParse(countMatch.Groups["count"].Value, NumberStyles.None,
                        CultureInfo.InvariantCulture, out var declared))
                {
                    if (countMatch.Groups["kind"].Value == "Error")
                    {
                        declaredErrors = declared;
                    }
                    else
                    {
                        declaredWarnings = declared;
                    }
                }

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

            var simpleDiagnostic = new Diagnostic(
                string.Empty,
                string.Empty,
                string.Empty,
                simpleDiagMatch.Groups["level"].Value,
                simpleDiagMatch.Groups["code"].Value,
                TextHelpers.Truncate(simpleDiagMatch.Groups["message"].Value.Trim(), MessageMaxLen));

            // Keyed off the rendered message for the same reason as the file-anchored diagnostics.
            if (!seen.Add($"{simpleDiagnostic.Code}:{simpleDiagnostic.Message}"))
            {
                continue;
            }

            diagnostics.Add(simpleDiagnostic);
        }

        return new ParsedOutput(diagnostics, projectCount, elapsed, declaredErrors, declaredWarnings);
    }

    private bool TryAddDiagnosticLine(string line, HashSet<string> seen, List<Diagnostic> diagnostics)
    {
        var diagMatch = DiagnosticPattern().Match(line);
        if (!diagMatch.Success)
        {
            return false;
        }

        var diagnostic = new Diagnostic(
            TextHelpers.ShortenPath(diagMatch.Groups["file"].Value.Trim(), RootPath),
            diagMatch.Groups["line"].Value,
            diagMatch.Groups["col"].Value,
            diagMatch.Groups["level"].Value,
            diagMatch.Groups["code"].Value,
            TextHelpers.Truncate(diagMatch.Groups["message"].Value.Trim(), MessageMaxLen));

        // Key off what gets rendered, not the raw capture. Two diagnostics differing only past the
        // truncation limit, or whose paths shorten to the same relative path, render as identical
        // lines — keyed on the raw text both survive and the reader sees the same error twice. The
        // message is in the key because the code is optional: without it, two unrelated codeless
        // diagnostics reported at the same position would collapse into one.
        if (seen.Add($"{diagnostic.File}({diagnostic.Line},{diagnostic.Col}):{diagnostic.Code}:{diagnostic.Message}"))
        {
            diagnostics.Add(diagnostic);
        }

        return true;
    }

    private static string FormatDiagnostics(List<Diagnostic> errors, List<Diagnostic> warnings, string context,
        int exitCode, int errorTotal, int warningTotal)
    {
        var sb = new StringBuilder();

        if (errors.Count == 0)
        {
            // Non-zero exit with only warnings parsed (no error line matched the regex): a bare
            // "0 errors, N warnings" header would read like a near-success for a run that FAILED.
            // Prepend an explicit failure marker so the rendered verdict stays exit-code-derived.
            if (exitCode != 0)
            {
                sb.AppendLine(CultureInfo.InvariantCulture, $"✗ dotnet build failed (exit {exitCode})");
            }

            sb.AppendLine(CultureInfo.InvariantCulture,
                    $"dotnet build: {Tally(errorTotal, errors.Count, "error")}, {Tally(warningTotal, warnings.Count, "warning")}{context}")
                .AppendLine(Separator);
            AppendGroupedByCode(sb, warnings);
        }
        else
        {
            sb.AppendLine(CultureInfo.InvariantCulture,
                    $"dotnet build: {Tally(errorTotal, errors.Count, "error")}, {Tally(warningTotal, warnings.Count, "warning")}{context}")
                .AppendLine(Separator);
            AppendGroupedByFile(sb, errors);
            AppendTopCodes(sb, errors);
            if (warnings.Count > 0)
            {
                sb.AppendLine(CultureInfo.InvariantCulture,
                    $"{Tally(warningTotal, warnings.Count, "warning")} suppressed (use --vv to see)");
            }
        }

        // Normalize to '\n' so output stays identical across platforms; AppendLine emits '\r\n' on Windows.
        return sb.ToString().ReplaceLineEndings("\n");
    }

    /// <summary>
    /// Renders a diagnostic count, annotating it with "(N shown)" when this filter can display
    /// fewer entries than the build reported — duplicates collapsed across target frameworks or
    /// projects, or shapes the regexes did not match. Without the annotation that shortfall is
    /// invisible and the header reads as an authoritative, complete count.
    /// </summary>
    /// <param name="total">The count the build reported.</param>
    /// <param name="shown">The number of distinct entries rendered below the header.</param>
    /// <param name="noun">The singular noun to render ("error" or "warning").</param>
    private static string Tally(int total, int shown, string noun)
    {
        var label = $"{total} {noun}{(total == 1 ? "" : "s")}";
        return total == shown ? label : $"{label} ({shown} shown)";
    }

    /// <summary>Renders a diagnostic code, standing in for codeless MSBuild output.</summary>
    /// <param name="code">The parsed code, empty when the diagnostic carried none.</param>
    private static string CodeLabel(string code)
    {
        return code.Length == 0 ? NoCode : code;
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
               || NoiseBuildResultPattern().IsMatch(line);
    }

    private static void AppendGroupedByCode(StringBuilder sb, List<Diagnostic> diags)
    {
        foreach (var group in diags.GroupBy(d => d.Code).OrderBy(g => g.Key))
        {
            var items = group.ToList();
            sb.AppendLine(CultureInfo.InvariantCulture, $"{CodeLabel(group.Key)} ({items.Count}x)");
            foreach (var d in items)
            {
                // A diagnostic with no file has no line either; emitting the separators anyway
                // renders ": —", which reads like a location the filter dropped.
                var location = d.File.Length == 0 ? string.Empty : $"{d.File}:{d.Line} — ";
                sb.AppendLine(CultureInfo.InvariantCulture, $"  {location}{d.Message}");
            }
        }
    }

    private static void AppendGroupedByFile(StringBuilder sb, List<Diagnostic> errors)
    {
        foreach (var group in errors.GroupBy(d => d.File).OrderByDescending(g => g.Count()))
        {
            var items = group.ToList();
            var file = group.Key.Length == 0 ? NoFile : group.Key;
            sb.AppendLine(CultureInfo.InvariantCulture,
                $"{file} ({items.Count} error{(items.Count == 1 ? "" : "s")})");
            foreach (var d in items)
            {
                // Tool-level diagnostics (MSBUILD, CSC) carry neither position nor, sometimes, a
                // code; both are omitted rather than rendered as the empty "(,)" and ": " stubs.
                var position = d.Line.Length == 0 ? string.Empty : $"({d.Line},{d.Col}) ";
                var code = d.Code.Length == 0 ? string.Empty : $"{d.Code}: ";
                sb.AppendLine(CultureInfo.InvariantCulture, $"  {position}{code}{d.Message}");
            }
        }
    }

    private static void AppendTopCodes(StringBuilder sb, List<Diagnostic> errors)
    {
        var topCodes = errors
            .GroupBy(d => d.Code)
            .OrderByDescending(g => g.Count())
            .Take(5)
            .Select(g => $"{CodeLabel(g.Key)} ({g.Count()}x)")
            .ToList();

        if (topCodes.Count > 0)
        {
            sb.AppendLine(CultureInfo.InvariantCulture, $"Top codes: {string.Join(", ", topCodes)}");
        }
    }

    // Matches: /path/file.cs(10,5): error CS0001: message [project.csproj]
    // - file matches lazily up to the (line,col) anchor so paths containing parens (e.g. "Program Files (x86)") survive
    // - code allows lower-case prefixes (xUnit1013, IDE0055) in addition to CS/MSB, and is optional
    //   because MSBuild tasks may report "error : message" with no code at all
    // - message captures the full text and only the trailing "[project]" suffix is peeled off at end-of-line,
    //   so messages containing brackets (e.g. "'string[]'") are kept intact
    [GeneratedRegex(
        @"^\s*(?<file>.+?)\((?<line>\d+),(?<col>\d+)\):\s+(?<level>error|warning)\s+(?:(?<code>[A-Za-z]+\d+)\s*)?:\s*(?<message>.+?)(?:\s+\[(?<project>[^\]]+)\])?\s*$")]
    private static partial Regex DiagnosticPattern();

    // Matches: "MSBUILD : error MSB1001: message" or "CSC : error CS2012: message" (no file/line/col).
    // The code is optional: Exec tasks emit "EXEC : warning : could not lock config file", and
    // requiring a code dropped those from the listing and the count alike.
    [GeneratedRegex(
        @"^\s*\S*\s*:\s*(?<level>error|warning)\s+(?:(?<code>[A-Za-z]+\d+)\s*)?:\s*(?<message>.+?)(?:\s*\[.+?\])?\s*$")]
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

    // MSBuild's end-of-build tally: "    0 Warning(s)" and "    3 Error(s)". Not noise — it is the
    // only authoritative diagnostic count in the output, so it is captured rather than discarded.
    [GeneratedRegex(@"^\s+(?<count>\d+) (?<kind>Warning|Error)\(s\)\s*$")]
    private static partial Regex DeclaredCountPattern();

    private sealed record Diagnostic(string File, string Line, string Col, string Level, string Code, string Message);

    /// <summary>Everything a single pass over the raw output yields.</summary>
    /// <param name="Diagnostics">The distinct diagnostics parsed, in the order encountered.</param>
    /// <param name="ProjectCount">How many project output lines were seen.</param>
    /// <param name="Elapsed">The formatted build duration, empty when absent.</param>
    /// <param name="DeclaredErrors">MSBuild's own error tally, null when the output carried none.</param>
    /// <param name="DeclaredWarnings">MSBuild's own warning tally, null when the output carried none.</param>
    private sealed record ParsedOutput(
        List<Diagnostic> Diagnostics,
        int ProjectCount,
        string Elapsed,
        int? DeclaredErrors,
        int? DeclaredWarnings);
}
