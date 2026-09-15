using System.Text.Json;
using System.Text.Json.Nodes;
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

    /// <summary>The name of the finding that reports whether Codex will run dtk's handler.</summary>
    private const string HookApprovalCheck = "hook approval";

    /// <summary>Reported when Codex has recorded no approval for dtk's handler.</summary>
    private static readonly HookApprovalFinding NotYetApproved = new(HookApprovalCheck, false,
        "not yet approved — Codex skips this hook until you review it under /hooks");

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
                new HookApprovalFinding(HookApprovalCheck, false,
                    $"{configPath} could not be read, so dtk cannot tell whether Codex will run this hook")
            ];
        }

        var findings = new List<HookApprovalFinding> { ApprovalFinding(config, installation, configPath) };

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

    private static HookApprovalFinding ApprovalFinding(
        CodexConfig config, HookInstallation installation, string configPath)
    {
        var path = installation.RegistrationPath;
        if (FindHandler(path, installation.Command) is not (var group, var handler))
        {
            return NotYetApproved;
        }

        if (config.HasHookApproval(path, group, handler))
        {
            return new HookApprovalFinding(HookApprovalCheck, true,
                $"approval recorded in {configPath} (dtk cannot tell whether it matches the current definition)");
        }

        return config.IsHookTurnedOff(path, group, handler)
            ? new HookApprovalFinding(HookApprovalCheck, false,
                "turned off — Codex skips this hook until you turn it back on under /hooks")
            : NotYetApproved;
    }

    /// <summary>
    /// The position Codex keys a handler's approval by: the index of its group in <c>hooks.PreToolUse</c> and its index
    /// in that group's <c>hooks</c>, counting every entry as written. Returns the first handler whose command runs
    /// <paramref name="command"/>, or <see langword="null"/> when none does or the file cannot be read.
    /// </summary>
    /// <param name="registrationPath">The <c>hooks.json</c> to search.</param>
    /// <param name="command">The command dtk registers.</param>
    private static (int Group, int Handler)? FindHandler(string registrationPath, string command)
    {
        // Parsed as leniently as doctor read the registration, and guarded the same way: JsonNode.Parse accepts a
        // repeated key and throws ArgumentException only when the object is first read.
        try
        {
            if (JsonNode.Parse(File.ReadAllText(registrationPath), documentOptions: IntegratorHelpers.LenientJson)
                    is not JsonObject root
                || root["hooks"] is not JsonObject hooks
                || hooks["PreToolUse"] is not JsonArray groups)
            {
                return null;
            }

            for (var group = 0; group < groups.Count; group++)
            {
                if (groups[group] is not JsonObject matcherGroup || matcherGroup["hooks"] is not JsonArray handlers)
                {
                    continue;
                }

                for (var handler = 0; handler < handlers.Count; handler++)
                {
                    if (handlers[handler] is JsonObject entry
                        && entry["command"] is JsonValue value
                        && value.TryGetValue<string>(out var text)
                        && text.Contains(command, StringComparison.Ordinal))
                    {
                        return (group, handler);
                    }
                }
            }

            return null;
        }
        catch (Exception ex)
            when (ex is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return null;
        }
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
