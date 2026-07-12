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
    private const string SectionEndMarker = "<!-- /dtk -->";

    /// <summary>
    /// Quoted and rooted at <c>$GEMINI_PROJECT_DIR</c> (the absolute project root Gemini CLI
    /// exports to hooks) so the hook resolves correctly regardless of the CLI's current working
    /// directory.
    /// </summary>
    private const string HookCommand = """python3 "$GEMINI_PROJECT_DIR"/.gemini/hooks/dotnet-to-dtk.py""";

    private const string GeminiSection =
        $"""
        {SectionMarker}
        ## DotnetTokenKiller (dtk)

        {IntegrationInstructions.Markdown}

        The `BeforeTool` hook shells out to `python3`; on Windows (where the launcher is usually
        `python`, not `python3`), edit the hook command in `.gemini/settings.json` if it doesn't fire.
        {SectionEndMarker}
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
            SectionMarker, SectionEndMarker, GeminiSection,
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
