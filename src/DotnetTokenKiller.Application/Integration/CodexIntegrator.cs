using DotnetTokenKiller.Application.Integration.Hooks;
using DotnetTokenKiller.Domain.Integration;

namespace DotnetTokenKiller.Application.Integration;

/// <summary>Installs dtk integration artifacts for OpenAI Codex CLI.</summary>
/// <param name="rtk">Detects and reconciles an rtk hook so dtk owns dotnet commands.</param>
/// <param name="home">Resolves the user's home directory and <c>$CODEX_HOME</c> for global integration.</param>
/// <remarks>
/// Creates:
/// <list type="bullet">
///   <item><description><c>AGENTS.md</c> (section-based merge) and <c>.agents/skills/dotnet-token-killer/SKILL.md</c></description></item>
///   <item><description>
///     <c>.codex/hooks.json</c> registering <c>dtk hook codex</c> under <c>PreToolUse</c> (merged, never overwritten)
///   </description></item>
/// </list>
/// Codex runs a hook only once the user has approved it, and remembers the approval as a hash of the definition, so
/// the command and timeout registered here must never change. dtk does not approve the hook itself: the approval is
/// the user's record of reviewing what runs before every shell command.
/// Internal for the same reason as <see cref="ClaudeCodeIntegrator"/>: its constructor takes internal types.
/// </remarks>
internal sealed class CodexIntegrator(RtkHookCoexistence rtk, HomePaths home)
    : IProviderIntegrator, IGlobalIntegrator, IHookIntegrator, IHookApprovalInspector
{
    /// <summary>Printed when this run wrote the hook, which Codex will not run until the user approves it.</summary>
    internal const string ApprovalNote =
        "Codex runs this hook only after you approve it: open Codex and review it under /hooks "
        + "('codex exec' skips unapproved hooks silently). Requires Codex 0.131 or later.";

    /// <summary>Printed when this run wrote a project hook, which Codex reads only in trusted projects.</summary>
    internal const string ProjectTrustNote = "Codex reads .codex/ only in projects you have trusted.";

    /// <summary>Seconds Codex waits for the hook. Part of the approval hash: never change it.</summary>
    private const int HookTimeoutSeconds = 10;

    /// <inheritdoc/>
    public string ProviderName => "codex";

    /// <inheritdoc/>
    public IReadOnlyList<HookInstallation> DescribeHooks(string directory, HookScope scope)
    {
        var codexDir = scope == HookScope.Global ? home.CodexDir : Path.Combine(directory, ".codex");

        return
        [
            new HookInstallation(
                ProviderName,
                scope,
                Path.Combine(codexDir, "hooks.json"),
                HookCommands.Invocation(ProviderName),
                LegacyScriptPath: null,
                HookPayloadKind.CodexCli)
        ];
    }

    /// <inheritdoc/>
    public IReadOnlyList<HookApprovalFinding> InspectApproval(HookInstallation installation, string projectDirectory)
    {
        var configPath = Path.Combine(home.CodexDir, "config.toml");
        var config = CodexConfig.Load(configPath);

        if (!config.IsReadable)
        {
            return
            [
                new HookApprovalFinding("hook approval", false,
                    $"{configPath} could not be read, so dtk cannot tell whether Codex will run this hook")
            ];
        }

        var findings = new List<HookApprovalFinding> { ApprovalFinding(config, installation.RegistrationPath, configPath) };

        if (installation.Scope == HookScope.Project)
        {
            var codexDir = Path.GetDirectoryName(installation.RegistrationPath);
            findings.Add(config.TrustsProject(projectDirectory)
                ? new HookApprovalFinding("project trust", true, $"{projectDirectory} is a trusted project")
                : new HookApprovalFinding("project trust", false,
                    $"Codex reads {codexDir} only in trusted projects — trust this project when Codex asks"));
        }

        return findings;
    }

    private static HookApprovalFinding ApprovalFinding(CodexConfig config, string registrationPath, string configPath)
    {
        if (config.HasHookApproval(registrationPath))
        {
            return new HookApprovalFinding("hook approval", true,
                $"approval recorded in {configPath} (dtk cannot tell whether it matches the current definition)");
        }

        return config.IsHookTurnedOff(registrationPath)
            ? new HookApprovalFinding("hook approval", false,
                "turned off — Codex skips this hook until you turn it back on under /hooks")
            : new HookApprovalFinding("hook approval", false,
                "not yet approved — Codex skips this hook until you review it under /hooks");
    }

    /// <inheritdoc/>
    public Task<IntegrationResult> IntegrateAsync(string directory, bool force, CancellationToken cancellationToken)
        => IntegrateCoreAsync(
            Path.Combine(directory, "AGENTS.md"),
            Path.Combine(directory, ".agents", "skills"),
            directory,
            HookScope.Project,
            force,
            cancellationToken);

    /// <inheritdoc/>
    public Task<IntegrationResult> IntegrateGlobalAsync(bool force, CancellationToken cancellationToken)
        => IntegrateCoreAsync(
            Path.Combine(home.CodexDir, "AGENTS.md"),
            home.AgentsSkillsDir,
            home.Home,
            HookScope.Global,
            force,
            cancellationToken);

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
            new HookRegistrationSpec(hook.RegistrationPath, "PreToolUse", "Bash", hook.Command, HookTimeoutSeconds),
            context, cancellationToken).ConfigureAwait(false);

        if (context.Created.Contains(hook.RegistrationPath) || context.Updated.Contains(hook.RegistrationPath))
        {
            context.Notes.Add(ApprovalNote);
            if (scope == HookScope.Project)
            {
                context.Notes.Add(ProjectTrustNote);
            }
        }

        // The current scope's own registration path, plus the global one, deduplicated. Building the
        // project-scope candidate from `hookDirectory` (as an earlier version did) breaks for
        // IntegrateGlobalAsync, where hookDirectory is home.Home: that would probe ~/.codex/hooks.json even
        // when $CODEX_HOME points elsewhere, reconciling rtk's config over a file Codex never reads.
        // `hook.RegistrationPath` is already the correct path for whichever scope this run is (project or
        // global); the extra global lookup only matters when scope is Project, so a project run also sees
        // an rtk hook left in the global hooks.json.
        var rtkOutcome = await rtk.ReconcileFilesAsync(
            [.. new[] { hook.RegistrationPath, DescribeHooks(hookDirectory, HookScope.Global)[0].RegistrationPath }
                .Distinct(StringComparer.Ordinal)],
            cancellationToken).ConfigureAwait(false);
        rtkOutcome.ApplyTo(context);

        return context.ToResult();
    }
}
