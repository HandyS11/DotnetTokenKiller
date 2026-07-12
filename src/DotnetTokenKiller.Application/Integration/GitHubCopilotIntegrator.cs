using DotnetTokenKiller.Domain.Integration;

namespace DotnetTokenKiller.Application.Integration;

/// <summary>Installs dtk integration artifacts for GitHub Copilot.</summary>
/// <remarks>
/// Appends (or creates) a dtk section in <c>.github/copilot-instructions.md</c>.
/// </remarks>
public sealed class GitHubCopilotIntegrator : IProviderIntegrator
{
    private const string SectionMarker = "<!-- dtk -->";
    private const string SectionEndMarker = "<!-- /dtk -->";

    private const string CopilotSection =
        $"""
        {SectionMarker}
        ## DotnetTokenKiller (dtk)

        {IntegrationInstructions.Markdown}
        {SectionEndMarker}
        """;

    /// <inheritdoc/>
    public string ProviderName => "copilot";

    /// <inheritdoc/>
    public async Task<IntegrationResult> IntegrateAsync(
        string directory,
        bool force,
        CancellationToken cancellationToken)
    {
        var context = new IntegrationContext(force);

        await IntegratorHelpers.WriteSectionBasedFileAsync(
            Path.Combine(directory, ".github", "copilot-instructions.md"),
            SectionMarker, SectionEndMarker, CopilotSection,
            context, cancellationToken).ConfigureAwait(false);

        return context.ToResult();
    }
}
