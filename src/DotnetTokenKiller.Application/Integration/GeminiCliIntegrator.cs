using DotnetTokenKiller.Application.Integration.Hooks;
using DotnetTokenKiller.Domain.Integration;

namespace DotnetTokenKiller.Application.Integration;

/// <summary>Installs dtk integration artifacts for Gemini CLI.</summary>
/// <param name="home">Resolves the user's home directory for global (home-config) integration.</param>
/// <remarks>
/// Creates:
/// <list type="bullet">
///   <item><description><c>GEMINI.md</c> (project root, section-based merge)</description></item>
///   <item><description>
///     <c>.gemini/settings.json</c> registering <c>dtk hook gemini; exit 0</c> (merged, never overwritten); a
///     Python hook left by an older dtk is migrated
///   </description></item>
/// </list>
/// Declared <see langword="internal"/> (rather than <see langword="public"/>, its original
/// accessibility) because its primary constructor takes the <see langword="internal"/>
/// <see cref="HomePaths"/>: a primary constructor is as accessible as its containing type, and the
/// compiler rejects (CS0051) a public constructor exposing a less-accessible parameter type. See
/// <see cref="ClaudeCodeIntegrator"/> for the same pattern. Callers still reach it polymorphically
/// through the public <see cref="IProviderIntegrator"/> via DI, and tests reach it directly via
/// <c>InternalsVisibleTo</c>.
/// </remarks>
internal sealed class GeminiCliIntegrator(HomePaths home) : IProviderIntegrator, IGlobalIntegrator, IHookIntegrator
{
    /// <inheritdoc/>
    public string ProviderName => "gemini";

    /// <inheritdoc/>
    public IReadOnlyList<HookInstallation> DescribeHooks(string directory, HookScope scope)
    {
        var geminiDir = scope == HookScope.Global ? home.GeminiDir : Path.Combine(directory, ".gemini");

        return
        [
            new HookInstallation(
                ProviderName,
                scope,
                Path.Combine(geminiDir, "settings.json"),
                HookCommands.FailOpen(ProviderName),
                Path.Combine(geminiDir, "hooks", IntegratorHelpers.LegacyHookScriptName),
                HookPayloadKind.GeminiCli)
        ];
    }

    /// <inheritdoc/>
    public Task<IntegrationResult> IntegrateAsync(string directory, bool force, CancellationToken cancellationToken)
        => IntegrateCoreAsync(
            Path.Combine(directory, "GEMINI.md"),
            directory,
            HookScope.Project,
            force,
            cancellationToken);

    /// <inheritdoc/>
    public Task<IntegrationResult> IntegrateGlobalAsync(bool force, CancellationToken cancellationToken)
        => IntegrateCoreAsync(
            Path.Combine(home.GeminiDir, "GEMINI.md"),
            home.Home,
            HookScope.Global,
            force,
            cancellationToken);

    private async Task<IntegrationResult> IntegrateCoreAsync(
        string contextFilePath,
        string hookDirectory,
        HookScope scope,
        bool force,
        CancellationToken cancellationToken)
    {
        var context = new IntegrationContext(force);

        await IntegratorHelpers.WriteSectionBasedFileAsync(
            contextFilePath,
            SharedInstructionArtifacts.SectionMarker, SharedInstructionArtifacts.SectionEndMarker, SharedInstructionArtifacts.Section,
            context, cancellationToken).ConfigureAwait(false);

        var hook = DescribeHooks(hookDirectory, scope)[0];

        var replacedLegacy = await IntegratorHelpers.WriteHookRegistrationAsync(
            new HookRegistrationSpec(hook.RegistrationPath, "BeforeTool", "run_shell_command", hook.Command),
            context, cancellationToken).ConfigureAwait(false);

        await IntegratorHelpers.RetireLegacyHookScriptAsync(
            hook.LegacyScriptPath, replacedLegacy, [hook.RegistrationPath], context, cancellationToken).ConfigureAwait(false);

        return context.ToResult();
    }
}
