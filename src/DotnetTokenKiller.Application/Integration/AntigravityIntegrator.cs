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
/// <c>dtk init gemini --global</c>: gate G9 verified Antigravity CLI loads <c>~/.gemini/GEMINI.md</c> as a
/// user-global rule.
/// Internal for the same reason as <see cref="ClaudeCodeIntegrator"/>: its constructor takes internal types.
/// </remarks>
internal sealed class AntigravityIntegrator(RtkHookCoexistence rtk, HomePaths home)
    : IProviderIntegrator, IGlobalIntegrator, IHookIntegrator
{
    /// <summary>Printed when this run wrote a project hook, which Antigravity loads only in trusted workspaces.</summary>
    internal const string WorkspaceTrustNote = "Antigravity loads .agents/hooks.json only in workspaces you have trusted.";

    /// <summary>
    /// Printed whenever this run created or updated the hook registration file, in either scope: Antigravity matches
    /// permission rules against the rewritten command, so an allow rule written for <c>dotnet</c> does not cover it.
    /// </summary>
    internal const string PermissionRulesNote =
        "Antigravity checks permission rules against the rewritten command: where you allow command(dotnet), also "
        + "allow command(dtk), or rewritten commands will prompt (and be denied under agy -p).";

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

        if (context.Created.Contains(hook.RegistrationPath) || context.Updated.Contains(hook.RegistrationPath))
        {
            if (scope == HookScope.Project)
            {
                context.Notes.Add(WorkspaceTrustNote);
            }

            context.Notes.Add(PermissionRulesNote);
        }

        var rtkOutcome = await rtk.ReconcileFilesAsync(RtkCandidates(hook.RegistrationPath), cancellationToken).ConfigureAwait(false);
        rtkOutcome.ApplyTo(context);

        return context.ToResult();
    }

    /// <summary>Where rtk registers itself for Antigravity: a hooks file, or a plugin's hooks file, in either scope.</summary>
    /// <param name="registrationPath">
    /// This run's own hook registration path; its directory is the current scope's config directory (a trusted
    /// workspace's <c>.agents</c>, or <c>~/.gemini/config</c>). Combined with <see cref="HomePaths.AntigravityConfigDir"/>
    /// and deduplicated, so a project run also sees an rtk hook left in the global config, the way
    /// <see cref="CodexIntegrator"/> does for <c>.codex/hooks.json</c>, and a global run no longer probes
    /// <c>~/.agents/hooks.json</c>, which Antigravity never reads.
    /// </param>
    private List<string> RtkCandidates(string registrationPath)
    {
        string[] configDirectories =
            [.. new[] { Path.GetDirectoryName(registrationPath)!, home.AntigravityConfigDir }.Distinct(StringComparer.Ordinal)];
        var plugins = configDirectories
            .Select(directory => Path.Combine(directory, "plugins"))
            .Where(Directory.Exists)
            .SelectMany(directory => IntegratorHelpers.EnumerateSafely(directory, Directory.EnumerateDirectories))
            .Select(plugin => Path.Combine(plugin, "hooks.json"));

        return [.. configDirectories.Select(directory => Path.Combine(directory, "hooks.json")), .. plugins];
    }
}
