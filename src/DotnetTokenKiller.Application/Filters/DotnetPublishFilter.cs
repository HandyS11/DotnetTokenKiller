using System.Text.RegularExpressions;
using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Domain.Filters;

namespace DotnetTokenKiller.Application.Filters;

/// <summary>
/// Condenses dotnet publish output to the build's error/warning summary plus, on success, each
/// project's publish directory.
/// </summary>
/// <param name="rootPath">Optional root path used to shorten file paths in diagnostics and publish directories.</param>
public sealed partial class DotnetPublishFilter(string? rootPath = null) : IContextualOutputFilter
{
    private string RootPath => rootPath ?? Environment.CurrentDirectory;

    /// <summary>Applies the filter to the publish output, with nothing known about the run.</summary>
    /// <param name="strippedOutput">The publish output to filter, with ANSI escape sequences already stripped.</param>
    /// <param name="exitCode">The process exit code; the sole source of truth for the success/failure verdict.</param>
    public string Apply(string strippedOutput, int exitCode) =>
        Apply(strippedOutput, exitCode, FilterContext.Unknown).Output;

    /// <summary>
    /// Applies the filter to the publish output. When the run knows which files the work in progress changed, a
    /// publish without errors lists only their warnings and counts the others.
    /// </summary>
    /// <param name="strippedOutput">The publish output to filter, with ANSI escape sequences already stripped.</param>
    /// <param name="exitCode">The process exit code; the sole source of truth for the success/failure verdict.</param>
    /// <param name="context">The run's context.</param>
    public FilterResult Apply(string strippedOutput, int exitCode, FilterContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (string.IsNullOrEmpty(strippedOutput))
        {
            return new FilterResult(string.Empty);
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

        return new FilterResult(
            report.Render("dotnet publish", exitCode, publishDirectories, report.ChangesFor(context)));
    }

    // Matches the Publish target's "  MyApp -> /path/bin/Release/net10.0/publish/" line. The SDK
    // prints the directory with its trailing separator, which is what tells it apart from the
    // build's "  MyApp -> /path/MyApp.dll" line printed just before it.
    [GeneratedRegex(@"^\s+(?<project>\S+) -> (?<directory>.+[/\\])\s*$")]
    private static partial Regex PublishDirectoryPattern();
}
