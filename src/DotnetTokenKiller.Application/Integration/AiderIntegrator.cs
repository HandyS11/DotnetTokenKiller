using DotnetTokenKiller.Domain.Integration;

namespace DotnetTokenKiller.Application.Integration;

/// <summary>Installs dtk integration artifacts for Aider.</summary>
/// <remarks>
/// Creates:
/// <list type="bullet">
///   <item><description><c>.aider-dtk-instructions.md</c> (instructions file read into context)</description></item>
///   <item><description><c>.aider.conf.yml</c> (Aider configuration, section-based merge)</description></item>
/// </list>
/// </remarks>
public sealed class AiderIntegrator : IProviderIntegrator
{
    private const string SectionMarker = "# dtk";
    private const string SectionEndMarker = "# /dtk";

    private const string AiderConfSection =
        """
        # dtk
        # DotnetTokenKiller: use dtk instead of dotnet for build/test/restore/clean.
        read:
          - .aider-dtk-instructions.md
        # /dtk
        """;

    private const string InstructionsMarkdown =
        """
        # DotnetTokenKiller (dtk)

        Use `dtk` instead of raw `dotnet` for build, test, restore, and clean commands.
        `dtk` filters output to actionable signal only, reducing noise by 50-97%.

        ## Usage

        ```sh
        dtk dotnet build MyProject.slnx
        dtk dotnet test --filter "Category=Unit"
        dtk dotnet restore
        dtk dotnet clean
        ```

        - All arguments and flags are forwarded to `dotnet` unchanged.
        - Exit codes are preserved — CI pipelines work correctly.
        - Unknown subcommands (e.g. `run`, `publish`) pass through to `dotnet` unchanged.
        """;

    /// <inheritdoc/>
    public string ProviderName => "aider";

    /// <inheritdoc/>
    public async Task<IntegrationResult> IntegrateAsync(
        string directory,
        bool force,
        CancellationToken cancellationToken)
    {
        var context = new IntegrationContext(force);

        await IntegratorHelpers.WriteFileAsync(
            Path.Combine(directory, ".aider-dtk-instructions.md"),
            InstructionsMarkdown, context, cancellationToken).ConfigureAwait(false);

        await IntegratorHelpers.WriteSectionBasedFileAsync(
            Path.Combine(directory, ".aider.conf.yml"),
            SectionMarker, SectionEndMarker, AiderConfSection,
            context, cancellationToken).ConfigureAwait(false);

        return context.ToResult();
    }
}
