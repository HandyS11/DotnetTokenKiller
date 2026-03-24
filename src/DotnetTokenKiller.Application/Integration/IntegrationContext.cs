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

    /// <summary>Builds an <see cref="IntegrationResult"/> from the accumulated lists.</summary>
    internal IntegrationResult ToResult()
    {
        return new IntegrationResult(Created, Updated, Skipped);
    }
}
