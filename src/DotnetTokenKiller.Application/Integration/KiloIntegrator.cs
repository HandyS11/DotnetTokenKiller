using DotnetTokenKiller.Application.Integration.Hooks;
using DotnetTokenKiller.Domain.Integration;

namespace DotnetTokenKiller.Application.Integration;

/// <summary>Installs dtk integration artifacts for Kilo Code.</summary>
/// <param name="home">Resolves the user's home and Kilo Code config directories for global integration.</param>
/// <remarks>
/// Creates:
/// <list type="bullet">
///   <item><description><c>AGENTS.md</c> (section-based merge) and <c>.agents/skills/dotnet-token-killer/SKILL.md</c></description></item>
///   <item><description><c>.kilo/plugin/dtk.js</c> (project) or <c>&lt;Kilo config dir&gt;/plugin/dtk.js</c> (global),
///   the generated plugin (see <see cref="KiloPlugin"/>)</description></item>
/// </list>
/// Internal for the same reason as <see cref="ClaudeCodeIntegrator"/>: its constructor takes internal types.
/// </remarks>
internal sealed class KiloIntegrator(HomePaths home)
    : IProviderIntegrator, IGlobalIntegrator, IHookIntegrator, IUninstallIntegrator
{
    /// <summary>
    /// Printed when this run creates or updates the plugin file: Kilo Code only reads plugins at startup.
    /// </summary>
    internal const string ReloadNote =
        "Kilo Code loads plugins when it starts: restart Kilo (or the VS Code extension) for the rewrite to take effect.";

    /// <inheritdoc/>
    public string ProviderName => "kilo";

    /// <inheritdoc/>
    public IReadOnlyList<HookInstallation> DescribeHooks(string directory, HookScope scope)
    {
        var path = PluginPath(directory, scope);

        return
        [
            new HookInstallation(
                ProviderName,
                scope,
                path,
                HookCommands.Invocation(ProviderName),
                LegacyScriptPath: null,
                HookPayloadKind.Kilo,
                KiloPlugin.Artifact(path))
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

        return context.ToResult();
    }

    private string InstructionsPath(string directory, HookScope scope) =>
        Path.Combine(scope == HookScope.Global ? home.KiloConfigDir : directory, "AGENTS.md");

    private string SkillsDirectory(string directory, HookScope scope) =>
        scope == HookScope.Global ? home.AgentsSkillsDir : Path.Combine(directory, ".agents", "skills");

    /// <summary>Kilo Code's plugin folder, singular in both scopes.</summary>
    /// <param name="directory">The project root; ignored when <paramref name="scope"/> is <see cref="HookScope.Global"/>.</param>
    /// <param name="scope">Which install to resolve.</param>
    private string PluginPath(string directory, HookScope scope) =>
        Path.Combine(
            scope == HookScope.Global ? home.KiloConfigDir : Path.Combine(directory, ".kilo"), "plugin", "dtk.js");

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

        var pluginPath = PluginPath(hookDirectory, scope);
        await IntegratorHelpers.WriteGeneratedFileAsync(KiloPlugin.Artifact(pluginPath), context, cancellationToken)
            .ConfigureAwait(false);

        if (context.Created.Contains(pluginPath) || context.Updated.Contains(pluginPath))
        {
            context.Notes.Add(ReloadNote);
        }

        return context.ToResult();
    }
}
