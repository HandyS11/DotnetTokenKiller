using DotnetTokenKiller.Domain.Filters;

namespace DotnetTokenKiller.Application.Filters;

/// <summary>Condenses <c>dotnet tool restore</c> output to the commands it made available, or its errors.</summary>
/// <remarks>
/// Its parsing is not written yet: it returns nothing, so the pipeline's raw-tail fallback shows the output.
/// </remarks>
/// <param name="rootPath">Optional root path used to shorten file paths in the output.</param>
public sealed class DotnetToolRestoreFilter(string? rootPath = null) : IOutputFilter
{
    /// <summary>The root that paths are shown relative to: the given one, else the working directory.</summary>
    internal string RootPath => rootPath ?? Environment.CurrentDirectory;

    /// <summary>Applies the filter to the tool restore output.</summary>
    /// <param name="strippedOutput">The tool restore output to filter, with ANSI escapes already stripped.</param>
    /// <param name="exitCode">The process exit code; the sole source of truth for the success/failure verdict.</param>
    public string Apply(string strippedOutput, int exitCode) => string.Empty;
}
