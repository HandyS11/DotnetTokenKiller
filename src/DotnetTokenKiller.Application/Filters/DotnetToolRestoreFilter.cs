using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Domain.Filters;

namespace DotnetTokenKiller.Application.Filters;

/// <summary>Condenses <c>dotnet tool restore</c> output to the commands it made available, or its errors.</summary>
/// <remarks>
/// Success is one line naming the restored tools' commands. Failure keeps each distinct error line once, then a
/// status line. Output the filter cannot read (a localized SDK) degrades to blank, so the pipeline's raw-tail
/// fallback shows it.
/// </remarks>
/// <param name="rootPath">Optional root path used to shorten file paths in the output.</param>
public sealed partial class DotnetToolRestoreFilter(string? rootPath = null) : IOutputFilter
{
    /// <summary>The root that paths are shown relative to: the given one, else the working directory.</summary>
    internal string RootPath => rootPath ?? Environment.CurrentDirectory;

    /// <summary>Applies the filter to the tool restore output.</summary>
    /// <param name="strippedOutput">The tool restore output to filter, with ANSI escapes already stripped.</param>
    /// <param name="exitCode">The process exit code; the sole source of truth for the success/failure verdict.</param>
    public string Apply(string strippedOutput, int exitCode)
    {
        if (string.IsNullOrWhiteSpace(strippedOutput))
        {
            return string.Empty;
        }

        var commands = new List<string>();
        var toolCount = 0;
        var errors = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var rawLine in strippedOutput.Split('\n'))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith("Restore was successful", StringComparison.Ordinal))
            {
                continue;
            }

            var restored = RestoredToolPattern().Match(line);
            if (restored.Success)
            {
                toolCount++;
                commands.AddRange(restored.Groups["commands"].Value
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                continue;
            }

            var shortened = TextHelpers.ShortenPathsInLine(line, RootPath);
            if (seen.Add(shortened))
            {
                errors.Add(shortened);
            }
        }

        return exitCode == 0 ? Success(toolCount, commands) : Failure(exitCode, toolCount, errors);
    }

    private static string Success(int toolCount, List<string> commands)
    {
        if (toolCount == 0)
        {
            return string.Empty;
        }

        return string.Create(CultureInfo.InvariantCulture,
            $"✓ dotnet tool restore: {toolCount} tool{(toolCount == 1 ? "" : "s")} ({string.Join(", ", commands)})\n");
    }

    private static string Failure(int exitCode, int toolCount, List<string> errors)
    {
        if (errors.Count == 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder();
        foreach (var error in errors)
        {
            sb.Append(error).Append('\n');
        }

        sb.Append(CultureInfo.InvariantCulture, $"✗ dotnet tool restore failed (exit {exitCode}): {FailureSummary(toolCount, errors.Count)}\n");
        return sb.ToString();
    }

    /// <summary>
    /// What a failed restore's status line counts: the distinct error lines, and the tools restored when there were
    /// any. Never a total of tools: the output does not say how many a failed restore attempted (SDK 10.0.403 stops
    /// at the first missing package), and one tool's failure can span several lines.
    /// </summary>
    /// <param name="toolCount">The tools the output reported as restored.</param>
    /// <param name="errorCount">The distinct error lines kept.</param>
    private static string FailureSummary(int toolCount, int errorCount)
    {
        var errors = Counted(errorCount, "error");
        return toolCount > 0 ? $"{errors} · {Counted(toolCount, "tool")} restored" : errors;
    }

    /// <summary><c>1 error</c>, <c>2 errors</c>: a count and its noun, plural when it is not one.</summary>
    /// <param name="count">The count.</param>
    /// <param name="noun">The singular noun.</param>
    private static string Counted(int count, string noun) =>
        string.Create(CultureInfo.InvariantCulture, $"{count} {noun}{(count == 1 ? "" : "s")}");

    [GeneratedRegex(@"^Tool '[^']*' \(version '[^']*'\) was restored\. Available commands: (?<commands>.*)$")]
    private static partial Regex RestoredToolPattern();
}
