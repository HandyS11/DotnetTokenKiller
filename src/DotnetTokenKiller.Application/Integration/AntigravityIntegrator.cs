using DotnetTokenKiller.Application.Integration.Hooks;
using DotnetTokenKiller.Domain.Integration;

namespace DotnetTokenKiller.Application.Integration;

/// <summary>Installs dtk integration artifacts for Google Antigravity CLI.</summary>
/// <param name="rtk">Detects and reconciles an rtk hook so dtk owns dotnet commands.</param>
/// <param name="home">Resolves the user's home and <c>~/.gemini</c> directories for global integration.</param>
/// <remarks>
/// Creates:
/// <list type="bullet">
///   <item><description><c>AGENTS.md</c> (section-based merge) and <c>.agents/skills/dotnet-token-killer/SKILL.md</c></description></item>
///   <item><description>
///     <c>.agents/hooks.json</c> with a <c>dtk</c> hook group registering <c>dtk hook antigravity || exit 0</c> under
///     <c>PreToolUse</c> for <c>run_command</c> (merged; other groups are left alone)
///   </description></item>
/// </list>
/// The global install writes the section into <c>~/.gemini/GEMINI.md</c>, the same file and text as
/// <c>dtk init gemini --global</c>, which Antigravity CLI reads as a global rule.
/// Internal for the same reason as <see cref="ClaudeCodeIntegrator"/>: its constructor takes internal types.
/// </remarks>
internal sealed class AntigravityIntegrator(RtkHookCoexistence rtk, HomePaths home)
    : IProviderIntegrator, IGlobalIntegrator, IHookIntegrator
{
    /// <summary>Printed when this run wrote a project hook, which Antigravity loads only in trusted workspaces.</summary>
    internal const string WorkspaceTrustNote = "Antigravity loads .agents/hooks.json only in workspaces you have trusted.";

    /// <summary>The hook group dtk owns inside a shared <c>hooks.json</c>.</summary>
    private const string GroupName = "dtk";

    private const int HookTimeoutSeconds = 10;

    /// <inheritdoc/>
    public string ProviderName => "antigravity";

    /// <inheritdoc/>
    public IReadOnlyList<HookInstallation> DescribeHooks(string directory, HookScope scope)
    {
        var configDir = scope == HookScope.Global ? home.AntigravityConfigDir : Path.Combine(directory, ".agents");

        return
        [
            new HookInstallation(
                ProviderName,
                scope,
                Path.Combine(configDir, "hooks.json"),
                HookCommands.OrExitZero(ProviderName),
                LegacyScriptPath: null,
                HookPayloadKind.AntigravityCli)
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
            Path.Combine(home.GeminiDir, "GEMINI.md"), home.AntigravitySkillsDir, home.Home, HookScope.Global,
            force, cancellationToken);

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

        var hook = DescribeHooks(hookDirectory, scope)[0];
        await IntegratorHelpers.WriteHookRegistrationAsync(
            new HookRegistrationSpec(
                hook.RegistrationPath, "PreToolUse", "run_command", hook.Command, HookTimeoutSeconds, ContainerKey: GroupName),
            context, cancellationToken).ConfigureAwait(false);

        if (scope == HookScope.Project
            && (context.Created.Contains(hook.RegistrationPath) || context.Updated.Contains(hook.RegistrationPath)))
        {
            context.Notes.Add(WorkspaceTrustNote);
        }

        var rtkOutcome = await rtk.ReconcileFilesAsync(RtkCandidates(hookDirectory), cancellationToken).ConfigureAwait(false);
        rtkOutcome.ApplyTo(context);

        return context.ToResult();
    }

    /// <summary>Where rtk registers itself for Antigravity: a hooks file, or a plugin's hooks file, in either scope.</summary>
    /// <param name="hookDirectory">The project root.</param>
    private List<string> RtkCandidates(string hookDirectory)
    {
        var configDirectories = new[] { Path.Combine(hookDirectory, ".agents"), home.AntigravityConfigDir };
        var plugins = configDirectories
            .Select(directory => Path.Combine(directory, "plugins"))
            .Where(Directory.Exists)
            .SelectMany(directory => Directory.EnumerateDirectories(directory))
            .Select(plugin => Path.Combine(plugin, "hooks.json"));

        return [.. configDirectories.Select(directory => Path.Combine(directory, "hooks.json")), .. plugins];
    }
}
