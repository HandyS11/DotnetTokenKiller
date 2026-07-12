using DotnetTokenKiller.Domain.Integration;

namespace DotnetTokenKiller.Application.Integration;

/// <summary>Installs dtk integration artifacts for JetBrains AI (Junie).</summary>
/// <remarks>
/// Creates:
/// <list type="bullet">
///   <item><description><c>.junie/guidelines.md</c> (JetBrains AI project guidelines)</description></item>
/// </list>
/// </remarks>
public sealed class JetBrainsAiIntegrator : IProviderIntegrator
{
    private const string SectionMarker = "<!-- dtk -->";
    private const string SectionEndMarker = "<!-- /dtk -->";

    private const string GuidelinesSection =
        $"""
        {SectionMarker}
        ## DotnetTokenKiller (dtk)

        {IntegrationInstructions.Markdown}
        {SectionEndMarker}
        """;

    /// <inheritdoc/>
    public string ProviderName => "jetbrains";

    /// <inheritdoc/>
    public async Task<IntegrationResult> IntegrateAsync(
        string directory,
        bool force,
        CancellationToken cancellationToken)
    {
        var context = new IntegrationContext(force);

        await IntegratorHelpers.WriteSectionBasedFileAsync(
            Path.Combine(directory, ".junie", "guidelines.md"),
            SectionMarker, SectionEndMarker, GuidelinesSection,
            context, cancellationToken).ConfigureAwait(false);

        return context.ToResult();
    }
}
