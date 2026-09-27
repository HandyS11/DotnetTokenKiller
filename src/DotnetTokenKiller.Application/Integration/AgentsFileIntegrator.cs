using DotnetTokenKiller.Domain.Integration;

namespace DotnetTokenKiller.Application.Integration;

/// <summary>
/// Base for a harness that reads the shared <c>AGENTS.md</c> section and <c>.agents/skills</c> skill: installs and
/// removes those in either scope, and leaves the harness's own hook to <see cref="InstallHookAsync"/> and
/// <see cref="UninstallHookAsync"/>.
/// </summary>
/// <param name="home">Resolves the user's home and the harness's config directories.</param>
internal abstract class AgentsFileIntegrator(HomePaths home)
    : IProviderIntegrator, IGlobalIntegrator, IHookIntegrator, IUninstallIntegrator
{
    /// <inheritdoc/>
    public abstract string ProviderName { get; }

    /// <summary>Gets the user's home and the harnesses' config directories.</summary>
    protected HomePaths Home => home;

    /// <summary>Gets the directory holding the global <c>AGENTS.md</c>.</summary>
    protected abstract string GlobalInstructionsDirectory { get; }

    /// <inheritdoc/>
    public abstract IReadOnlyList<HookInstallation> DescribeHooks(string directory, HookScope scope);

    /// <inheritdoc/>
    public Task<IntegrationResult> IntegrateAsync(string directory, bool force, CancellationToken cancellationToken)
        => IntegrateCoreAsync(directory, HookScope.Project, force, cancellationToken);

    /// <inheritdoc/>
    public virtual Task<IntegrationResult> IntegrateGlobalAsync(bool force, CancellationToken cancellationToken)
        => IntegrateCoreAsync(home.Home, HookScope.Global, force, cancellationToken);

    /// <inheritdoc/>
    public IReadOnlyList<string> SharedArtifactPaths(string directory, HookScope scope) =>
        [InstructionsPath(directory, scope), SharedInstructionArtifacts.SkillPath(SkillsDirectory(directory, scope))];

    /// <inheritdoc/>
    public virtual async Task<IntegrationResult> UninstallAsync(
        string directory, HookScope scope, IReadOnlyDictionary<string, string> sharedInUse, CancellationToken cancellationToken)
    {
        var hookDirectory = scope == HookScope.Global ? home.Home : directory;
        var context = IntegrationContext.ForUninstall(hookDirectory, sharedInUse);

        await SharedInstructionArtifacts.RemoveAgentsFilesAsync(
            InstructionsPath(hookDirectory, scope), SkillsDirectory(hookDirectory, scope), context, cancellationToken)
            .ConfigureAwait(false);

        await UninstallHookAsync(hookDirectory, scope, context, cancellationToken).ConfigureAwait(false);

        return context.ToResult();
    }

    /// <summary>Writes the harness's hook, after the shared instruction files.</summary>
    /// <param name="directory">The project root, or the home directory for a global run.</param>
    /// <param name="scope">The scope installed.</param>
    /// <param name="context">The install context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    protected abstract Task InstallHookAsync(
        string directory, HookScope scope, IntegrationContext context, CancellationToken cancellationToken);

    /// <summary>Removes the harness's hook, after the shared instruction files.</summary>
    /// <param name="directory">The project root, or the home directory for a global run.</param>
    /// <param name="scope">The scope uninstalled.</param>
    /// <param name="context">The uninstall context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    protected abstract Task UninstallHookAsync(
        string directory, HookScope scope, IntegrationContext context, CancellationToken cancellationToken);

    /// <summary>The instruction file holding dtk's section: <c>AGENTS.md</c> in the project or the global directory.</summary>
    /// <param name="directory">The project root; ignored when <paramref name="scope"/> is <see cref="HookScope.Global"/>.</param>
    /// <param name="scope">Which scope to resolve.</param>
    protected virtual string InstructionsPath(string directory, HookScope scope) =>
        Path.Combine(scope == HookScope.Global ? GlobalInstructionsDirectory : directory, "AGENTS.md");

    /// <summary>The skills directory: <c>.agents/skills</c> in the project, or <see cref="HomePaths.AgentsSkillsDir"/>.</summary>
    /// <param name="directory">The project root; ignored when <paramref name="scope"/> is <see cref="HookScope.Global"/>.</param>
    /// <param name="scope">Which scope to resolve.</param>
    protected virtual string SkillsDirectory(string directory, HookScope scope) =>
        scope == HookScope.Global ? home.AgentsSkillsDir : Path.Combine(directory, ".agents", "skills");

    /// <summary>Whether this run created or updated <paramref name="path"/>.</summary>
    /// <param name="context">The install context.</param>
    /// <param name="path">The file to look for.</param>
    protected static bool Wrote(IntegrationContext context, string path) =>
        context.Created.Contains(path) || context.Updated.Contains(path);

    private async Task<IntegrationResult> IntegrateCoreAsync(
        string directory, HookScope scope, bool force, CancellationToken cancellationToken)
    {
        var context = new IntegrationContext(force);

        await SharedInstructionArtifacts.WriteAgentsFilesAsync(
            InstructionsPath(directory, scope), SkillsDirectory(directory, scope), context, cancellationToken)
            .ConfigureAwait(false);

        await InstallHookAsync(directory, scope, context, cancellationToken).ConfigureAwait(false);

        return context.ToResult();
    }
}
