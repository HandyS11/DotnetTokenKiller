using System.Text.RegularExpressions;
using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Domain.Filters;

namespace DotnetTokenKiller.Application.Filters;

/// <summary>
/// Condenses dotnet pack output to the build's error/warning summary plus, on success, each
/// package it created.
/// </summary>
/// <param name="rootPath">Optional root path used to shorten file paths in diagnostics and package paths.</param>
public sealed partial class DotnetPackFilter(string? rootPath = null) : IContextualOutputFilter
{
    private string RootPath => rootPath ?? Environment.CurrentDirectory;

    /// <summary>Applies the filter to the pack output, with nothing known about the run.</summary>
    /// <param name="strippedOutput">The pack output to filter, with ANSI escape sequences already stripped.</param>
    /// <param name="exitCode">The process exit code; the sole source of truth for the success/failure verdict.</param>
    public string Apply(string strippedOutput, int exitCode) =>
        Apply(strippedOutput, exitCode, FilterContext.Unknown).Output;

    /// <summary>
    /// Applies the filter to the pack output. When the run knows which files the work in progress changed, a
    /// pack without errors lists only their warnings and counts the others.
    /// </summary>
    /// <param name="strippedOutput">The pack output to filter, with ANSI escape sequences already stripped.</param>
    /// <param name="exitCode">The process exit code; the sole source of truth for the success/failure verdict.</param>
    /// <param name="context">The run's context.</param>
    public FilterResult Apply(string strippedOutput, int exitCode, FilterContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (string.IsNullOrEmpty(strippedOutput))
        {
            return new FilterResult(string.Empty);
        }

        var packages = new List<string>();
        var report = MsBuildDiagnosticReport.Parse(strippedOutput, RootPath, line =>
        {
            var match = CreatedPackagePattern().Match(line);
            if (match.Success)
            {
                packages.Add(TextHelpers.ShortenPath(match.Groups["package"].Value, RootPath));
            }

            return match.Success;
        });

        return new FilterResult(report.Render("dotnet pack", exitCode, packages, report.ChangesFor(context)));
    }

    // Matches "  Successfully created package '/path/bin/Release/MyLib.1.0.0.nupkg'." (and .snupkg).
    [GeneratedRegex(@"^\s*Successfully created package '(?<package>.+)'\.?\s*$")]
    private static partial Regex CreatedPackagePattern();
}
