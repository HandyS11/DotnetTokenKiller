using DotnetTokenKiller.Application.Integration.Hooks;
using DotnetTokenKiller.Domain.Integration;

namespace DotnetTokenKiller.Application.Integration;

/// <summary>Installs dtk integration artifacts for Amp.</summary>
/// <param name="home">Resolves the user's home and Amp config directories for global integration.</param>
/// <remarks>
/// Creates:
/// <list type="bullet">
///   <item><description><c>AGENTS.md</c> (section-based merge) and <c>.agents/skills/dotnet-token-killer/SKILL.md</c></description></item>
///   <item><description><c>.amp/plugins/dtk.js</c> (project) or <c>&lt;Amp config dir&gt;/plugins/dtk.js</c> (global),
///   the generated plugin (see <see cref="AmpPlugin"/>)</description></item>
/// </list>
/// Internal for the same reason as <see cref="ClaudeCodeIntegrator"/>: its constructor takes internal types.
/// </remarks>
internal sealed class AmpIntegrator(HomePaths home)
    : IProviderIntegrator, IGlobalIntegrator, IHookIntegrator, IUninstallIntegrator
{
    /// <summary>
    /// Printed when this run creates or updates the plugin file: Amp only reads plugins at startup.
    /// </summary>
    internal const string ReloadNote =
        "Amp loads plugins when it starts: run 'plugins: reload' from Amp's command palette, or restart Amp.";

    /// <summary>
    /// Printed alongside <see cref="ReloadNote"/> for a project install, since Amp trusts project plugins by
    /// default.
    /// </summary>
    internal const string ProjectPluginNote =
        "Amp runs project plugins without asking, so everyone who opens this project with Amp runs dtk's plugin "
        + "(it only rewrites dotnet commands).";

    /// <inheritdoc/>
    public string ProviderName => "amp";

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
                HookPayloadKind.Amp,
                AmpPlugin.Artifact(path))
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
        Path.Combine(scope == HookScope.Global ? home.AmpConfigDir : directory, "AGENTS.md");

    private string SkillsDirectory(string directory, HookScope scope) =>
        scope == HookScope.Global ? home.AgentsSkillsDir : Path.Combine(directory, ".agents", "skills");

    /// <summary>Amp's plugin folder, plural in both scopes.</summary>
    /// <param name="directory">The project root; ignored when <paramref name="scope"/> is <see cref="HookScope.Global"/>.</param>
    /// <param name="scope">Which install to resolve.</param>
    private string PluginPath(string directory, HookScope scope) =>
        Path.Combine(
            scope == HookScope.Global ? home.AmpConfigDir : Path.Combine(directory, ".amp"), "plugins", "dtk.js");

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
        await IntegratorHelpers.WriteGeneratedFileAsync(AmpPlugin.Artifact(pluginPath), context, cancellationToken)
            .ConfigureAwait(false);

        if (context.Created.Contains(pluginPath) || context.Updated.Contains(pluginPath))
        {
            context.Notes.Add(ReloadNote);
            if (scope == HookScope.Project)
            {
                context.Notes.Add(ProjectPluginNote);
            }
        }

        return context.ToResult();
    }
}
