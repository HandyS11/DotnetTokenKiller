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
