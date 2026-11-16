using DotnetTokenKiller.Domain.Filters;

namespace DotnetTokenKiller.Application.Filters;

/// <summary>Condenses dotnet build output to a concise error/warning summary.</summary>
/// <param name="rootPath">Optional root path used to shorten file paths in diagnostics.</param>
public sealed class DotnetBuildFilter(string? rootPath = null) : IContextualOutputFilter
{
    private string RootPath => rootPath ?? Environment.CurrentDirectory;

    /// <summary>Applies the filter to the build output, with nothing known about the run.</summary>
    /// <param name="strippedOutput">The build output to filter, with ANSI escape sequences already stripped.</param>
    /// <param name="exitCode">The process exit code; the sole source of truth for the success/failure verdict.</param>
    public string Apply(string strippedOutput, int exitCode) =>
        Apply(strippedOutput, exitCode, FilterContext.Unknown).Output;

    /// <summary>
    /// Applies the filter to the build output. When the run knows which files the work in progress changed, a
    /// build without errors lists only their warnings and counts the others.
    /// </summary>
    /// <param name="strippedOutput">The build output to filter, with ANSI escape sequences already stripped.</param>
    /// <param name="exitCode">The process exit code; the sole source of truth for the success/failure verdict.</param>
    /// <param name="context">The run's context.</param>
    public FilterResult Apply(string strippedOutput, int exitCode, FilterContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (string.IsNullOrEmpty(strippedOutput))
        {
            return new FilterResult(string.Empty);
        }

        var report = MsBuildDiagnosticReport.Parse(strippedOutput, RootPath);
        return new FilterResult(report.Render("dotnet build", exitCode, [], report.ChangesFor(context)));
    }
}
