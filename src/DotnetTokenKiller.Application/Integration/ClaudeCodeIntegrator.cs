using System.Text.Json;
using System.Text.Json.Nodes;
using DotnetTokenKiller.Application.Integration.Hooks;
using DotnetTokenKiller.Domain.Integration;

namespace DotnetTokenKiller.Application.Integration;

/// <summary>Installs dtk integration artifacts for Claude Code.</summary>
/// <param name="rtk">Detects and reconciles an rtk hook so dtk owns dotnet commands.</param>
/// <param name="home">Resolves the user's home directory for global (home-config) integration.</param>
/// <remarks>
/// Creates:
/// <list type="bullet">
///   <item><description><c>.claude/skills/dotnet-token-killer/SKILL.md</c></description></item>
///   <item><description>
///     <c>.claude/settings.json</c> registering <c>dtk hook claude</c> (merged, never overwritten) for
///     <c>PreToolUse</c> (the rewrite) and for <c>SessionStart</c> and <c>SubagentStart</c> (dtk's rules for every
///     agent, see <see cref="IntegrationInstructions.AgentRules"/>); a Python hook left by an older dtk is migrated
///   </description></item>
///   <item><description>
///     When an rtk PreToolUse hook is detected, merges <c>exclude_commands = ["dotnet"]</c> into the config file rtk
///     reads (see <see cref="RtkConfigLocator"/>) so dtk (not rtk) owns dotnet commands. Silent if already excluded.
///   </description></item>
/// </list>
/// Declared <see langword="internal"/> (rather than <see langword="public"/>, its original
/// accessibility) because its primary constructor takes the <see langword="internal"/>
/// <see cref="RtkHookCoexistence"/>: a primary constructor is as accessible as its containing
/// type, and the compiler rejects (CS0051) a public constructor exposing a less-accessible
/// parameter type. Keeping <see cref="RtkHookCoexistence"/> internal (rather than promoting it to
/// public) requires this type to be internal too; callers still reach it polymorphically through
/// the public <see cref="IProviderIntegrator"/> via DI, and tests reach it directly via
/// <c>InternalsVisibleTo</c>.
/// </remarks>
internal sealed class ClaudeCodeIntegrator(RtkHookCoexistence rtk, HomePaths home)
    : IProviderIntegrator, IGlobalIntegrator, IHookIntegrator, IUninstallIntegrator, IHookApprovalInspector
{
    /// <summary>The doctor check reporting the two hooks that give agents dtk's rules.</summary>
    private const string ContextHooksLabel = "context hooks";

    /// <summary>The events whose registration gives every agent dtk's rules, in the order doctor names them.</summary>
    private static readonly string[] ContextEvents = ["SessionStart", "SubagentStart"];
    /// <summary>The per-user settings file Claude Code reads beside <c>settings.json</c>, which dtk never edits.</summary>
    private const string LocalSettingsFileName = "settings.local.json";

    /// <summary>
    /// The <c>SessionStart</c> sources dtk's rules are injected on: a new session, <c>/clear</c>, and a compaction,
    /// which may summarize them away. Not <c>resume</c>, whose transcript keeps the context it was given.
    /// </summary>
    internal const string SessionStartMatcher = "startup|clear|compact";

    /// <inheritdoc/>
    public string ProviderName => "claude";

    /// <inheritdoc/>
    public IReadOnlyList<HookInstallation> DescribeHooks(string directory, HookScope scope)
    {
        var baseDirectory = scope == HookScope.Global ? home.ClaudeDir : Path.Combine(directory, ".claude");

        return
        [
            new HookInstallation(
                ProviderName,
                scope,
                Path.Combine(baseDirectory, "settings.json"),
                HookCommands.Invocation(ProviderName),
                Path.Combine(baseDirectory, "hooks", IntegratorHelpers.LegacyHookScriptName),
                HookPayloadKind.ClaudeCode)
        ];
    }

    /// <inheritdoc/>
    public Task<IntegrationResult> IntegrateAsync(string directory, bool force, CancellationToken cancellationToken)
        => IntegrateCoreAsync(Path.Combine(directory, ".claude"), directory, HookScope.Project, force, cancellationToken);

    /// <inheritdoc/>
    public Task<IntegrationResult> IntegrateGlobalAsync(bool force, CancellationToken cancellationToken)
        => IntegrateCoreAsync(home.ClaudeDir, home.Home, HookScope.Global, force, cancellationToken);

    private async Task<IntegrationResult> IntegrateCoreAsync(
        string baseDirectory,
        string hookDirectory,
        HookScope scope,
        bool force,
        CancellationToken cancellationToken)
    {
        var context = new IntegrationContext(force);

        await IntegratorHelpers.WriteGeneratedFileAsync(SkillArtifact(baseDirectory), context, cancellationToken)
            .ConfigureAwait(false);

        var hook = DescribeHooks(hookDirectory, scope)[0];

        var registrations = Registrations(hook);
        var replacedLegacy = await IntegratorHelpers.WriteHookRegistrationAsync(registrations[0], context, cancellationToken)
            .ConfigureAwait(false);
        foreach (var contextRegistration in registrations.Skip(1))
        {
            await IntegratorHelpers.WriteHookRegistrationAsync(contextRegistration, context, cancellationToken)
                .ConfigureAwait(false);
        }

        context.SettleOutcome(hook.RegistrationPath);

        await IntegratorHelpers.RetireLegacyHookScriptAsync(
            hook.LegacyScriptPath!, replacedLegacy, HookFiles(hook), context, cancellationToken).ConfigureAwait(false);

        var rtkOutcome = await rtk.ReconcileAsync(hookDirectory, cancellationToken).ConfigureAwait(false);
        rtkOutcome.ApplyTo(context);

        return context.ToResult();
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> SharedArtifactPaths(string directory, HookScope scope) => [];

    /// <summary>
    /// Whether the settings file that registers the rewrite also registers <c>dtk hook claude</c> for
    /// <c>SessionStart</c> and <c>SubagentStart</c>, without which agents never get dtk's rules: an install made before
    /// those hooks existed has only <c>PreToolUse</c>, and re-running <c>dtk init claude</c> adds them.
    /// </summary>
    /// <param name="installation">A registration doctor found current.</param>
    /// <param name="projectDirectory">The directory doctor treats as the project root; unused.</param>
    public IReadOnlyList<HookApprovalFinding> InspectApproval(HookInstallation installation, string projectDirectory)
    {
        ArgumentNullException.ThrowIfNull(installation);

        var path = installation.RegistrationPath;
        List<string> missing;
        try
        {
            var hooks = JsonNode.Parse(File.ReadAllText(path), documentOptions: IntegratorHelpers.LenientJson)?["hooks"];
            missing = [.. ContextEvents.Where(eventName => !Registers(hooks, eventName, installation.Command))];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException
                                       or InvalidOperationException or ArgumentException)
        {
            return [new HookApprovalFinding(ContextHooksLabel, false, $"{path} could not be read: {ex.Message}")];
        }

        if (missing.Count == 0)
        {
            return
            [
                new HookApprovalFinding(
                    ContextHooksLabel, true, "SessionStart and SubagentStart registered: agents in .NET projects get dtk's rules")
            ];
        }

        var scopeFlag = installation.Scope == HookScope.Global ? " --global" : string.Empty;
        return
        [
            new HookApprovalFinding(
                ContextHooksLabel,
                false,
                $"{string.Join(" and ", missing)} not registered in {path}: agents will not get dtk's rules. "
                + $"Run 'dtk init {ProviderName}{scopeFlag}'.")
        ];
    }

    /// <summary>Whether the hooks object registers <paramref name="command"/>, or an equivalent of it, for an event.</summary>
    /// <param name="hooks">The settings file's <c>hooks</c> object, if any.</param>
    /// <param name="eventName">The event.</param>
    /// <param name="command">The command dtk registers.</param>
    private static bool Registers(JsonNode? hooks, string eventName, string command) =>
        hooks is JsonObject events
        && events[eventName] is JsonArray entries
        && IntegratorHelpers.FindEquivalentEntries(entries, command).Count > 0;

    /// <inheritdoc/>
    public async Task<IntegrationResult> UninstallAsync(
        string directory, HookScope scope, IReadOnlyDictionary<string, string> sharedInUse, CancellationToken cancellationToken)
    {
        var (baseDirectory, hookDirectory) = scope == HookScope.Global
            ? (home.ClaudeDir, home.Home)
            : (Path.Combine(directory, ".claude"), directory);
        var context = IntegrationContext.ForUninstall(hookDirectory, sharedInUse);

        await UninstallHelpers.RemoveGeneratedFileAsync(SkillArtifact(baseDirectory), context, cancellationToken)
            .ConfigureAwait(false);

        var hook = DescribeHooks(hookDirectory, scope)[0];
        foreach (var registration in Registrations(hook))
        {
            await UninstallHelpers.RemoveHookRegistrationAsync(registration, context, cancellationToken).ConfigureAwait(false);
        }

        context.SettleOutcome(hook.RegistrationPath);
        await UninstallHelpers.RemoveLegacyHookScriptAsync(hook.LegacyScriptPath!, HookFiles(hook), context, cancellationToken)
            .ConfigureAwait(false);

        await rtk.NoteRemainingExclusionAsync(
                context,
                rtk.IsRtkHookPresent(hookDirectory),
                cancellationToken)
            .ConfigureAwait(false);

        return context.ToResult();
    }

    private static GeneratedArtifact SkillArtifact(string baseDirectory) =>
        SharedInstructionArtifacts.SkillArtifact(Path.Combine(baseDirectory, "skills"));

    /// <summary>
    /// The three registrations of <c>dtk hook claude</c>, the rewrite first: <c>PreToolUse</c> for <c>Bash</c>,
    /// <c>SessionStart</c> on <see cref="SessionStartMatcher"/>, and <c>SubagentStart</c> for every agent type (the
    /// hook itself skips the ones without a shell).
    /// </summary>
    /// <param name="hook">The installation.</param>
    private static HookRegistrationSpec[] Registrations(HookInstallation hook) =>
    [
        new(hook.RegistrationPath, "PreToolUse", "Bash", hook.Command),
        new(hook.RegistrationPath, "SessionStart", SessionStartMatcher, hook.Command),
        new(hook.RegistrationPath, "SubagentStart", "*", hook.Command)
    ];

    /// <summary>
    /// The files Claude Code runs hooks from beside the registration: dtk merges <c>settings.json</c> only, but Claude
    /// Code also runs the hooks in <c>settings.local.json</c>.
    /// </summary>
    /// <param name="hook">The installation.</param>
    private static string[] HookFiles(HookInstallation hook) =>
        [hook.RegistrationPath, Path.Combine(Path.GetDirectoryName(hook.RegistrationPath)!, LocalSettingsFileName)];
}
