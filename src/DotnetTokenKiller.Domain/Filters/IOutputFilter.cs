namespace DotnetTokenKiller.Domain.Filters;

/// <summary>Filters and condenses command output.</summary>
public interface IOutputFilter
{
    /// <summary>Applies the filter to the command output and returns the condensed result.</summary>
    /// <remarks>
    /// The pipeline strips ANSI escape sequences exactly once, before any filter runs. Callers
    /// must pass output that has already been stripped (see <c>AnsiStrip.Strip</c> in
    /// <c>DotnetTokenKiller.Domain.Text</c>); implementations must not strip again.
    /// </remarks>
    /// <param name="strippedOutput">The command output to filter, with ANSI escape sequences already stripped.</param>
    /// <param name="exitCode">The process exit code; the sole source of truth for the success/failure verdict.</param>
    string Apply(string strippedOutput, int exitCode);
}
