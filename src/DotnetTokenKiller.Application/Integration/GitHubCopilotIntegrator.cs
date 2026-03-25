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
        """
        <!-- dtk -->
        ## DotnetTokenKiller (dtk)

        Use `dtk` instead of raw `dotnet` for build, test, restore, and clean commands.
        `dtk` filters output to actionable signal only, reducing noise by 50-97%.

        ```sh
        dtk dotnet build MyProject.slnx
        dtk dotnet test --filter "Category=Unit"
        dtk dotnet restore
        dtk dotnet clean
        ```

        - All arguments and flags are forwarded to `dotnet` unchanged.
        - Exit codes are preserved — CI pipelines work correctly.
        - Unknown subcommands (e.g. `run`, `publish`) pass through to `dotnet` unchanged.
        <!-- /dtk -->
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
