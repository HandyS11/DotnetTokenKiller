using DotnetTokenKiller.Domain.Integration;

namespace DotnetTokenKiller.Application.Integration;

/// <summary>
/// Scope-limited mutable accumulator passed through the helper methods of a single integration run.
/// Not thread-safe; intended for single-threaded use within one <see cref="IProviderIntegrator.IntegrateAsync"/> call.
/// </summary>
internal sealed class IntegrationContext
{
    internal IntegrationContext(bool force)
    {
        Force = force;
    }

    /// <summary>Gets a value indicating whether existing files should be overwritten.</summary>
    internal bool Force { get; }

    /// <summary>Gets the list of file paths created during this integration run.</summary>
    internal List<string> Created { get; } = [];

    /// <summary>Gets the list of file paths updated during this integration run.</summary>
    internal List<string> Updated { get; } = [];

    /// <summary>Gets the list of file paths skipped because they already existed and <see cref="Force"/> is <see langword="false"/>.</summary>
    internal List<string> Skipped { get; } = [];

    /// <summary>Gets advisory messages accumulated during this integration run.</summary>
    internal List<string> Notes { get; } = [];

    /// <summary>
    /// Gets the list of file paths that needed no write during this run because the desired state
    /// was already in place: a generated file already byte-identical to the stamped current
    /// template, or a settings merge whose entry was already registered. Force-independent by
    /// nature — <see cref="Force"/> would not change anything for these paths.
    /// </summary>
    internal List<string> Unchanged { get; } = [];

    /// <summary>Builds an <see cref="IntegrationResult"/> from the accumulated lists.</summary>
    internal IntegrationResult ToResult()
    {
        return new IntegrationResult(Created, Updated, Skipped, Notes)
        {
            UnchangedFiles = Unchanged
        };
    }
}
