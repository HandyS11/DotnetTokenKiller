using DotnetTokenKiller.Domain.Integration;

namespace DotnetTokenKiller.Application.Integration;

/// <summary>Installs dtk integration artifacts for oh-my-pi (<c>omp</c>), a fork of pi with its own directories.</summary>
/// <param name="rtk">Detects and reconciles an rtk extension so dtk owns dotnet commands.</param>
/// <param name="home">Resolves the user's home and oh-my-pi's agent directory.</param>
/// <remarks>
/// <para>
/// oh-my-pi's shell minimizer has its own dotnet filter, selected by program name; once the command is
/// <c>dtk dotnet …</c> it no longer applies, so dtk leaves oh-my-pi's <c>config.yml</c> alone.
/// </para>
/// <para>
/// With no profile, oh-my-pi honors <c>PI_CODING_AGENT_DIR</c> like pi. When both global agent directories resolve to
/// one, both harnesses load one <c>extensions/dtk.js</c>, which pi's install owns: a global install writes exactly what
/// <c>dtk init pi --global</c> writes, a global uninstall removes nothing, and the global scope describes no hook, so
/// doctor reports the extension once, under pi, and uninstalling pi does not count oh-my-pi as a user of the shared
/// files.
/// </para>
/// </remarks>
internal sealed class OhMyPiIntegrator(RtkHookCoexistence rtk, HomePaths home) : PiFamilyIntegrator(rtk, home)
{
    private readonly HomePaths _home = home;
    private readonly PiIntegrator _pi = new(rtk, home);

    /// <inheritdoc/>
    public override string ProviderName => "oh-my-pi";

    /// <inheritdoc/>
    protected override string HarnessName => "oh-my-pi";

    /// <inheritdoc/>
    protected override string ProjectFolder => ".omp";

    /// <inheritdoc/>
    protected override HookPayloadKind PayloadKind => HookPayloadKind.OhMyPi;

    /// <inheritdoc/>
    protected override string GlobalAgentDirectory(HomePaths paths) => paths.OhMyPiAgentDir;

    /// <inheritdoc/>
    public override IReadOnlyList<HookInstallation> DescribeHooks(string directory, HookScope scope) =>
        scope == HookScope.Global && _home.PiAndOhMyPiShareAgentDir ? [] : base.DescribeHooks(directory, scope);

    /// <inheritdoc/>
    public override async Task<IntegrationResult> IntegrateGlobalAsync(bool force, CancellationToken cancellationToken)
    {
        if (!_home.PiAndOhMyPiShareAgentDir)
        {
            return await base.IntegrateGlobalAsync(force, cancellationToken).ConfigureAwait(false);
        }

        var result = await _pi.IntegrateGlobalAsync(force, cancellationToken).ConfigureAwait(false);
        return result with
        {
            Notes =
            [
                .. result.Notes,
                $"pi and oh-my-pi share {_home.PiAgentDir} (PI_CODING_AGENT_DIR); one extension serves both, "
                + "installed as 'dtk init pi --global'."
            ]
        };
    }

    /// <inheritdoc/>
    public override Task<IntegrationResult> UninstallAsync(
        string directory, HookScope scope, IReadOnlyDictionary<string, string> sharedInUse, CancellationToken cancellationToken)
    {
        if (scope != HookScope.Global || !_home.PiAndOhMyPiShareAgentDir)
        {
            return base.UninstallAsync(directory, scope, sharedInUse, cancellationToken);
        }

        return Task.FromResult(new IntegrationResult(
            [], [], [],
            [
                $"pi and oh-my-pi share {_home.PiAgentDir} (PI_CODING_AGENT_DIR), so nothing was removed; "
                + "run 'dtk init pi --global --uninstall' to remove the extension that serves both."
            ]));
    }
}
