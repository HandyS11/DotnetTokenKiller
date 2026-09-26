using DotnetTokenKiller.Application.Integration.Hooks;
using DotnetTokenKiller.Domain.Integration;

namespace DotnetTokenKiller.Application.Integration;

/// <summary>Installs dtk integration artifacts for Devin Desktop (formerly Windsurf) and Devin CLI.</summary>
/// <param name="home">Resolves the user's home and Devin config directories for global integration.</param>
/// <remarks>
/// Creates, in a project: <c>.devin/rules/dtk.md</c> and <c>.devin/hooks.v1.json</c>, whose root is the hooks object
/// registering <c>dtk hook devin</c> under <c>PreToolUse</c> for the <c>exec</c> tool. Globally: that registration
/// under <c>hooks</c> in Devin's <c>config.json</c>, and a dtk section in Devin Desktop's always-on
/// <c>global_rules.md</c>. Devin Local and Devin CLI run the hook; the legacy Cascade agent cannot rewrite commands.
/// A project install and uninstall also retire the <c>.windsurf/rules/dtk.md</c> an older dtk wrote, which Devin still
/// reads as a fallback, when its content proves dtk wrote it.
/// Internal for the same reason as <see cref="ClaudeCodeIntegrator"/>: its constructor takes internal types.
/// </remarks>
internal sealed class DevinIntegrator(HomePaths home)
    : IProviderIntegrator, IGlobalIntegrator, IHookIntegrator, IUninstallIntegrator
{
    /// <summary>Printed when this run wrote the hook.</summary>
    internal const string CascadeNote =
        "Devin Local and Devin CLI run the rewrite hook; the legacy Cascade agent cannot rewrite commands.";

    /// <summary>Printed when this run wrote a project hook.</summary>
    internal const string RestrictedModeNote =
        "Devin Desktop runs no hooks while a workspace is in Restricted Mode: trust this workspace for the hook to run.";

    /// <summary>Seconds Devin waits for the hook.</summary>
    private const int HookTimeoutSeconds = 10;

    /// <summary>Devin's shell tool, which the hook's matcher selects.</summary>
    private const string ShellTool = "exec";

    /// <summary>The rule, byte for byte the one <c>dtk init windsurf</c> wrote, so released hashes still recognize it.</summary>
    private static readonly string DevinRule =
        $"""
        # DotnetTokenKiller (dtk)

        {IntegrationInstructions.Intro}

        ## Usage

        {IntegrationInstructions.UsageBody}
        """;

    /// <summary>Printed by an install when an edited <c>.windsurf/rules/dtk.md</c> is left in place.</summary>
    /// <param name="path">The legacy rule's path.</param>
    internal static string LegacyRuleKeptNote(string path) =>
        $"{path} was left in place: it differs from every rule dtk wrote, so it was edited. Devin still reads "
        + ".windsurf/rules, so delete it once .devin/rules/dtk.md covers it.";

    /// <summary>
    /// Printed by an uninstall when an edited <c>.windsurf/rules/dtk.md</c> is left in place. Unlike
    /// <see cref="LegacyRuleKeptNote"/>, an uninstall has just removed <c>.devin/rules/dtk.md</c>, so it cannot point
    /// there as the reason to delete the legacy file later.
    /// </summary>
    /// <param name="path">The legacy rule's path.</param>
    internal static string LegacyRuleKeptOnUninstallNote(string path) =>
        $"{path} was left in place: it differs from every rule dtk wrote, so it was edited. Delete it yourself if "
        + "nothing needs it.";

    /// <inheritdoc/>
    public string ProviderName => "devin";

    /// <inheritdoc/>
    public IReadOnlyList<HookInstallation> DescribeHooks(string directory, HookScope scope) =>
    [
        new HookInstallation(
            ProviderName,
            scope,
            scope == HookScope.Global
                ? Path.Combine(home.DevinConfigDir, "config.json")
                : Path.Combine(directory, ".devin", "hooks.v1.json"),
            HookCommands.Invocation(ProviderName),
            LegacyScriptPath: null,
            HookPayloadKind.Devin)
    ];

    /// <inheritdoc/>
    public async Task<IntegrationResult> IntegrateAsync(string directory, bool force, CancellationToken cancellationToken)
    {
        var context = new IntegrationContext(force);

        await IntegratorHelpers.WriteFileAsync(RulePath(directory), DevinRule, context, cancellationToken).ConfigureAwait(false);
        await WriteHookAsync(directory, HookScope.Project, context, cancellationToken).ConfigureAwait(false);
        await RetireWindsurfRuleAsync(directory, context, isUninstall: false, cancellationToken).ConfigureAwait(false);

        return context.ToResult();
    }

    /// <inheritdoc/>
    public async Task<IntegrationResult> IntegrateGlobalAsync(bool force, CancellationToken cancellationToken)
    {
        var context = new IntegrationContext(force);

        await IntegratorHelpers.WriteSectionBasedFileAsync(
            home.WindsurfGlobalRulesPath, SharedInstructionArtifacts.SectionMarker, SharedInstructionArtifacts.SectionEndMarker,
            SharedInstructionArtifacts.Section, context, cancellationToken).ConfigureAwait(false);
        await WriteHookAsync(home.Home, HookScope.Global, context, cancellationToken).ConfigureAwait(false);

        return context.ToResult();
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> SharedArtifactPaths(string directory, HookScope scope) => [];

    /// <inheritdoc/>
    public async Task<IntegrationResult> UninstallAsync(
        string directory, HookScope scope, IReadOnlyDictionary<string, string> sharedInUse, CancellationToken cancellationToken)
    {
        var root = scope == HookScope.Global ? home.Home : directory;
        var context = IntegrationContext.ForUninstall(root, sharedInUse);

        if (scope == HookScope.Project)
        {
            await UninstallHelpers.RemoveOwnedFileAsync(
                RulePath(directory), DevinRule, IntegrationInstructions.ReleasedMarkdownRuleHashes, "dtk init devin", context,
                cancellationToken).ConfigureAwait(false);
            await RetireWindsurfRuleAsync(directory, context, isUninstall: true, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await UninstallHelpers.RemoveSectionAsync(
                home.WindsurfGlobalRulesPath, SharedInstructionArtifacts.SectionMarker, SharedInstructionArtifacts.SectionEndMarker,
                context, cancellationToken).ConfigureAwait(false);
        }

        await UninstallHelpers.RemoveHookRegistrationAsync(Registration(DescribeHooks(root, scope)[0]), context, cancellationToken)
            .ConfigureAwait(false);

        return context.ToResult();
    }

    private static string RulePath(string directory) => Path.Combine(directory, ".devin", "rules", "dtk.md");

    private static string LegacyRulePath(string directory) => Path.Combine(directory, ".windsurf", "rules", "dtk.md");

    /// <summary>The project file's root is the hooks object; the global <c>config.json</c> holds it under <c>hooks</c>.</summary>
    /// <param name="hook">The hook to build a registration for.</param>
    private static HookRegistrationSpec Registration(HookInstallation hook) =>
        new(hook.RegistrationPath, "PreToolUse", ShellTool, hook.Command, HookTimeoutSeconds,
            ContainerKey: hook.Scope == HookScope.Global ? "hooks" : null);

    /// <summary>
    /// Deletes the <c>.windsurf/rules/dtk.md</c> an older dtk wrote when its content proves it (the current body or a
    /// released hash), pruning the directories that leaves empty; an edited copy is kept with a note. An install has no
    /// prune boundary of its own, so it deletes through a throwaway uninstall context rooted at the project.
    /// </summary>
    /// <param name="directory">The project root.</param>
    /// <param name="context">The accumulator to fold the deletion's outcome into.</param>
    /// <param name="isUninstall">
    /// Whether this runs from <see cref="UninstallAsync"/>, which has already removed <c>.devin/rules/dtk.md</c> and so
    /// needs <see cref="LegacyRuleKeptOnUninstallNote"/> instead of <see cref="LegacyRuleKeptNote"/> when the legacy
    /// file is kept.
    /// </param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private static async Task RetireWindsurfRuleAsync(
        string directory, IntegrationContext context, bool isUninstall, CancellationToken cancellationToken)
    {
        var path = LegacyRulePath(directory);
        if (!File.Exists(path))
        {
            return;
        }

        var existing = await IntegratorHelpers.TryReadExistingAsync(path, cancellationToken).ConfigureAwait(false);
        var ownsIt = existing is not null
            && (string.Equals(existing, DevinRule.ReplaceLineEndings("\n"), StringComparison.Ordinal)
                || IntegrationInstructions.ReleasedMarkdownRuleHashes.Contains(UninstallHelpers.HashOwnedFile(existing), StringComparer.Ordinal));

        if (!ownsIt)
        {
            context.Notes.Add(isUninstall ? LegacyRuleKeptOnUninstallNote(path) : LegacyRuleKeptNote(path));
            return;
        }

        var deleter = context.PruneBoundary is null
            ? IntegrationContext.ForUninstall(directory, new Dictionary<string, string>())
            : context;
        UninstallHelpers.DeleteFile(path, deleter);

        if (!ReferenceEquals(deleter, context))
        {
            context.Removed.AddRange(deleter.Removed);
            context.Notes.AddRange(deleter.Notes);
        }
    }

    private async Task WriteHookAsync(string directory, HookScope scope, IntegrationContext context, CancellationToken cancellationToken)
    {
        var hook = DescribeHooks(directory, scope)[0];
        await IntegratorHelpers.WriteHookRegistrationAsync(Registration(hook), context, cancellationToken).ConfigureAwait(false);

        if (context.Created.Contains(hook.RegistrationPath) || context.Updated.Contains(hook.RegistrationPath))
        {
            context.Notes.Add(CascadeNote);
            if (scope == HookScope.Project)
            {
                context.Notes.Add(RestrictedModeNote);
            }
        }

        if (ImportedClaudeHook.IsRegisteredIn(ImportedClaudeHook.SettingsFiles(home, scope == HookScope.Project ? directory : null)))
        {
            context.Notes.Add(ImportedClaudeHook.Note("Devin"));
        }
    }
}
