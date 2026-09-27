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
internal sealed class AmpIntegrator(HomePaths home) : AgentsFileIntegrator(home)
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
    public override string ProviderName => "amp";

    /// <inheritdoc/>
    protected override string GlobalInstructionsDirectory => Home.AmpConfigDir;

    /// <inheritdoc/>
    public override IReadOnlyList<HookInstallation> DescribeHooks(string directory, HookScope scope)
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
    protected override async Task InstallHookAsync(
        string directory, HookScope scope, IntegrationContext context, CancellationToken cancellationToken)
    {
        var pluginPath = PluginPath(directory, scope);
        await IntegratorHelpers.WriteGeneratedFileAsync(AmpPlugin.Artifact(pluginPath), context, cancellationToken)
            .ConfigureAwait(false);

        if (Wrote(context, pluginPath))
        {
            context.Notes.Add(ReloadNote);
            if (scope == HookScope.Project)
            {
                context.Notes.Add(ProjectPluginNote);
            }
        }
    }

    /// <inheritdoc/>
    protected override Task UninstallHookAsync(
        string directory, HookScope scope, IntegrationContext context, CancellationToken cancellationToken) =>
        UninstallHelpers.RemoveGeneratedFileAsync(AmpPlugin.Artifact(PluginPath(directory, scope)), context, cancellationToken);

    /// <summary>Amp's plugin folder, plural in both scopes.</summary>
    /// <param name="directory">The project root; ignored when <paramref name="scope"/> is <see cref="HookScope.Global"/>.</param>
    /// <param name="scope">Which install to resolve.</param>
    private string PluginPath(string directory, HookScope scope) =>
        Path.Combine(
            scope == HookScope.Global ? Home.AmpConfigDir : Path.Combine(directory, ".amp"), "plugins", "dtk.js");
}
