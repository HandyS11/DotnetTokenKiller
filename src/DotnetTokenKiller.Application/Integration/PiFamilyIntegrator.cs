using DotnetTokenKiller.Application.Integration.Hooks;
using DotnetTokenKiller.Domain.Integration;

namespace DotnetTokenKiller.Application.Integration;

/// <summary>
/// Installs dtk for a harness of the pi family: the shared <c>AGENTS.md</c> section and skill, and a generated
/// <c>extensions/dtk.js</c> (see <see cref="PiExtension"/>).
/// </summary>
/// <param name="rtk">Detects and reconciles an rtk extension so dtk owns dotnet commands.</param>
/// <param name="home">Resolves the user's home and the harnesses' agent directories.</param>
internal abstract class PiFamilyIntegrator(RtkHookCoexistence rtk, HomePaths home)
    : IProviderIntegrator, IGlobalIntegrator, IHookIntegrator, IUninstallIntegrator
{
    /// <inheritdoc/>
    public abstract string ProviderName { get; }

    /// <summary>Gets the harness's display name, for the generated file's comments.</summary>
    protected abstract string HarnessName { get; }

    /// <summary>Gets the harness's project config folder, e.g. <c>.pi</c>.</summary>
    protected abstract string ProjectFolder { get; }

    /// <summary>Gets the payload kind the probe sends.</summary>
    protected abstract HookPayloadKind PayloadKind { get; }

    /// <summary>Gets the harness's global agent directory.</summary>
    /// <param name="paths">The home paths to resolve against.</param>
    protected abstract string GlobalAgentDirectory(HomePaths paths);

    /// <summary>Adds scope-specific notes after a successful install.</summary>
    /// <param name="scope">The scope installed.</param>
    /// <param name="context">The integration context.</param>
    protected virtual void AddNotes(HookScope scope, IntegrationContext context)
    {
    }

    /// <inheritdoc/>
    public IReadOnlyList<HookInstallation> DescribeHooks(string directory, HookScope scope)
    {
        var path = Path.Combine(ConfigDirectory(directory, scope), "extensions", "dtk.js");

        return
        [
            new HookInstallation(
                ProviderName,
                scope,
                path,
                HookCommands.Invocation(ProviderName),
                LegacyScriptPath: null,
                PayloadKind,
                PiExtension.Artifact(path, ProviderName, HarnessName))
        ];
    }

    /// <inheritdoc/>
    public Task<IntegrationResult> IntegrateAsync(string directory, bool force, CancellationToken cancellationToken)
        => IntegrateCoreAsync(directory, HookScope.Project, force, cancellationToken);

    /// <inheritdoc/>
    public Task<IntegrationResult> IntegrateGlobalAsync(bool force, CancellationToken cancellationToken)
        => IntegrateCoreAsync(home.Home, HookScope.Global, force, cancellationToken);

    /// <inheritdoc/>
    public IReadOnlyList<string> SharedArtifactPaths(string directory, HookScope scope) =>
        [InstructionsPath(directory, scope), SharedInstructionArtifacts.SkillPath(SkillsDirectory(directory, scope))];

    /// <inheritdoc/>
    public async Task<IntegrationResult> UninstallAsync(
        string directory, HookScope scope, IReadOnlyDictionary<string, string> sharedInUse, CancellationToken cancellationToken)
    {
        var hookDirectory = scope == HookScope.Global ? home.Home : directory;
        var context = IntegrationContext.ForUninstall(hookDirectory, sharedInUse);

        await SharedInstructionArtifacts.RemoveAgentsFilesAsync(
            InstructionsPath(hookDirectory, scope), SkillsDirectory(hookDirectory, scope), context, cancellationToken)
            .ConfigureAwait(false);

        await UninstallHelpers.RemoveGeneratedFileAsync(DescribeHooks(hookDirectory, scope)[0].PluginArtifact!, context, cancellationToken)
            .ConfigureAwait(false);

        rtk.NoteRemainingExclusion(context, RtkHookCoexistence.IsRtkRewriteReferencedIn(RtkCandidates(hookDirectory)));

        return context.ToResult();
    }

    private async Task<IntegrationResult> IntegrateCoreAsync(
        string directory, HookScope scope, bool force, CancellationToken cancellationToken)
    {
        var context = new IntegrationContext(force);

        await SharedInstructionArtifacts.WriteAgentsFilesAsync(
            InstructionsPath(directory, scope), SkillsDirectory(directory, scope), context, cancellationToken)
            .ConfigureAwait(false);

        await IntegratorHelpers.WriteGeneratedFileAsync(DescribeHooks(directory, scope)[0].PluginArtifact!, context, cancellationToken)
            .ConfigureAwait(false);

        var rtkOutcome = await rtk.ReconcileFilesAsync(RtkCandidates(directory), cancellationToken).ConfigureAwait(false);
        rtkOutcome.ApplyTo(context);

        AddNotes(scope, context);

        return context.ToResult();
    }

    private string ConfigDirectory(string directory, HookScope scope) =>
        scope == HookScope.Global ? GlobalAgentDirectory(home) : Path.Combine(directory, ProjectFolder);

    private string InstructionsPath(string directory, HookScope scope) =>
        scope == HookScope.Global ? Path.Combine(GlobalAgentDirectory(home), "AGENTS.md") : Path.Combine(directory, "AGENTS.md");

    private string SkillsDirectory(string directory, HookScope scope) =>
        scope == HookScope.Global ? home.AgentsSkillsDir : Path.Combine(directory, ".agents", "skills");

    /// <summary>
    /// Every file either pi-family harness would load as an extension, in both scopes: rtk installs its pi extension
    /// in pi's folders, and a user may copy it into oh-my-pi's.
    /// </summary>
    /// <param name="directory">The project root.</param>
    private List<string> RtkCandidates(string directory) =>
    [
        .. new[]
            {
                Path.Combine(directory, ".pi", "extensions"),
                Path.Combine(home.PiAgentDir, "extensions"),
                Path.Combine(directory, ".omp", "extensions"),
                Path.Combine(home.OhMyPiAgentDir, "extensions")
            }
            .Where(Directory.Exists)
            .SelectMany(folder => IntegratorHelpers.EnumerateSafely(folder, Directory.EnumerateFiles))
    ];
}
