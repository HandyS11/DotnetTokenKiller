using DotnetTokenKiller.Domain.Integration;

namespace DotnetTokenKiller.Application.Integration;

/// <summary>Installs dtk integration artifacts for Cursor.</summary>
/// <remarks>
/// Creates:
/// <list type="bullet">
///   <item><description><c>.cursor/rules/dtk.mdc</c> (Cursor project rule)</description></item>
/// </list>
/// </remarks>
public sealed class CursorIntegrator : IProviderIntegrator
{
    private static readonly string CursorRule =
        $"""
        ---
        description: Use dtk instead of dotnet for {IntegrationInstructions.SubcommandProse} commands
        globs:
          - "**/*.cs"
          - "**/*.csproj"
          - "**/*.slnx"
          - "**/*.sln"
        alwaysApply: false
        ---

        # DotnetTokenKiller (dtk)

        {IntegrationInstructions.Intro}

        ## Usage

        {IntegrationInstructions.UsageBody}
        """;

    /// <inheritdoc/>
    public string ProviderName => "cursor";

    /// <inheritdoc/>
    public async Task<IntegrationResult> IntegrateAsync(
        string directory,
        bool force,
        CancellationToken cancellationToken)
    {
        var context = new IntegrationContext(force);

        await IntegratorHelpers.WriteFileAsync(
            Path.Combine(directory, ".cursor", "rules", "dtk.mdc"),
            CursorRule, context, cancellationToken).ConfigureAwait(false);

        return context.ToResult();
    }
}
