using DotnetTokenKiller.Application.Integration.Hooks;
using DotnetTokenKiller.Domain.Integration;

namespace DotnetTokenKiller.Application.Integration;

/// <summary>Installs dtk integration artifacts for Crush.</summary>
/// <param name="home">Resolves the user's home and Crush config directories for global integration.</param>
/// <remarks>
/// Creates, in a project: the shared <c>AGENTS.md</c> section and <c>.agents/skills</c> skill, and dtk's section in
/// <c>.crushrc</c> (or in an existing <c>crushrc</c> when the project has no <c>.crushrc</c>) registering
/// <c>dtk hook crush</c> for the <c>bash</c> tool. Globally: dtk's section in <c>crushrc</c> and in <c>CRUSH.md</c>
/// under <see cref="HomePaths.CrushConfigDir"/>, and the skill in <c>~/.agents/skills</c>.
/// Internal for the same reason as <see cref="ClaudeCodeIntegrator"/>: its constructor takes internal types.
/// </remarks>
internal sealed class CrushIntegrator(HomePaths home)
    : IProviderIntegrator, IGlobalIntegrator, IHookIntegrator, IUninstallIntegrator
{
    /// <summary>The first Crush release that reads <c>crushrc</c> with <c>hook add</c>; pinned by gate C.</summary>
    internal const string MinimumCrushVersion = "0.88.0";

    /// <summary>Printed when this run wrote the hook: older Crush releases do not read <c>crushrc</c>.</summary>
    internal const string VersionNote =
        "The hook is registered in crushrc, which Crush reads from version " + MinimumCrushVersion + " on; update Crush if it is older.";

    /// <summary>Printed when this run wrote the hook.</summary>
    internal const string SubagentNote =
        "Crush runs hooks for the main agent's tool calls only, so a sub-agent's dotnet commands run as written.";

    /// <inheritdoc/>
    public string ProviderName => "crush";

    /// <inheritdoc/>
    public IReadOnlyList<HookInstallation> DescribeHooks(string directory, HookScope scope) =>
    [
        new HookInstallation(
            ProviderName,
            scope,
            RcPath(directory, scope),
            HookCommands.Invocation(ProviderName),
            LegacyScriptPath: null,
            HookPayloadKind.Crush) { IsScriptRegistration = true }
    ];

    /// <inheritdoc/>
    public async Task<IntegrationResult> IntegrateAsync(string directory, bool force, CancellationToken cancellationToken)
    {
        var context = new IntegrationContext(force);

        await SharedInstructionArtifacts.WriteAgentsFilesAsync(
            Path.Combine(directory, "AGENTS.md"), Path.Combine(directory, ".agents", "skills"), context, cancellationToken)
            .ConfigureAwait(false);
        await WriteHookAsync(RcPath(directory, HookScope.Project), context, cancellationToken).ConfigureAwait(false);

        return context.ToResult();
    }

    /// <inheritdoc/>
    public async Task<IntegrationResult> IntegrateGlobalAsync(bool force, CancellationToken cancellationToken)
    {
        var context = new IntegrationContext(force);

        await IntegratorHelpers.WriteSectionBasedFileAsync(
            GlobalInstructionsPath, SharedInstructionArtifacts.SectionMarker, SharedInstructionArtifacts.SectionEndMarker,
            SharedInstructionArtifacts.Section, context, cancellationToken).ConfigureAwait(false);
        await IntegratorHelpers.WriteGeneratedFileAsync(
            SharedInstructionArtifacts.SkillArtifact(home.AgentsSkillsDir), context, cancellationToken).ConfigureAwait(false);
        await WriteHookAsync(RcPath(home.Home, HookScope.Global), context, cancellationToken).ConfigureAwait(false);

        return context.ToResult();
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> SharedArtifactPaths(string directory, HookScope scope) =>
        scope == HookScope.Global
            ? [SharedInstructionArtifacts.SkillPath(home.AgentsSkillsDir)]
            : [Path.Combine(directory, "AGENTS.md"), SharedInstructionArtifacts.SkillPath(Path.Combine(directory, ".agents", "skills"))];

    /// <inheritdoc/>
    public async Task<IntegrationResult> UninstallAsync(
        string directory, HookScope scope, IReadOnlyDictionary<string, string> sharedInUse, CancellationToken cancellationToken)
    {
        var root = scope == HookScope.Global ? home.Home : directory;
        var context = IntegrationContext.ForUninstall(root, sharedInUse);

        if (scope == HookScope.Global)
        {
            await UninstallHelpers.RemoveSectionAsync(
                GlobalInstructionsPath, SharedInstructionArtifacts.SectionMarker, SharedInstructionArtifacts.SectionEndMarker,
                context, cancellationToken).ConfigureAwait(false);
            await UninstallHelpers.RemoveGeneratedFileAsync(
                SharedInstructionArtifacts.SkillArtifact(home.AgentsSkillsDir), context, cancellationToken).ConfigureAwait(false);
            await CrushrcFile.RemoveAsync(RcPath(root, scope), context, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await SharedInstructionArtifacts.RemoveAgentsFilesAsync(
                Path.Combine(directory, "AGENTS.md"), Path.Combine(directory, ".agents", "skills"), context, cancellationToken)
                .ConfigureAwait(false);
            await CrushrcFile.RemoveAsync(Path.Combine(directory, ".crushrc"), context, cancellationToken).ConfigureAwait(false);
            await CrushrcFile.RemoveAsync(Path.Combine(directory, "crushrc"), context, cancellationToken).ConfigureAwait(false);
        }

        return context.ToResult();
    }

    private string GlobalInstructionsPath => Path.Combine(home.CrushConfigDir, "CRUSH.md");

    /// <summary>
    /// The <c>crushrc</c> dtk registers in: globally <c>crushrc</c> in Crush's config directory; in a project,
    /// <c>.crushrc</c>, unless the project already has a <c>crushrc</c> and no <c>.crushrc</c>.
    /// </summary>
    /// <param name="directory">The project root; ignored when <paramref name="scope"/> is <see cref="HookScope.Global"/>.</param>
    /// <param name="scope">Which scope to resolve the path for.</param>
    private string RcPath(string directory, HookScope scope)
    {
        if (scope == HookScope.Global)
        {
            return Path.Combine(home.CrushConfigDir, "crushrc");
        }

        var dotRc = Path.Combine(directory, ".crushrc");
        var plainRc = Path.Combine(directory, "crushrc");
        return !File.Exists(dotRc) && File.Exists(plainRc) ? plainRc : dotRc;
    }

    private static async Task WriteHookAsync(string rcPath, IntegrationContext context, CancellationToken cancellationToken)
    {
        await CrushrcFile.WriteAsync(rcPath, HookCommands.Invocation("crush"), context, cancellationToken).ConfigureAwait(false);

        if (context.Created.Contains(rcPath) || context.Updated.Contains(rcPath))
        {
            context.Notes.Add(VersionNote);
            context.Notes.Add(SubagentNote);
        }
    }
}
