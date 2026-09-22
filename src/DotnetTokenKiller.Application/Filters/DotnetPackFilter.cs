using System.Text.RegularExpressions;
using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Domain.Filters;

namespace DotnetTokenKiller.Application.Filters;

/// <summary>
/// Condenses dotnet pack output to the build's error/warning summary plus, on success, each
/// package it created.
/// </summary>
/// <param name="rootPath">Optional root path used to shorten file paths in diagnostics and package paths.</param>
public sealed partial class DotnetPackFilter(string? rootPath = null) : IOutputFilter
{
    private string RootPath => rootPath ?? Environment.CurrentDirectory;

    /// <summary>Applies the filter to the pack output.</summary>
    /// <param name="strippedOutput">The pack output to filter, with ANSI escape sequences already stripped.</param>
    /// <param name="exitCode">The process exit code; the sole source of truth for the success/failure verdict.</param>
    public string Apply(string strippedOutput, int exitCode)
    {
        if (string.IsNullOrEmpty(strippedOutput))
        {
            return string.Empty;
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

        return report.Render("dotnet pack", exitCode, packages);
    }

    // Matches "  Successfully created package '/path/bin/Release/MyLib.1.0.0.nupkg'." (and .snupkg).
    [GeneratedRegex(@"^\s*Successfully created package '(?<package>.+)'\.?\s*$")]
    private static partial Regex CreatedPackagePattern();
}
