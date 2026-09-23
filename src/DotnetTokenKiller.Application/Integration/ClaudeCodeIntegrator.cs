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
///     <c>.claude/settings.json</c> registering <c>dtk hook claude</c> (merged, never overwritten); a Python
///     hook left by an older dtk is migrated
///   </description></item>
///   <item><description>
///     When an rtk PreToolUse hook is detected, merges <c>exclude_commands = ["dotnet"]</c> into
///     <c>~/.config/rtk/config.toml</c> so dtk (not rtk) owns dotnet commands. Silent if already excluded.
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
    : IProviderIntegrator, IGlobalIntegrator, IHookIntegrator, IUninstallIntegrator
{
    /// <summary>The per-user settings file Claude Code reads beside <c>settings.json</c>, which dtk never edits.</summary>
    private const string LocalSettingsFileName = "settings.local.json";

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

        var replacedLegacy = await IntegratorHelpers.WriteHookRegistrationAsync(Registration(hook), context, cancellationToken)
            .ConfigureAwait(false);

        await IntegratorHelpers.RetireLegacyHookScriptAsync(
            hook.LegacyScriptPath!, replacedLegacy, HookFiles(hook), context, cancellationToken).ConfigureAwait(false);

        var rtkOutcome = await rtk.ReconcileAsync(hookDirectory, cancellationToken).ConfigureAwait(false);
        rtkOutcome.ApplyTo(context);

        return context.ToResult();
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> SharedArtifactPaths(string directory, HookScope scope) => [];

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
        await UninstallHelpers.RemoveHookRegistrationAsync(Registration(hook), context, cancellationToken).ConfigureAwait(false);
        await UninstallHelpers.RemoveLegacyHookScriptAsync(hook.LegacyScriptPath!, HookFiles(hook), context, cancellationToken)
            .ConfigureAwait(false);

        rtk.NoteRemainingExclusion(context, rtk.IsRtkHookPresent(hookDirectory));

        return context.ToResult();
    }

    private static GeneratedArtifact SkillArtifact(string baseDirectory) =>
        SharedInstructionArtifacts.SkillArtifact(Path.Combine(baseDirectory, "skills"));

    private static HookRegistrationSpec Registration(HookInstallation hook) =>
        new(hook.RegistrationPath, "PreToolUse", "Bash", hook.Command);

    /// <summary>
    /// The files Claude Code runs hooks from beside the registration: dtk merges <c>settings.json</c> only, but Claude
    /// Code also runs the hooks in <c>settings.local.json</c>.
    /// </summary>
    /// <param name="hook">The installation.</param>
    private static string[] HookFiles(HookInstallation hook) =>
        [hook.RegistrationPath, Path.Combine(Path.GetDirectoryName(hook.RegistrationPath)!, LocalSettingsFileName)];
}
