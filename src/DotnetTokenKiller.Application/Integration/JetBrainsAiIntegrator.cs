using DotnetTokenKiller.Domain.Integration;

namespace DotnetTokenKiller.Application.Integration;

/// <summary>Installs dtk integration artifacts for JetBrains AI (Junie).</summary>
/// <remarks>
/// Creates:
/// <list type="bullet">
///   <item><description><c>.junie/guidelines.md</c> (JetBrains AI project guidelines)</description></item>
/// </list>
/// </remarks>
public sealed class JetBrainsAiIntegrator : IProviderIntegrator, IUninstallIntegrator
{
    private const string SectionMarker = "<!-- dtk -->";
    private const string SectionEndMarker = "<!-- /dtk -->";

    private static readonly string GuidelinesSection =
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
            GuidelinesPath(directory),
            SectionMarker, SectionEndMarker, GuidelinesSection,
            context, cancellationToken).ConfigureAwait(false);

        return context.ToResult();
    }

    /// <inheritdoc/>
    IReadOnlyList<string> IUninstallIntegrator.SharedArtifactPaths(string directory, HookScope scope) => [];

    /// <inheritdoc/>
    async Task<IntegrationResult> IUninstallIntegrator.UninstallAsync(
        string directory, HookScope scope, IReadOnlyDictionary<string, string> sharedInUse, CancellationToken cancellationToken)
    {
        var context = IntegrationContext.ForUninstall(directory, sharedInUse);

        await UninstallHelpers.RemoveSectionAsync(
            GuidelinesPath(directory), SectionMarker, SectionEndMarker, context, cancellationToken).ConfigureAwait(false);

        return context.ToResult();
    }

    private static string GuidelinesPath(string directory) => Path.Combine(directory, ".junie", "guidelines.md");
}
