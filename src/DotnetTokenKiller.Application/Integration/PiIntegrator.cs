namespace DotnetTokenKiller.Application.Integration;

/// <summary>Installs dtk integration artifacts for the pi coding agent.</summary>
/// <param name="rtk">Detects and reconciles an rtk extension so dtk owns dotnet commands.</param>
/// <param name="home">Resolves the user's home and pi's agent directory.</param>
/// <remarks>
/// pi 0.73 and later load <c>.pi/extensions</c> only in a trusted project, and print, JSON and RPC modes skip it until
/// then. dtk never reads or writes pi's <c>trust.json</c>, so a project install and doctor only say so.
/// </remarks>
internal sealed class PiIntegrator(RtkHookCoexistence rtk, HomePaths home)
    : PiFamilyIntegrator(rtk, home), IHookApprovalInspector
{
    /// <summary>What a project-scope install and doctor tell the user about pi's project trust.</summary>
    internal const string TrustNote =
        "pi loads .pi/extensions only in a trusted project: approve it when pi asks, or run /trust. "
        + "Print, JSON and RPC modes (pi -p) skip it until then; 'dtk init pi --global' avoids the prompt.";

    /// <inheritdoc/>
    public override string ProviderName => "pi";

    /// <inheritdoc/>
    protected override string HarnessName => "pi";

    /// <inheritdoc/>
    protected override string ProjectFolder => ".pi";

    /// <inheritdoc/>
    protected override HookPayloadKind PayloadKind => HookPayloadKind.Pi;

    /// <inheritdoc/>
    protected override string GlobalAgentDirectory(HomePaths paths) => paths.PiAgentDir;

    /// <inheritdoc/>
    protected override void AddNotes(HookScope scope, IntegrationContext context)
    {
        if (scope == HookScope.Project)
        {
            context.Notes.Add(TrustNote);
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<HookApprovalFinding> InspectApproval(HookInstallation installation, string projectDirectory) =>
        installation.Scope == HookScope.Project ? [new HookApprovalFinding("project trust", true, TrustNote)] : [];
}
