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
internal sealed class OpenCodeIntegrator(RtkHookCoexistence rtk, HomePaths home) : AgentsFileIntegrator(home)
{
    /// <summary>The folder names rtk's OpenCode plugin might use, singular or plural.</summary>
    private static readonly string[] RtkPluginFolderNames = ["plugin", "plugins"];

    /// <inheritdoc/>
    public override string ProviderName => "opencode";

    /// <inheritdoc/>
    protected override string GlobalInstructionsDirectory => Home.OpenCodeConfigDir;

    /// <inheritdoc/>
    public override IReadOnlyList<HookInstallation> DescribeHooks(string directory, HookScope scope)
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
    protected override async Task InstallHookAsync(
        string directory, HookScope scope, IntegrationContext context, CancellationToken cancellationToken)
    {
        await IntegratorHelpers.WriteGeneratedFileAsync(DescribeHooks(directory, scope)[0].PluginArtifact!, context, cancellationToken)
            .ConfigureAwait(false);

        var rtkOutcome = await rtk.ReconcileFilesAsync(RtkCandidates(directory), cancellationToken).ConfigureAwait(false);
        rtkOutcome.ApplyTo(context);
    }

    /// <inheritdoc/>
    protected override async Task UninstallHookAsync(
        string directory, HookScope scope, IntegrationContext context, CancellationToken cancellationToken)
    {
        await UninstallHelpers.RemoveGeneratedFileAsync(DescribeHooks(directory, scope)[0].PluginArtifact!, context, cancellationToken)
            .ConfigureAwait(false);

        rtk.NoteRemainingExclusion(context, RtkHookCoexistence.IsRtkRewriteReferencedIn(RtkCandidates(directory)));
    }

    private string ConfigDirectory(string directory, HookScope scope) =>
        scope == HookScope.Global ? Home.OpenCodeConfigDir : Path.Combine(directory, ".opencode");

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
