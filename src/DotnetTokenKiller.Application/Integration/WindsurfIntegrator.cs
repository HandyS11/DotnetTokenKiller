using DotnetTokenKiller.Domain.Integration;

namespace DotnetTokenKiller.Application.Integration;

/// <summary>Installs dtk integration artifacts for Windsurf.</summary>
/// <remarks>
/// Creates:
/// <list type="bullet">
///   <item><description><c>.windsurf/rules/dtk.md</c> (Windsurf project rule)</description></item>
/// </list>
/// </remarks>
public sealed class WindsurfIntegrator : IProviderIntegrator, IUninstallIntegrator
{
    private static readonly string WindsurfRule =
        $"""
        # DotnetTokenKiller (dtk)

        {IntegrationInstructions.Intro}

        ## Usage

        {IntegrationInstructions.UsageBody}
        """;

    /// <inheritdoc/>
    public string ProviderName => "windsurf";

    /// <inheritdoc/>
    public async Task<IntegrationResult> IntegrateAsync(
        string directory,
        bool force,
        CancellationToken cancellationToken)
    {
        var context = new IntegrationContext(force);

        await IntegratorHelpers.WriteFileAsync(RulePath(directory), WindsurfRule, context, cancellationToken).ConfigureAwait(false);

        return context.ToResult();
    }

    /// <inheritdoc/>
    IReadOnlyList<string> IUninstallIntegrator.SharedArtifactPaths(string directory, HookScope scope) => [];

    /// <inheritdoc/>
    async Task<IntegrationResult> IUninstallIntegrator.UninstallAsync(
        string directory, HookScope scope, IReadOnlyDictionary<string, string> sharedInUse, CancellationToken cancellationToken)
    {
        var context = IntegrationContext.ForUninstall(directory, sharedInUse);

        await UninstallHelpers.RemoveOwnedFileAsync(RulePath(directory), WindsurfRule, context, cancellationToken).ConfigureAwait(false);

        return context.ToResult();
    }

    private static string RulePath(string directory) => Path.Combine(directory, ".windsurf", "rules", "dtk.md");
}
