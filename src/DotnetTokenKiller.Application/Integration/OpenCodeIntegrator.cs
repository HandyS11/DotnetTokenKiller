using DotnetTokenKiller.Application.Integration.Hooks;
using DotnetTokenKiller.Domain.Integration;

namespace DotnetTokenKiller.Application.Integration;

/// <summary>Installs dtk integration artifacts for OpenCode.</summary>
/// <param name="rtk">Detects and reconciles an rtk plugin so dtk owns dotnet commands.</param>
/// <param name="home">Resolves the user's home and OpenCode config directories for global integration.</param>
/// <remarks>
/// Creates:
/// <list type="bullet">
///   <item><description><c>AGENTS.md</c> (section-based merge) and <c>.agents/skills/dotnet-token-killer/SKILL.md</c></description></item>
///   <item><description><c>.opencode/plugins/dtk.js</c>, the generated plugin (see <see cref="OpenCodePlugin"/>)</description></item>
/// </list>
/// Internal for the same reason as <see cref="ClaudeCodeIntegrator"/>: its constructor takes internal types.
/// </remarks>
internal sealed class OpenCodeIntegrator(RtkHookCoexistence rtk, HomePaths home)
    : IProviderIntegrator, IGlobalIntegrator, IHookIntegrator, IUninstallIntegrator
{
    /// <inheritdoc/>
    public string ProviderName => "opencode";

    /// <inheritdoc/>
    public IReadOnlyList<HookInstallation> DescribeHooks(string directory, HookScope scope)
    {
        var path = Path.Combine(ConfigDirectory(directory, scope), "plugins", "dtk.js");

        return
        [
            new HookInstallation(
                ProviderName,
                scope,
                path,
                HookCommands.Invocation(ProviderName),
                LegacyScriptPath: null,
                HookPayloadKind.OpenCode,
                OpenCodePlugin.Artifact(path))
        ];
    }

    /// <inheritdoc/>
    public Task<IntegrationResult> IntegrateAsync(string directory, bool force, CancellationToken cancellationToken)
        => IntegrateCoreAsync(
            InstructionsPath(directory, HookScope.Project), SkillsDirectory(directory, HookScope.Project), directory,
            HookScope.Project, force, cancellationToken);

    /// <inheritdoc/>
    public Task<IntegrationResult> IntegrateGlobalAsync(bool force, CancellationToken cancellationToken)
        => IntegrateCoreAsync(
            InstructionsPath(home.Home, HookScope.Global), SkillsDirectory(home.Home, HookScope.Global), home.Home,
            HookScope.Global, force, cancellationToken);

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

    private string InstructionsPath(string directory, HookScope scope) =>
        Path.Combine(scope == HookScope.Global ? home.OpenCodeConfigDir : directory, "AGENTS.md");

    private string SkillsDirectory(string directory, HookScope scope) =>
        scope == HookScope.Global ? home.AgentsSkillsDir : Path.Combine(directory, ".agents", "skills");

    /// <summary>The folder names rtk's OpenCode plugin might use, singular or plural.</summary>
    private static readonly string[] RtkPluginFolderNames = ["plugin", "plugins"];

    private string ConfigDirectory(string directory, HookScope scope) =>
        scope == HookScope.Global ? home.OpenCodeConfigDir : Path.Combine(directory, ".opencode");

    private async Task<IntegrationResult> IntegrateCoreAsync(
        string instructionsPath,
        string skillsDirectory,
        string hookDirectory,
        HookScope scope,
        bool force,
        CancellationToken cancellationToken)
    {
        var context = new IntegrationContext(force);

        await SharedInstructionArtifacts.WriteAgentsFilesAsync(instructionsPath, skillsDirectory, context, cancellationToken)
            .ConfigureAwait(false);

        await IntegratorHelpers.WriteGeneratedFileAsync(DescribeHooks(hookDirectory, scope)[0].PluginArtifact!, context, cancellationToken)
            .ConfigureAwait(false);

        var rtkOutcome = await rtk.ReconcileFilesAsync(RtkCandidates(hookDirectory), cancellationToken).ConfigureAwait(false);
        rtkOutcome.ApplyTo(context);

        return context.ToResult();
    }

    /// <summary>Every plugin file OpenCode would load from either scope, where rtk installs its own plugin.</summary>
    /// <param name="hookDirectory">The project root.</param>
    private List<string> RtkCandidates(string hookDirectory) =>
    [
        .. new[] { HookScope.Project, HookScope.Global }
            .SelectMany(scope => RtkPluginFolderNames.Select(folder => Path.Combine(ConfigDirectory(hookDirectory, scope), folder)))
            .Where(Directory.Exists)
            .SelectMany(folder => IntegratorHelpers.EnumerateSafely(folder, Directory.EnumerateFiles))
    ];
}
