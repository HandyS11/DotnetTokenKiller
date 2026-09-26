using DotnetTokenKiller.Application.Integration.Hooks;
using DotnetTokenKiller.Domain.Integration;

namespace DotnetTokenKiller.Application.Integration;

/// <summary>Installs dtk integration artifacts for Cursor (the IDE and <c>cursor-agent</c>).</summary>
/// <param name="rtk">Detects and reconciles an rtk hook so dtk owns dotnet commands.</param>
/// <param name="home">Resolves the user's home directory for global integration.</param>
/// <remarks>
/// Creates:
/// <list type="bullet">
///   <item><description><c>.cursor/rules/dtk.mdc</c> (Cursor project rule; project scope only)</description></item>
///   <item><description>
///     <c>.cursor/hooks.json</c> or <c>~/.cursor/hooks.json</c> registering <c>dtk hook cursor</c> under
///     <c>preToolUse</c> for the <c>Shell</c> tool (merged, never overwritten)
///   </description></item>
/// </list>
/// Internal for the same reason as <see cref="ClaudeCodeIntegrator"/>: its constructor takes internal types.
/// </remarks>
internal sealed class CursorIntegrator(RtkHookCoexistence rtk, HomePaths home)
    : IProviderIntegrator, IGlobalIntegrator, IHookIntegrator, IUninstallIntegrator
{
    /// <summary>Printed when this run wrote the hook: Cursor's rewrite carries an approval, so dtk narrows what it rewrites.</summary>
    internal const string AutoApprovalNote =
        "Cursor approves a command its hook rewrites, so dtk rewrites only simple `dotnet build`, `test`, `restore`, "
        + "`clean`, `format` and `list package` commands; chained or piped commands, `publish` and `pack` run as written.";

    /// <summary>Printed when this run wrote a project hook.</summary>
    internal const string TrustNote =
        "Cursor runs project hooks only in trusted workspaces, and cursor-agent needs --trust when it runs headless.";

    /// <summary>Printed when this run wrote the hook: the rewrites Cursor is known to drop.</summary>
    internal const string KnownGapsNote =
        "Cursor sends hooks no payload in remote Linux workspaces and may drop rewrites for subagents' shell calls; "
        + "those commands run as written, guided only by the rule.";

    /// <summary>Printed by a global install, which has no rule file to write.</summary>
    internal const string GlobalRuleNote =
        "Cursor keeps user rules in its settings, not in a file, so the global install adds only the hook. "
        + "Run 'dtk init cursor' in a project to add the rule there.";

    private static readonly string CursorRule =
        $"""
        ---
        description: Use dtk instead of dotnet for {IntegrationInstructions.SubcommandProse} commands
        globs:
          - "**/*.cs"
          - "**/*.csproj"
          - "**/*.slnx"
          - "**/*.sln"
        alwaysApply: false
        ---

        # DotnetTokenKiller (dtk)

        {IntegrationInstructions.Intro}

        ## Usage

        {IntegrationInstructions.UsageBody}
        """;

    /// <inheritdoc/>
    public string ProviderName => "cursor";

    /// <inheritdoc/>
    public IReadOnlyList<HookInstallation> DescribeHooks(string directory, HookScope scope) =>
    [
        new HookInstallation(
            ProviderName,
            scope,
            Path.Combine(scope == HookScope.Global ? home.CursorDir : Path.Combine(directory, ".cursor"), "hooks.json"),
            HookCommands.Invocation(ProviderName),
            LegacyScriptPath: null,
            HookPayloadKind.Cursor)
    ];

    /// <inheritdoc/>
    public async Task<IntegrationResult> IntegrateAsync(string directory, bool force, CancellationToken cancellationToken)
    {
        var context = new IntegrationContext(force);

        await IntegratorHelpers.WriteFileAsync(RulePath(directory), CursorRule, context, cancellationToken).ConfigureAwait(false);
        await WriteHookAsync(directory, HookScope.Project, context, cancellationToken).ConfigureAwait(false);

        return context.ToResult();
    }

    /// <inheritdoc/>
    public async Task<IntegrationResult> IntegrateGlobalAsync(bool force, CancellationToken cancellationToken)
    {
        var context = new IntegrationContext(force);

        await WriteHookAsync(home.Home, HookScope.Global, context, cancellationToken).ConfigureAwait(false);
        context.Notes.Add(GlobalRuleNote);

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
                RulePath(directory), CursorRule, IntegrationInstructions.ReleasedCursorRuleHashes, "dtk init cursor", context,
                cancellationToken).ConfigureAwait(false);
        }

        var hook = DescribeHooks(root, scope)[0];
        await CursorHooksFile.RemoveAsync(hook.RegistrationPath, hook.Command, context, cancellationToken).ConfigureAwait(false);

        rtk.NoteRemainingExclusion(context, RtkHookCoexistence.IsRtkRewriteReferencedIn(RtkCandidates(root)));

        return context.ToResult();
    }

    private static string RulePath(string directory) => Path.Combine(directory, ".cursor", "rules", "dtk.mdc");

    private async Task WriteHookAsync(string directory, HookScope scope, IntegrationContext context, CancellationToken cancellationToken)
    {
        var hook = DescribeHooks(directory, scope)[0];
        await CursorHooksFile.WriteAsync(hook.RegistrationPath, hook.Command, context, cancellationToken).ConfigureAwait(false);

        if (context.Created.Contains(hook.RegistrationPath) || context.Updated.Contains(hook.RegistrationPath))
        {
            context.Notes.Add(AutoApprovalNote);
            if (scope == HookScope.Project)
            {
                context.Notes.Add(TrustNote);
            }

            context.Notes.Add(KnownGapsNote);
        }

        if (ImportedClaudeHook.IsRegisteredIn(ImportedClaudeHook.SettingsFiles(home, scope == HookScope.Project ? directory : null)))
        {
            context.Notes.Add(ImportedClaudeHook.Note("Cursor"));
        }

        var rtkOutcome = await rtk.ReconcileFilesAsync(RtkCandidates(directory), cancellationToken).ConfigureAwait(false);
        rtkOutcome.ApplyTo(context);
    }

    /// <summary>
    /// Where rtk registers itself for Cursor: <c>rtk hook cursor</c> in either <c>hooks.json</c>, or (rtk before its
    /// native hook) the <c>rtk-rewrite.sh</c> script, which runs <c>rtk rewrite</c>.
    /// </summary>
    /// <param name="directory">The project root, or the home directory for a global run.</param>
    private List<string> RtkCandidates(string directory) =>
        [.. new[]
        {
            Path.Combine(directory, ".cursor", "hooks.json"),
            Path.Combine(home.CursorDir, "hooks.json"),
            Path.Combine(home.CursorDir, "hooks", "rtk-rewrite.sh")
        }.Distinct(StringComparer.Ordinal)];
}
