namespace DotnetTokenKiller.Application.Integration;

/// <summary>Installs dtk integration artifacts for oh-my-pi (<c>omp</c>), a fork of pi with its own directories.</summary>
/// <param name="rtk">Detects and reconciles an rtk extension so dtk owns dotnet commands.</param>
/// <param name="home">Resolves the user's home and oh-my-pi's agent directory.</param>
/// <remarks>
/// oh-my-pi's shell minimizer has its own dotnet filter, selected by program name; once the command is
/// <c>dtk dotnet …</c> it no longer applies, so dtk leaves oh-my-pi's <c>config.yml</c> alone.
/// </remarks>
internal sealed class OhMyPiIntegrator(RtkHookCoexistence rtk, HomePaths home) : PiFamilyIntegrator(rtk, home)
{
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
}
