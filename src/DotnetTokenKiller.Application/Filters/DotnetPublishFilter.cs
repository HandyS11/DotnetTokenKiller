using System.Text.RegularExpressions;
using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Domain.Filters;

namespace DotnetTokenKiller.Application.Filters;

/// <summary>
/// Condenses dotnet publish output to the build's error/warning summary plus, on success, each
/// project's publish directory.
/// </summary>
/// <param name="rootPath">Optional root path used to shorten file paths in diagnostics and publish directories.</param>
public sealed partial class DotnetPublishFilter(string? rootPath = null) : IOutputFilter
{
    private string RootPath => rootPath ?? Environment.CurrentDirectory;

    /// <summary>Applies the filter to the publish output.</summary>
    /// <param name="strippedOutput">The publish output to filter, with ANSI escape sequences already stripped.</param>
    /// <param name="exitCode">The process exit code; the sole source of truth for the success/failure verdict.</param>
    public string Apply(string strippedOutput, int exitCode)
    {
        if (string.IsNullOrEmpty(strippedOutput))
        {
            return string.Empty;
        }

        var publishDirectories = new List<string>();
        var report = MsBuildDiagnosticReport.Parse(strippedOutput, RootPath, line =>
        {
            var match = PublishDirectoryPattern().Match(line);
            if (!match.Success)
            {
                return false;
            }

            var directory = TextHelpers.ShortenPath(match.Groups["directory"].Value, RootPath);
            publishDirectories.Add($"{match.Groups["project"].Value} -> {directory}");
            return true;
        });

        return report.Render("dotnet publish", exitCode, publishDirectories);
    }

    // Matches the Publish target's "  MyApp -> /path/bin/Release/net10.0/publish/" line. The SDK
    // prints the directory with its trailing separator, which is what tells it apart from the
    // build's "  MyApp -> /path/MyApp.dll" line printed just before it.
    [GeneratedRegex(@"^\s+(?<project>\S+) -> (?<directory>.+[/\\])\s*$")]
    private static partial Regex PublishDirectoryPattern();
}
