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
internal sealed class KiloIntegrator(HomePaths home) : AgentsFileIntegrator(home)
{
    /// <summary>
    /// Printed when this run creates or updates the plugin file: Kilo Code only reads plugins at startup.
    /// </summary>
    internal const string ReloadNote =
        "Kilo Code loads plugins when it starts: restart Kilo (or the VS Code extension) for the rewrite to take effect.";

    /// <inheritdoc/>
    public override string ProviderName => "kilo";

    /// <inheritdoc/>
    protected override string GlobalInstructionsDirectory => Home.KiloConfigDir;

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
                HookPayloadKind.Kilo,
                KiloPlugin.Artifact(path))
        ];
    }

    /// <inheritdoc/>
    protected override async Task InstallHookAsync(
        string directory, HookScope scope, IntegrationContext context, CancellationToken cancellationToken)
    {
        var pluginPath = PluginPath(directory, scope);
        await IntegratorHelpers.WriteGeneratedFileAsync(KiloPlugin.Artifact(pluginPath), context, cancellationToken)
            .ConfigureAwait(false);

        if (Wrote(context, pluginPath))
        {
            context.Notes.Add(ReloadNote);
        }
    }

    /// <inheritdoc/>
    protected override Task UninstallHookAsync(
        string directory, HookScope scope, IntegrationContext context, CancellationToken cancellationToken) =>
        UninstallHelpers.RemoveGeneratedFileAsync(KiloPlugin.Artifact(PluginPath(directory, scope)), context, cancellationToken);

    /// <summary>Kilo Code's plugin folder, singular in both scopes.</summary>
    /// <param name="directory">The project root; ignored when <paramref name="scope"/> is <see cref="HookScope.Global"/>.</param>
    /// <param name="scope">Which install to resolve.</param>
    private string PluginPath(string directory, HookScope scope) =>
        Path.Combine(
            scope == HookScope.Global ? Home.KiloConfigDir : Path.Combine(directory, ".kilo"), "plugin", "dtk.js");
}
