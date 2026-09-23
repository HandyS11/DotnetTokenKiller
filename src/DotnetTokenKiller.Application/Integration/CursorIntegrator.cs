using DotnetTokenKiller.Domain.Integration;

namespace DotnetTokenKiller.Application.Integration;

/// <summary>Installs dtk integration artifacts for Cursor.</summary>
/// <remarks>
/// Creates:
/// <list type="bullet">
///   <item><description><c>.cursor/rules/dtk.mdc</c> (Cursor project rule)</description></item>
/// </list>
/// </remarks>
public sealed class CursorIntegrator : IProviderIntegrator, IUninstallIntegrator
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

        await IntegratorHelpers.WriteFileAsync(RulePath(directory), CursorRule, context, cancellationToken).ConfigureAwait(false);

        return context.ToResult();
    }

    /// <inheritdoc/>
    IReadOnlyList<string> IUninstallIntegrator.SharedArtifactPaths(string directory, HookScope scope) => [];

    /// <inheritdoc/>
    async Task<IntegrationResult> IUninstallIntegrator.UninstallAsync(
        string directory, HookScope scope, IReadOnlyDictionary<string, string> sharedInUse, CancellationToken cancellationToken)
    {
        var context = IntegrationContext.ForUninstall(directory, sharedInUse);

        await UninstallHelpers.RemoveOwnedFileAsync(
            RulePath(directory), CursorRule, IntegrationInstructions.ReleasedCursorRuleHashes, "dtk init cursor", context,
            cancellationToken).ConfigureAwait(false);

        return context.ToResult();
    }

    private static string RulePath(string directory) => Path.Combine(directory, ".cursor", "rules", "dtk.mdc");
}
