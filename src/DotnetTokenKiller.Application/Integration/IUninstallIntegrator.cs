using DotnetTokenKiller.Domain.Integration;

namespace DotnetTokenKiller.Application.Integration;

/// <summary>
/// Implemented by every provider integrator, to remove what its install writes
/// (<c>dtk init &lt;provider&gt; --uninstall</c>). The removal mirrors the install: dtk's entries and sections come
/// out of shared files, and files dtk generated go only when dtk can prove their content is still its own.
/// </summary>
internal interface IUninstallIntegrator
{
    /// <summary>
    /// The files this provider's install writes that another provider's install also writes — an <c>AGENTS.md</c>,
    /// <c>GEMINI.md</c> or <c>copilot-instructions.md</c> section, or the shared skill.
    /// </summary>
    /// <param name="directory">Project root; ignored when <paramref name="scope"/> is <see cref="HookScope.Global"/>.</param>
    /// <param name="scope">Which install to describe.</param>
    IReadOnlyList<string> SharedArtifactPaths(string directory, HookScope scope);

    /// <summary>Removes what this provider's install writes at the given scope.</summary>
    /// <param name="directory">Project root; ignored when <paramref name="scope"/> is <see cref="HookScope.Global"/>.</param>
    /// <param name="scope">Which install to remove.</param>
    /// <param name="sharedInUse">
    /// Shared files another installed dtk integration still uses, mapped to that provider's name; dtk's part of these
    /// is left in place.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// Deleted files in <see cref="IntegrationResult.RemovedFiles"/>, files dtk's part was removed from in
    /// <see cref="IntegrationResult.UpdatedFiles"/>, existing files holding nothing of dtk's in
    /// <see cref="IntegrationResult.UnchangedFiles"/>, and files kept because they differ from what dtk wrote in
    /// <see cref="IntegrationResult.SkippedFiles"/>, each explained by a note.
    /// </returns>
    Task<IntegrationResult> UninstallAsync(
        string directory,
        HookScope scope,
        IReadOnlyDictionary<string, string> sharedInUse,
        CancellationToken cancellationToken);
}
