using DotnetTokenKiller.Application.Integration.Hooks;
using DotnetTokenKiller.Domain.Integration;

namespace DotnetTokenKiller.Application.Integration;

/// <summary>Installs dtk integration artifacts for Crush.</summary>
/// <param name="home">Resolves the user's home and Crush config directories for global integration.</param>
/// <remarks>
/// Creates, in a project: the shared <c>AGENTS.md</c> section and <c>.agents/skills</c> skill, and dtk's section in
/// <c>.crushrc</c> (or in an existing <c>crushrc</c> when the project has no <c>.crushrc</c>) registering
/// <c>dtk hook crush</c> for the <c>bash</c> tool. Globally: dtk's section in <c>crushrc</c> and in <c>CRUSH.md</c>
/// under <see cref="HomePaths.CrushConfigDir"/>, and the skill in <c>~/.agents/skills</c>.
/// Internal for the same reason as <see cref="ClaudeCodeIntegrator"/>: its constructor takes internal types.
/// </remarks>
internal sealed class CrushIntegrator(HomePaths home)
    : IProviderIntegrator, IGlobalIntegrator, IHookIntegrator, IUninstallIntegrator
{
    /// <summary>The first Crush release that reads <c>crushrc</c> with <c>hook add</c>; pinned by gate C.</summary>
    internal const string MinimumCrushVersion = "0.88.0";

    /// <summary>Printed when this run wrote the hook: older Crush releases do not read <c>crushrc</c>.</summary>
    internal const string VersionNote =
        "The hook is registered in crushrc, which Crush reads from version " + MinimumCrushVersion + " on; update Crush if it is older.";

    /// <summary>Printed when this run wrote the hook.</summary>
    internal const string SubagentNote =
        "Crush runs hooks for the main agent's tool calls only, so a sub-agent's dotnet commands run as written.";

    /// <inheritdoc/>
    public string ProviderName => "crush";

    /// <inheritdoc/>
    public IReadOnlyList<HookInstallation> DescribeHooks(string directory, HookScope scope) =>
    [
        new HookInstallation(
            ProviderName,
            scope,
            RcPath(directory, scope),
            HookCommands.Invocation(ProviderName),
            LegacyScriptPath: null,
            HookPayloadKind.Crush) { IsScriptRegistration = true }
    ];

    /// <inheritdoc/>
    public async Task<IntegrationResult> IntegrateAsync(string directory, bool force, CancellationToken cancellationToken)
    {
        var context = new IntegrationContext(force);
        var rcPath = RcPath(directory, HookScope.Project);

        // Checked before anything is written, so a damaged crushrc leaves no partial install behind.
        CrushrcFile.Validate(rcPath);

        await SharedInstructionArtifacts.WriteAgentsFilesAsync(
            Path.Combine(directory, "AGENTS.md"), Path.Combine(directory, ".agents", "skills"), context, cancellationToken)
            .ConfigureAwait(false);
        await WriteHookAsync(rcPath, context, cancellationToken).ConfigureAwait(false);

        return context.ToResult();
    }

    /// <inheritdoc/>
    public async Task<IntegrationResult> IntegrateGlobalAsync(bool force, CancellationToken cancellationToken)
    {
        var context = new IntegrationContext(force);
        var rcPath = RcPath(home.Home, HookScope.Global);
        CrushrcFile.Validate(rcPath);

        await IntegratorHelpers.WriteSectionBasedFileAsync(
            GlobalInstructionsPath, SharedInstructionArtifacts.SectionMarker, SharedInstructionArtifacts.SectionEndMarker,
            SharedInstructionArtifacts.Section, context, cancellationToken).ConfigureAwait(false);
        await IntegratorHelpers.WriteGeneratedFileAsync(
            SharedInstructionArtifacts.SkillArtifact(home.AgentsSkillsDir), context, cancellationToken).ConfigureAwait(false);
        await WriteHookAsync(rcPath, context, cancellationToken).ConfigureAwait(false);

        return context.ToResult();
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> SharedArtifactPaths(string directory, HookScope scope) =>
        scope == HookScope.Global
            ? [SharedInstructionArtifacts.SkillPath(home.AgentsSkillsDir)]
            : [Path.Combine(directory, "AGENTS.md"), SharedInstructionArtifacts.SkillPath(Path.Combine(directory, ".agents", "skills"))];

    /// <inheritdoc/>
    public async Task<IntegrationResult> UninstallAsync(
        string directory, HookScope scope, IReadOnlyDictionary<string, string> sharedInUse, CancellationToken cancellationToken)
    {
        var root = scope == HookScope.Global ? home.Home : directory;
        var context = IntegrationContext.ForUninstall(root, sharedInUse);

        // Every rc file this uninstall edits is checked first, so a damaged one leaves nothing half removed.
        var rcPaths = scope == HookScope.Global
            ? [RcPath(root, scope)]
            : new[] { Path.Combine(directory, ".crushrc"), Path.Combine(directory, "crushrc") };
        foreach (var rcPath in rcPaths)
        {
            CrushrcFile.Validate(rcPath);
        }

        if (scope == HookScope.Global)
        {
            await UninstallHelpers.RemoveSectionAsync(
                GlobalInstructionsPath, SharedInstructionArtifacts.SectionMarker, SharedInstructionArtifacts.SectionEndMarker,
                context, cancellationToken).ConfigureAwait(false);
            await UninstallHelpers.RemoveGeneratedFileAsync(
                SharedInstructionArtifacts.SkillArtifact(home.AgentsSkillsDir), context, cancellationToken).ConfigureAwait(false);
            await CrushrcFile.RemoveAsync(rcPaths[0], context, cancellationToken).ConfigureAwait(false);
        }
        else
        {
            await SharedInstructionArtifacts.RemoveAgentsFilesAsync(
                Path.Combine(directory, "AGENTS.md"), Path.Combine(directory, ".agents", "skills"), context, cancellationToken)
                .ConfigureAwait(false);
            foreach (var rcPath in rcPaths)
            {
                await CrushrcFile.RemoveAsync(rcPath, context, cancellationToken).ConfigureAwait(false);
            }
        }

        return context.ToResult();
    }

    private string GlobalInstructionsPath => Path.Combine(home.CrushConfigDir, "CRUSH.md");

    /// <summary>
    /// The <c>crushrc</c> dtk registers in: globally <c>crushrc</c> in Crush's config directory; in a project,
    /// whichever of <c>.crushrc</c> or <c>crushrc</c> already holds dtk's section (checked in that order), else
    /// <c>crushrc</c> when it exists and <c>.crushrc</c> does not, else <c>.crushrc</c>.
    /// </summary>
    /// <param name="directory">The project root; ignored when <paramref name="scope"/> is <see cref="HookScope.Global"/>.</param>
    /// <param name="scope">Which scope to resolve the path for.</param>
    private string RcPath(string directory, HookScope scope)
    {
        if (scope == HookScope.Global)
        {
            return Path.Combine(home.CrushConfigDir, "crushrc");
        }

        var dotRc = Path.Combine(directory, ".crushrc");
        var plainRc = Path.Combine(directory, "crushrc");

        if (HasDtkSection(dotRc))
        {
            return dotRc;
        }

        if (HasDtkSection(plainRc))
        {
            return plainRc;
        }

        return !File.Exists(dotRc) && File.Exists(plainRc) ? plainRc : dotRc;
    }

    /// <summary>
    /// Whether <paramref name="path"/> exists, is readable, and already contains dtk's section: with both
    /// <c>.crushrc</c> and <c>crushrc</c> present, the file dtk actually wrote into earlier takes precedence over
    /// the existence-only fallback, so a re-install and doctor's <see cref="DescribeHooks"/> keep pointing at it
    /// instead of orphaning it in favor of the other file.
    /// </summary>
    /// <param name="path">The <c>crushrc</c> candidate to check.</param>
    private static bool HasDtkSection(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            return File.ReadAllText(path).Contains(CrushrcFile.BeginMarker, StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static async Task WriteHookAsync(string rcPath, IntegrationContext context, CancellationToken cancellationToken)
    {
        await CrushrcFile.WriteAsync(rcPath, HookCommands.Invocation("crush"), context, cancellationToken).ConfigureAwait(false);

        if (context.Created.Contains(rcPath) || context.Updated.Contains(rcPath))
        {
            context.Notes.Add(VersionNote);
            context.Notes.Add(SubagentNote);
        }
    }
}
