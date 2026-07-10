using DotnetTokenKiller.Domain.Integration;

namespace DotnetTokenKiller.Application.Integration;

/// <summary>Installs dtk integration artifacts for Gemini CLI.</summary>
/// <remarks>
/// Creates:
/// <list type="bullet">
///   <item><description><c>GEMINI.md</c> (project root, section-based merge)</description></item>
///   <item><description><c>.gemini/hooks/dotnet-to-dtk.py</c></description></item>
///   <item><description><c>.gemini/settings.json</c> (merged, never overwritten)</description></item>
/// </list>
/// </remarks>
public sealed class GeminiCliIntegrator : IProviderIntegrator
{
    private const string SectionMarker = "<!-- dtk -->";

    private const string HookCommand = "python3 .gemini/hooks/dotnet-to-dtk.py";

    private const string GeminiSection =
        """
        <!-- dtk -->
        ## DotnetTokenKiller (dtk)

        Use `dtk` instead of raw `dotnet` for build, test, restore, clean, and format commands.
        `dtk` filters output to actionable signal only, reducing noise by 50-97%.

        ```sh
        dtk dotnet build MyProject.slnx
        dtk dotnet test --filter "Category=Unit"
        dtk dotnet restore
        dtk dotnet clean
        dtk dotnet format
        dtk dotnet format --verify-no-changes
        ```

        - All arguments and flags are forwarded to `dotnet` unchanged.
        - Exit codes are preserved — CI pipelines work correctly.
        - Unknown subcommands (e.g. `run`, `publish`) pass through to `dotnet` unchanged.
        <!-- /dtk -->
        """;

    /// <inheritdoc/>
    public string ProviderName => "gemini";

    /// <inheritdoc/>
    public async Task<IntegrationResult> IntegrateAsync(
        string directory,
        bool force,
        CancellationToken cancellationToken)
    {
        var context = new IntegrationContext(force);

        await IntegratorHelpers.WriteSectionBasedFileAsync(
            Path.Combine(directory, "GEMINI.md"),
            SectionMarker, "<!-- /dtk -->", GeminiSection,
            context, cancellationToken).ConfigureAwait(false);

        await IntegratorHelpers.WriteHookAndSettingsAsync(
            new HookSpec(
                Path.Combine(directory, ".gemini", "hooks", "dotnet-to-dtk.py"),
                HookScriptTemplates.GeminiHook,
                Path.Combine(directory, ".gemini", "settings.json"),
                "BeforeTool",
                "run_shell_command",
                HookCommand),
            context, cancellationToken).ConfigureAwait(false);

        return context.ToResult();
    }
}
