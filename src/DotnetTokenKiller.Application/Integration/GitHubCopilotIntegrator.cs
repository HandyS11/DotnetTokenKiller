using DotnetTokenKiller.Domain.Integration;

namespace DotnetTokenKiller.Application.Integration;

/// <summary>Installs dtk integration artifacts for GitHub Copilot.</summary>
/// <remarks>
/// Appends (or creates) a dtk section in <c>.github/copilot-instructions.md</c>.
/// </remarks>
public sealed class GitHubCopilotIntegrator : IProviderIntegrator, IUninstallIntegrator
{
    private const string SectionMarker = "<!-- dtk -->";
    private const string SectionEndMarker = "<!-- /dtk -->";

    private static readonly string CopilotSection =
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
            InstructionsPath(directory),
            SectionMarker, SectionEndMarker, CopilotSection,
            context, cancellationToken).ConfigureAwait(false);

        return context.ToResult();
    }

    /// <inheritdoc/>
    IReadOnlyList<string> IUninstallIntegrator.SharedArtifactPaths(string directory, HookScope scope) => [InstructionsPath(directory)];

    /// <inheritdoc/>
    async Task<IntegrationResult> IUninstallIntegrator.UninstallAsync(
        string directory, HookScope scope, IReadOnlyDictionary<string, string> sharedInUse, CancellationToken cancellationToken)
    {
        var context = IntegrationContext.ForUninstall(directory, sharedInUse);

        await UninstallHelpers.RemoveSectionAsync(
            InstructionsPath(directory), SectionMarker, SectionEndMarker, context, cancellationToken).ConfigureAwait(false);

        return context.ToResult();
    }

    private static string InstructionsPath(string directory) => Path.Combine(directory, ".github", "copilot-instructions.md");
}
