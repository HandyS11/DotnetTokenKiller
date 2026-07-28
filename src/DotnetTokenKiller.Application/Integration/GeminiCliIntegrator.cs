using DotnetTokenKiller.Domain.Integration;

namespace DotnetTokenKiller.Application.Integration;

/// <summary>Installs dtk integration artifacts for Gemini CLI.</summary>
/// <param name="home">Resolves the user's home directory for global (home-config) integration.</param>
/// <remarks>
/// Creates:
/// <list type="bullet">
///   <item><description><c>GEMINI.md</c> (project root, section-based merge)</description></item>
///   <item><description><c>.gemini/hooks/dotnet-to-dtk.py</c></description></item>
///   <item><description><c>.gemini/settings.json</c> (merged, never overwritten)</description></item>
/// </list>
/// Declared <see langword="internal"/> (rather than <see langword="public"/>, its original
/// accessibility) because its primary constructor takes the <see langword="internal"/>
/// <see cref="HomePaths"/>: a primary constructor is as accessible as its containing type, and the
/// compiler rejects (CS0051) a public constructor exposing a less-accessible parameter type. See
/// <see cref="ClaudeCodeIntegrator"/> for the same pattern. Callers still reach it polymorphically
/// through the public <see cref="IProviderIntegrator"/> via DI, and tests reach it directly via
/// <c>InternalsVisibleTo</c>.
/// </remarks>
internal sealed class GeminiCliIntegrator(HomePaths home) : IProviderIntegrator, IGlobalIntegrator
{
    private const string SectionMarker = "<!-- dtk -->";
    private const string SectionEndMarker = "<!-- /dtk -->";

    /// <summary>
    /// Quoted and rooted at <c>$GEMINI_PROJECT_DIR</c> (the absolute project root Gemini CLI
    /// exports to hooks) so the hook resolves correctly regardless of the CLI's current working
    /// directory.
    /// </summary>
    private const string HookCommand = """python3 "$GEMINI_PROJECT_DIR"/.gemini/hooks/dotnet-to-dtk.py""";

    /// <summary>
    /// Global variant of <see cref="HookCommand"/>: rooted at <c>$HOME</c> because the hook script is
    /// installed under <c>~/.gemini/hooks</c> (there is no project-scoped env var to anchor to).
    /// </summary>
    private const string GlobalHookCommand = """python3 "$HOME"/.gemini/hooks/dotnet-to-dtk.py""";

    private static readonly string GeminiSection =
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
    public Task<IntegrationResult> IntegrateAsync(string directory, bool force, CancellationToken cancellationToken)
        => IntegrateCoreAsync(
            Path.Combine(directory, "GEMINI.md"),
            Path.Combine(directory, ".gemini"),
            HookCommand,
            force,
            cancellationToken);

    /// <inheritdoc/>
    public Task<IntegrationResult> IntegrateGlobalAsync(bool force, CancellationToken cancellationToken)
        => IntegrateCoreAsync(
            Path.Combine(home.GeminiDir, "GEMINI.md"),
            home.GeminiDir,
            GlobalHookCommand,
            force,
            cancellationToken);

    private static async Task<IntegrationResult> IntegrateCoreAsync(
        string contextFilePath,
        string geminiDir,
        string hookCommand,
        bool force,
        CancellationToken cancellationToken)
    {
        var context = new IntegrationContext(force);

        await IntegratorHelpers.WriteSectionBasedFileAsync(
            contextFilePath,
            SectionMarker, SectionEndMarker, GeminiSection,
            context, cancellationToken).ConfigureAwait(false);

        await IntegratorHelpers.WriteHookAndSettingsAsync(
            new HookSpec(
                Path.Combine(geminiDir, "hooks", "dotnet-to-dtk.py"),
                HookScriptTemplates.GeminiHook,
                Path.Combine(geminiDir, "settings.json"),
                "BeforeTool",
                "run_shell_command",
                hookCommand),
            context, cancellationToken).ConfigureAwait(false);

        return context.ToResult();
    }
}
