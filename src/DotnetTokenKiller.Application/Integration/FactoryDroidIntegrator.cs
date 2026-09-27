using DotnetTokenKiller.Application.Integration.Hooks;
using DotnetTokenKiller.Domain.Integration;

namespace DotnetTokenKiller.Application.Integration;

/// <summary>Installs dtk integration artifacts for Factory Droid.</summary>
/// <param name="rtk">Detects and reconciles an rtk hook so dtk owns dotnet commands.</param>
/// <param name="home">Resolves the user's home and <c>.factory</c> directories for global integration.</param>
/// <remarks>
/// Creates the shared <c>AGENTS.md</c> section and <c>.agents/skills</c> skill (<c>~/.factory/AGENTS.md</c> and
/// <c>~/.agents/skills</c> globally), and registers <c>dtk hook droid</c> under <c>PreToolUse</c> for the
/// <c>Execute</c> tool in the file <see cref="FactoryDroidHooks.ResolveTarget"/> picks (merged, never overwritten).
/// Internal for the same reason as <see cref="ClaudeCodeIntegrator"/>: its constructor takes internal types.
/// </remarks>
internal sealed class FactoryDroidIntegrator(RtkHookCoexistence rtk, HomePaths home)
    : IProviderIntegrator, IGlobalIntegrator, IHookIntegrator, IUninstallIntegrator
{
    /// <summary>Printed when this run wrote the hook: Droid snapshots hooks when a session starts.</summary>
    internal const string SessionNote =
        "Droid reads hooks when a session starts: restart any running droid session for the hook to take effect.";

    /// <summary>Seconds Droid waits for the hook.</summary>
    private const int HookTimeoutSeconds = 10;

    /// <summary>Droid's shell tool, which the hook's matcher selects.</summary>
    private const string ShellTool = "Execute";

    /// <inheritdoc/>
    public string ProviderName => "droid";

    /// <inheritdoc/>
    public IReadOnlyList<HookInstallation> DescribeHooks(string directory, HookScope scope) =>
    [
        new HookInstallation(
            ProviderName,
            scope,
            FactoryDroidHooks.ResolveTarget(FactoryDir(directory, scope)).Path,
            HookCommands.Invocation(ProviderName),
            LegacyScriptPath: null,
            HookPayloadKind.FactoryDroid)
    ];

    /// <inheritdoc/>
    public Task<IntegrationResult> IntegrateAsync(string directory, bool force, CancellationToken cancellationToken) =>
        IntegrateCoreAsync(directory, HookScope.Project, force, cancellationToken);

    /// <inheritdoc/>
    public Task<IntegrationResult> IntegrateGlobalAsync(bool force, CancellationToken cancellationToken) =>
        IntegrateCoreAsync(home.Home, HookScope.Global, force, cancellationToken);

    /// <inheritdoc/>
    public IReadOnlyList<string> SharedArtifactPaths(string directory, HookScope scope) =>
        [InstructionsPath(directory, scope), SharedInstructionArtifacts.SkillPath(SkillsDirectory(directory, scope))];

    /// <inheritdoc/>
    /// <remarks>Removes dtk's entry from every file Droid reads hooks from, wherever an earlier run or the user put it.</remarks>
    public async Task<IntegrationResult> UninstallAsync(
        string directory, HookScope scope, IReadOnlyDictionary<string, string> sharedInUse, CancellationToken cancellationToken)
    {
        var root = scope == HookScope.Global ? home.Home : directory;
        var context = IntegrationContext.ForUninstall(root, sharedInUse);

        await SharedInstructionArtifacts.RemoveAgentsFilesAsync(
            InstructionsPath(root, scope), SkillsDirectory(root, scope), context, cancellationToken).ConfigureAwait(false);

        foreach (var (path, containerKey) in FactoryDroidHooks.Candidates(FactoryDir(root, scope)))
        {
            try
            {
                await UninstallHelpers.RemoveHookRegistrationAsync(Registration(path, containerKey), context, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (InvalidOperationException)
            {
                // Malformed JSON in one candidate must not stop dtk from removing its hook from the others: the
                // loop's remaining files may be where the hook actually lives.
                UninstallHelpers.Keep(path, "it is not valid JSON, so dtk could not remove its hook entry from it", context);
            }
        }

        rtk.NoteRemainingExclusion(context, RtkHookCoexistence.IsRtkRewriteReferencedIn(RtkCandidates(root, scope)));

        return context.ToResult();
    }

    private static HookRegistrationSpec Registration(string path, string? containerKey) =>
        new(path, FactoryDroidHooks.EventKey, ShellTool, HookCommands.Invocation("droid"), HookTimeoutSeconds,
            ContainerKey: containerKey);

    private string FactoryDir(string directory, HookScope scope) =>
        scope == HookScope.Global ? home.FactoryDir : Path.Combine(directory, ".factory");

    private string InstructionsPath(string directory, HookScope scope) =>
        Path.Combine(scope == HookScope.Global ? home.FactoryDir : directory, "AGENTS.md");

    private string SkillsDirectory(string directory, HookScope scope) =>
        scope == HookScope.Global ? home.AgentsSkillsDir : Path.Combine(directory, ".agents", "skills");

    /// <summary>Where rtk registers itself for Droid: every candidate file in this scope and in the user's home.</summary>
    /// <param name="directory">The project root, or the home directory for a global run.</param>
    /// <param name="scope">Which scope this run is installing into.</param>
    private List<string> RtkCandidates(string directory, HookScope scope) =>
        [.. FactoryDroidHooks.Candidates(FactoryDir(directory, scope))
            .Concat(FactoryDroidHooks.Candidates(home.FactoryDir))
            .Select(candidate => candidate.Path)
            .Distinct(StringComparer.Ordinal)];

    private async Task<IntegrationResult> IntegrateCoreAsync(
        string directory, HookScope scope, bool force, CancellationToken cancellationToken)
    {
        var context = new IntegrationContext(force);

        await SharedInstructionArtifacts.WriteAgentsFilesAsync(
            InstructionsPath(directory, scope), SkillsDirectory(directory, scope), context, cancellationToken)
            .ConfigureAwait(false);

        var factoryDir = FactoryDir(directory, scope);
        var (path, containerKey) = FactoryDroidHooks.ResolveTarget(factoryDir);
        await IntegratorHelpers.WriteHookRegistrationAsync(Registration(path, containerKey), context, cancellationToken)
            .ConfigureAwait(false);

        var wroteHook = context.Created.Contains(path) || context.Updated.Contains(path);
        wroteHook |= await RemoveFromOtherCandidatesAsync(factoryDir, path, context, cancellationToken).ConfigureAwait(false);

        if (wroteHook)
        {
            context.Notes.Add(SessionNote);
        }

        var rtkOutcome = await rtk.ReconcileFilesAsync(RtkCandidates(directory, scope), cancellationToken).ConfigureAwait(false);
        rtkOutcome.ApplyTo(context);

        return context.ToResult();
    }

    /// <summary>
    /// Removes dtk's handler from every candidate but <paramref name="target"/>, so the hook is registered once, where
    /// Droid reads <c>PreToolUse</c> from: an earlier install may have put it in a file Droid no longer reads it from.
    /// A candidate left empty is deleted. One dtk cannot rewrite is kept, with a note, when it may hold dtk's handler.
    /// </summary>
    /// <param name="factoryDir">The <c>.factory</c> directory being installed into.</param>
    /// <param name="target">The file this run registered the hook in.</param>
    /// <param name="context">The install context; receives the files updated, removed or kept.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Whether any candidate was changed.</returns>
    private static async Task<bool> RemoveFromOtherCandidatesAsync(
        string factoryDir, string target, IntegrationContext context, CancellationToken cancellationToken)
    {
        var changed = false;

        foreach (var (path, containerKey) in FactoryDroidHooks.Candidates(factoryDir))
        {
            if (string.Equals(path, target, StringComparison.Ordinal) || !File.Exists(path))
            {
                continue;
            }

            // A scratch context: a candidate without dtk's handler is not part of this install's report.
            var removal = new IntegrationContext(context.Force);
            try
            {
                await UninstallHelpers.RemoveHookRegistrationAsync(Registration(path, containerKey), removal, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (InvalidOperationException ex)
            {
                // Like uninstall, one unreadable candidate must not stop the install. It is reported only when it may
                // hold dtk's handler: a settings.json with comments and no dtk hook is none of this run's business.
                if (!FactoryDroidHooks.IsReadable(path) || FactoryDroidHooks.HoldsDtkHandler(path, containerKey))
                {
                    context.Skipped.Add(path);
                    context.Notes.Add(
                        $"{path} was kept: dtk could not check it for an older registration of its hook ({ex.Message}). "
                        + $"Remove any '{HookCommands.Invocation("droid")}' entry from it by hand.");
                }

                continue;
            }

            context.Updated.AddRange(removal.Updated);
            context.Removed.AddRange(removal.Removed);
            context.Skipped.AddRange(removal.Skipped);
            context.Notes.AddRange(removal.Notes);
            changed |= removal.Updated.Count > 0 || removal.Removed.Count > 0;
        }

        return changed;
    }
}
