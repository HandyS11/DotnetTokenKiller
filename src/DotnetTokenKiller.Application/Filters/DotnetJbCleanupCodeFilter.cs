using DotnetTokenKiller.Domain.Filters;

namespace DotnetTokenKiller.Application.Filters;

/// <summary>Condenses <c>dotnet jb cleanupcode</c> output to the files it changed.</summary>
/// <remarks>
/// Its parsing is not written yet: it returns nothing, so the pipeline's raw-tail fallback shows the output.
/// </remarks>
/// <param name="rootPath">Optional root path used to shorten file paths in the output.</param>
public sealed class DotnetJbCleanupCodeFilter(string? rootPath = null) : IContextualOutputFilter
{
    /// <summary>The root that paths are shown relative to: the given one, else the working directory.</summary>
    internal string RootPath => rootPath ?? Environment.CurrentDirectory;

    /// <summary>Applies the filter to the cleanupcode output, with nothing known about the run.</summary>
    /// <param name="strippedOutput">The cleanupcode output to filter, with ANSI escapes already stripped.</param>
    /// <param name="exitCode">The process exit code; the sole source of truth for the success/failure verdict.</param>
    public string Apply(string strippedOutput, int exitCode) =>
        Apply(strippedOutput, exitCode, FilterContext.Unknown).Output;

    /// <summary>Applies the filter to the cleanupcode output.</summary>
    /// <param name="strippedOutput">The cleanupcode output to filter, with ANSI escapes already stripped.</param>
    /// <param name="exitCode">The process exit code; the sole source of truth for the success/failure verdict.</param>
    /// <param name="context">The run's context.</param>
    public FilterResult Apply(string strippedOutput, int exitCode, FilterContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return new FilterResult(string.Empty);
    }
}
