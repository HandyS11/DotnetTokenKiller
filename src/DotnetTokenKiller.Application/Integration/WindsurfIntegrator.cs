using DotnetTokenKiller.Domain.Integration;

namespace DotnetTokenKiller.Application.Integration;

/// <summary>Installs dtk integration artifacts for Windsurf.</summary>
/// <remarks>
/// Creates:
/// <list type="bullet">
///   <item><description><c>.windsurf/rules/dtk.md</c> (Windsurf project rule)</description></item>
/// </list>
/// </remarks>
public sealed class WindsurfIntegrator : IProviderIntegrator
{
    private const string WindsurfRule =
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

        await IntegratorHelpers.WriteFileAsync(
            Path.Combine(directory, ".windsurf", "rules", "dtk.md"),
            WindsurfRule, context, cancellationToken).ConfigureAwait(false);

        return context.ToResult();
    }
}
