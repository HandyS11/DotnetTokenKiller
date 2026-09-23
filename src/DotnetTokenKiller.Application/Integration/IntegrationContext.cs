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

    /// <summary>Creates the context for one <c>dtk init --uninstall</c> run.</summary>
    /// <param name="pruneBoundary">
    /// The project directory, or the home directory for a global run: directories an uninstall empties are removed up
    /// to, but never including, this one.
    /// </param>
    /// <param name="sharedInUse">
    /// Shared files another installed dtk integration still uses, mapped to that provider's name; see
    /// <see cref="SharedInUse"/>.
    /// </param>
    internal static IntegrationContext ForUninstall(string pruneBoundary, IReadOnlyDictionary<string, string> sharedInUse) =>
        new(force: false) { PruneBoundary = pruneBoundary, SharedInUse = sharedInUse };

    /// <summary>Gets a value indicating whether existing files should be overwritten.</summary>
    internal bool Force { get; }

    /// <summary>
    /// Gets the directory up to which (exclusive) an uninstall removes the directories it empties, or
    /// <see langword="null"/> for an install, which removes none.
    /// </summary>
    internal string? PruneBoundary { get; private init; }

    /// <summary>
    /// Gets the shared files (an <c>AGENTS.md</c>, <c>GEMINI.md</c> or skill) that another dtk integration still
    /// installed in the same scope also writes, mapped to that provider's name. An uninstall leaves dtk's part of
    /// these in place.
    /// </summary>
    internal IReadOnlyDictionary<string, string> SharedInUse { get; private init; } = new Dictionary<string, string>();

    /// <summary>Gets the list of file paths created during this integration run.</summary>
    internal List<string> Created { get; } = [];

    /// <summary>Gets the list of file paths updated during this integration run.</summary>
    internal List<string> Updated { get; } = [];

    /// <summary>
    /// Gets the list of file paths skipped because they already existed and <see cref="Force"/> is
    /// <see langword="false"/>; for an uninstall, the files kept because dtk cannot prove their content is its own.
    /// </summary>
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

    /// <summary>Gets the list of file paths deleted during this integration run.</summary>
    internal List<string> Removed { get; } = [];

    /// <summary>Builds an <see cref="IntegrationResult"/> from the accumulated lists.</summary>
    internal IntegrationResult ToResult()
    {
        return new IntegrationResult(Created, Updated, Skipped, Notes)
        {
            UnchangedFiles = Unchanged,
            RemovedFiles = Removed
        };
    }
}
