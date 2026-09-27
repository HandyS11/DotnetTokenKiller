using DotnetTokenKiller.Application.Integration.Hooks;
using DotnetTokenKiller.Domain.Integration;

namespace DotnetTokenKiller.Application.Integration;

/// <summary>
/// Installs dtk for a harness of the pi family: the shared <c>AGENTS.md</c> section and skill, and a generated
/// <c>extensions/dtk.js</c> (see <see cref="PiExtension"/>).
/// </summary>
/// <param name="rtk">Detects and reconciles an rtk extension so dtk owns dotnet commands.</param>
/// <param name="home">Resolves the user's home and the harnesses' agent directories.</param>
internal abstract class PiFamilyIntegrator(RtkHookCoexistence rtk, HomePaths home) : AgentsFileIntegrator(home)
{
    private const string ExtensionsFolder = "extensions";

    /// <summary>Gets the harness's display name, for the generated file's comments.</summary>
    protected abstract string HarnessName { get; }

    /// <summary>Gets the harness's project config folder, e.g. <c>.pi</c>.</summary>
    protected abstract string ProjectFolder { get; }

    /// <summary>Gets the payload kind the probe sends.</summary>
    protected abstract HookPayloadKind PayloadKind { get; }

    /// <summary>Gets the harness's global agent directory.</summary>
    /// <param name="paths">The home paths to resolve against.</param>
    protected abstract string GlobalAgentDirectory(HomePaths paths);

    /// <summary>Adds scope-specific notes after a successful install.</summary>
    /// <param name="scope">The scope installed.</param>
    /// <param name="context">The integration context.</param>
    protected virtual void AddNotes(HookScope scope, IntegrationContext context)
    {
    }

    /// <inheritdoc/>
    protected override string GlobalInstructionsDirectory => GlobalAgentDirectory(Home);

    /// <inheritdoc/>
    public override IReadOnlyList<HookInstallation> DescribeHooks(string directory, HookScope scope)
    {
        var path = Path.Combine(ConfigDirectory(directory, scope), ExtensionsFolder, "dtk.js");

        return
        [
            new HookInstallation(
                ProviderName,
                scope,
                path,
                HookCommands.Invocation(ProviderName),
                LegacyScriptPath: null,
                PayloadKind,
                PiExtension.Artifact(path, ProviderName, HarnessName))
        ];
    }

    /// <inheritdoc/>
    protected override async Task InstallHookAsync(
        string directory, HookScope scope, IntegrationContext context, CancellationToken cancellationToken)
    {
        await IntegratorHelpers.WriteGeneratedFileAsync(DescribeHooks(directory, scope)[0].PluginArtifact!, context, cancellationToken)
            .ConfigureAwait(false);

        var rtkOutcome = await rtk.ReconcileFilesAsync(RtkCandidates(directory), cancellationToken).ConfigureAwait(false);
        rtkOutcome.ApplyTo(context);

        AddNotes(scope, context);
    }

    /// <inheritdoc/>
    protected override async Task UninstallHookAsync(
        string directory, HookScope scope, IntegrationContext context, CancellationToken cancellationToken)
    {
        await UninstallHelpers.RemoveGeneratedFileAsync(DescribeHooks(directory, scope)[0].PluginArtifact!, context, cancellationToken)
            .ConfigureAwait(false);

        rtk.NoteRemainingExclusion(context, RtkHookCoexistence.IsRtkRewriteReferencedIn(RtkCandidates(directory)));
    }

    private string ConfigDirectory(string directory, HookScope scope) =>
        scope == HookScope.Global ? GlobalAgentDirectory(Home) : Path.Combine(directory, ProjectFolder);

    /// <summary>
    /// Every file either pi-family harness would load as an extension, in both scopes: rtk installs its pi extension
    /// in pi's folders, and a user may copy it into oh-my-pi's.
    /// </summary>
    /// <param name="directory">The project root.</param>
    private List<string> RtkCandidates(string directory) =>
    [
        .. new[]
            {
                Path.Combine(directory, ".pi", ExtensionsFolder),
                Path.Combine(Home.PiAgentDir, ExtensionsFolder),
                Path.Combine(directory, ".omp", ExtensionsFolder),
                Path.Combine(Home.OhMyPiAgentDir, ExtensionsFolder)
            }
            .Where(Directory.Exists)
            .SelectMany(folder => IntegratorHelpers.EnumerateSafely(folder, Directory.EnumerateFiles))
    ];
}
