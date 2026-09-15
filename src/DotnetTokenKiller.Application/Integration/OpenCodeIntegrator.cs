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
    : IProviderIntegrator, IGlobalIntegrator, IHookIntegrator
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
            Path.Combine(directory, "AGENTS.md"), Path.Combine(directory, ".agents", "skills"), directory, HookScope.Project,
            force, cancellationToken);

    /// <inheritdoc/>
    public Task<IntegrationResult> IntegrateGlobalAsync(bool force, CancellationToken cancellationToken)
        => IntegrateCoreAsync(
            Path.Combine(home.OpenCodeConfigDir, "AGENTS.md"), home.AgentsSkillsDir, home.Home, HookScope.Global,
            force, cancellationToken);

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
