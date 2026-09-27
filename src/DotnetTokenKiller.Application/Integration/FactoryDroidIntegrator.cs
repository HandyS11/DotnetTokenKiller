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
    : AgentsFileIntegrator(home), IHookApprovalInspector
{
    /// <summary>Printed when this run wrote the hook: Droid snapshots hooks when a session starts.</summary>
    internal const string SessionNote =
        "Droid reads hooks when a session starts: restart any running droid session for the hook to take effect.";

    /// <summary>Seconds Droid waits for the hook.</summary>
    private const int HookTimeoutSeconds = 10;

    /// <summary>Droid's shell tool, which the hook's matcher selects.</summary>
    private const string ShellTool = "Execute";

    /// <summary>The name of the finding that reports whether Droid reads dtk's hook from where it is registered.</summary>
    private const string HookLocationCheck = "hook location";

    /// <inheritdoc/>
    public override string ProviderName => "droid";

    /// <inheritdoc/>
    protected override string GlobalInstructionsDirectory => Home.FactoryDir;

    /// <inheritdoc/>
    /// <remarks>
    /// Points at the candidate that holds dtk's handler — the resolved target when it does, else the first candidate that
    /// does — so doctor checks the hook that is actually registered; at the resolved target when none holds it.
    /// </remarks>
    public override IReadOnlyList<HookInstallation> DescribeHooks(string directory, HookScope scope)
    {
        var factoryDir = FactoryDir(directory, scope);
        var target = FactoryDroidHooks.ResolveTarget(factoryDir);
        var holder = FactoryDroidHooks.HoldsDtkHandler(target.Path, target.ContainerKey)
            ? target
            : FactoryDroidHooks.Candidates(factoryDir)
                .FirstOrDefault(candidate => FactoryDroidHooks.HoldsDtkHandler(candidate.Path, candidate.ContainerKey), target);

        return
        [
            new HookInstallation(
                ProviderName,
                scope,
                holder.Path,
                HookCommands.Invocation(ProviderName),
                LegacyScriptPath: null,
                HookPayloadKind.FactoryDroid)
        ];
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Droid runs the hook only from the file it reads <c>PreToolUse</c> from, and a <c>hooks.json</c> holding only dtk's
    /// hook shadows the user's <c>settings.json</c> ones, so this reports a failing <c>hook location</c> whenever dtk's
    /// hook is anywhere but the file <see cref="FactoryDroidHooks.ResolveTarget"/> picks; nothing when it is there.
    /// </remarks>
    public IReadOnlyList<HookApprovalFinding> InspectApproval(HookInstallation installation, string projectDirectory)
    {
        ArgumentNullException.ThrowIfNull(installation);

        var factoryDir = FactoryDir(projectDirectory, installation.Scope);
        var target = FactoryDroidHooks.ResolveTarget(factoryDir).Path;
        var registered = installation.RegistrationPath;
        if (string.Equals(registered, target, StringComparison.Ordinal))
        {
            return [];
        }

        var remedy = installation.Scope == HookScope.Global ? $"dtk init {ProviderName} --global" : $"dtk init {ProviderName}";
        var found = string.Equals(registered, FactoryDroidHooks.LiveHooksJson(factoryDir), StringComparison.Ordinal)
            ? $"dtk's droid hook in {registered} shadows the PreToolUse hooks in {target}"
            : $"dtk's droid hook is in {registered}, but Droid reads PreToolUse from {target}";

        return [new HookApprovalFinding(HookLocationCheck, false, $"{found}: run '{remedy}' to move it")];
    }

    /// <inheritdoc/>
    /// <remarks>Removes dtk's entry from every file Droid reads hooks from, wherever an earlier run or the user put it.</remarks>
    protected override async Task UninstallHookAsync(
        string directory, HookScope scope, IntegrationContext context, CancellationToken cancellationToken)
    {
        foreach (var (path, containerKey) in FactoryDroidHooks.Candidates(FactoryDir(directory, scope)))
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
                UninstallHelpers.Keep(
                    path,
                    FactoryDroidHooks.IsReadable(path)
                        ? "it has comments or trailing commas, which dtk cannot rewrite without losing them, so dtk did not "
                          + "remove its hook entry from it"
                        : "it is not valid JSON, so dtk could not remove its hook entry from it",
                    context);
            }
        }

        rtk.NoteRemainingExclusion(context, RtkHookCoexistence.IsRtkRewriteReferencedIn(RtkCandidates(directory, scope)));
    }

    private static HookRegistrationSpec Registration(string path, string? containerKey) =>
        new(path, FactoryDroidHooks.EventKey, ShellTool, HookCommands.Invocation("droid"), HookTimeoutSeconds,
            ContainerKey: containerKey);

    private string FactoryDir(string directory, HookScope scope) =>
        scope == HookScope.Global ? Home.FactoryDir : Path.Combine(directory, ".factory");

    /// <summary>Where rtk registers itself for Droid: every candidate file in this scope and in the user's home.</summary>
    /// <param name="directory">The project root, or the home directory for a global run.</param>
    /// <param name="scope">Which scope this run is installing into.</param>
    private List<string> RtkCandidates(string directory, HookScope scope) =>
        [.. FactoryDroidHooks.Candidates(FactoryDir(directory, scope))
            .Concat(FactoryDroidHooks.Candidates(Home.FactoryDir))
            .Select(candidate => candidate.Path)
            .Distinct(StringComparer.Ordinal)];

    /// <inheritdoc/>
    protected override async Task InstallHookAsync(
        string directory, HookScope scope, IntegrationContext context, CancellationToken cancellationToken)
    {
        var factoryDir = FactoryDir(directory, scope);
        var (path, containerKey) = FactoryDroidHooks.ResolveTarget(factoryDir);
        await IntegratorHelpers.WriteHookRegistrationAsync(Registration(path, containerKey), context, cancellationToken)
            .ConfigureAwait(false);

        var wroteHook = Wrote(context, path);
        wroteHook |= await RemoveFromOtherCandidatesAsync(factoryDir, path, context, cancellationToken).ConfigureAwait(false);

        if (wroteHook)
        {
            context.Notes.Add(SessionNote);
        }

        var rtkOutcome = await rtk.ReconcileFilesAsync(RtkCandidates(directory, scope), cancellationToken).ConfigureAwait(false);
        rtkOutcome.ApplyTo(context);
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
