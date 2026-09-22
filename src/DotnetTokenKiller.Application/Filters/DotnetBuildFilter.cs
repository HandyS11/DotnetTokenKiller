using DotnetTokenKiller.Domain.Filters;

namespace DotnetTokenKiller.Application.Filters;

/// <summary>Condenses dotnet build output to a concise error/warning summary.</summary>
/// <param name="rootPath">Optional root path used to shorten file paths in diagnostics.</param>
public sealed class DotnetBuildFilter(string? rootPath = null) : IOutputFilter
{
    private string RootPath => rootPath ?? Environment.CurrentDirectory;

    /// <summary>Applies the filter to the build output.</summary>
    /// <param name="strippedOutput">The build output to filter, with ANSI escape sequences already stripped.</param>
    /// <param name="exitCode">The process exit code; the sole source of truth for the success/failure verdict.</param>
    public string Apply(string strippedOutput, int exitCode)
    {
        if (string.IsNullOrEmpty(strippedOutput))
        {
            return string.Empty;
        }

        return MsBuildDiagnosticReport.Parse(strippedOutput, RootPath).Render("dotnet build", exitCode, []);
    }
}
