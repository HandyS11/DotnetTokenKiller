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
            if (!errors.Contains(shortened))
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

    private static string FailureSummary(int toolCount, int errorCount)
    {
        if (toolCount > 0)
        {
            return $"{errorCount} of {toolCount + errorCount} tools not restored";
        }

        return $"{errorCount} error{(errorCount == 1 ? "" : "s")}";
    }

    [GeneratedRegex(@"^Tool '[^']*' \(version '[^']*'\) was restored\. Available commands: (?<commands>.*)$")]
    private static partial Regex RestoredToolPattern();
}
