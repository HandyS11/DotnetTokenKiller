using System.Text.Json;
using System.Text.Json.Nodes;
using DotnetTokenKiller.Domain.Integration;

namespace DotnetTokenKiller.Application.Integration;

/// <summary>Installs dtk integration artifacts for GitHub Copilot CLI.</summary>
/// <param name="home">Resolves the user's home directory for global (home-config) integration.</param>
/// <remarks>
/// Creates a hook-based integration modeled on <see cref="GeminiCliIntegrator"/>:
/// <list type="bullet">
///   <item><description><c>.github/hooks/dotnet-to-dtk.py</c> — the preToolUse rewrite script.</description></item>
///   <item><description><c>.github/hooks/dtk-dotnet.json</c> — a dedicated, dtk-owned hook registration (no merge).</description></item>
///   <item><description><c>.github/copilot-instructions.md</c> — section-merged dtk instructions.</description></item>
/// </list>
/// Distinct from <see cref="GitHubCopilotIntegrator"/> (the instruction-only IDE <c>copilot</c> provider).
/// Declared <see langword="internal"/> because its primary constructor takes the internal
/// <see cref="HomePaths"/>; reached polymorphically via <see cref="IProviderIntegrator"/> through DI.
/// </remarks>
internal sealed class CopilotCliIntegrator(HomePaths home) : IProviderIntegrator, IGlobalIntegrator, IHookIntegrator
{
    private const string SectionMarker = "<!-- dtk -->";
    private const string SectionEndMarker = "<!-- /dtk -->";
    private const string HookScriptName = "dotnet-to-dtk.py";
    private const string HookJsonName = "dtk-dotnet.json";

    /// <summary>
    /// The dtk-managed section written into <c>.github/copilot-instructions.md</c>, between
    /// <see cref="SectionMarker"/> and <see cref="SectionEndMarker"/>. Internal (rather than
    /// private) so <c>SubcommandBindingTests</c> can pin this repo's own committed copy of that
    /// file to it, the same way <see cref="HookScriptTemplates.ClaudeHook"/> pins the committed
    /// Claude hook.
    /// <para>
    /// Ends with an explicit <c>\n</c>: when this section is the whole file (the create path, and how
    /// this repo's own committed copy came to be), a section without one produces a file that violates
    /// <c>.editorconfig</c>'s <c>insert_final_newline</c>. A raw string literal drops the newline
    /// before its closing delimiter, so the terminator has to be concatenated rather than typed.
    /// </para>
    /// </summary>
    internal static readonly string CopilotSection =
        $"""
        {SectionMarker}
        ## DotnetTokenKiller (dtk)

        {IntegrationInstructions.Markdown}

        A `preToolUse` hook in `.github/hooks/dtk-dotnet.json` rewrites `dotnet {IntegrationInstructions.SubcommandAlternation}`
        to `dtk dotnet ...` automatically. The hook shells out to `python3`; on Windows (where the launcher is
        usually `python`, not `python3`), edit the `bash` command in that file if it doesn't fire.
        {SectionEndMarker}
        """ + "\n";

    /// <inheritdoc/>
    public string ProviderName => "copilot-cli";

    /// <inheritdoc/>
    public IReadOnlyList<HookInstallation> DescribeHooks(string directory, HookScope scope)
    {
        var hooksDir = scope == HookScope.Global ? home.CopilotHooksDir : Path.Combine(directory, ".github", "hooks");

        return
        [
            new HookInstallation(
                ProviderName,
                scope,
                new GeneratedArtifact(
                    Path.Combine(hooksDir, HookScriptName),
                    HookScriptTemplates.CopilotCliHook,
                    StampStyle.HashComment,
                    IntegratorHelpers.HookLegacySignature),
                Path.Combine(hooksDir, HookJsonName),
                HookPayloadKind.CopilotCli)
        ];
    }

    /// <inheritdoc/>
    public async Task<IntegrationResult> IntegrateAsync(string directory, bool force, CancellationToken cancellationToken)
    {
        var context = new IntegrationContext(force);

        await IntegratorHelpers.WriteSectionBasedFileAsync(
            Path.Combine(directory, ".github", "copilot-instructions.md"),
            SectionMarker, SectionEndMarker, CopilotSection,
            context, cancellationToken).ConfigureAwait(false);

        await WriteHookArtifactsAsync(directory, HookScope.Project, HookCwdRelative, context, cancellationToken)
            .ConfigureAwait(false);

        return context.ToResult();
    }

    /// <inheritdoc/>
    public async Task<IntegrationResult> IntegrateGlobalAsync(bool force, CancellationToken cancellationToken)
    {
        var context = new IntegrationContext(force);

        // Global (~/.copilot/hooks) is not a git-tracked location, so an absolute cwd is portable and
        // unambiguous here (unlike the repo variant, which uses a relative cwd for a committed file).
        await WriteHookArtifactsAsync(home.Home, HookScope.Global, home.CopilotHooksDir, context, cancellationToken)
            .ConfigureAwait(false);

        context.Notes.Add(
            "Copilot CLI instructions are repository-scoped; the global install adds the rewrite hook only. "
            + "Run 'dtk integrate copilot-cli' inside a project to also write .github/copilot-instructions.md.");

        return context.ToResult();
    }

    /// <summary>Relative <c>cwd</c> for the repository hook (resolved by Copilot CLI against the repo root).</summary>
    private const string HookCwdRelative = ".github/hooks";

    private async Task WriteHookArtifactsAsync(
        string directory,
        HookScope scope,
        string cwd,
        IntegrationContext context,
        CancellationToken cancellationToken)
    {
        var hookInstallation = DescribeHooks(directory, scope)[0];

        await IntegratorHelpers.WriteGeneratedFileAsync(hookInstallation.Script, context, cancellationToken)
            .ConfigureAwait(false);

        await IntegratorHelpers.WriteFileAsync(
            hookInstallation.RegistrationPath,
            BuildHookJson(cwd),
            context, cancellationToken).ConfigureAwait(false);
    }

    private static string BuildHookJson(string cwd)
    {
        var root = new JsonObject
        {
            ["version"] = 1,
            ["hooks"] = new JsonObject
            {
                ["preToolUse"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "command",
                        ["matcher"] = "bash",
                        ["bash"] = $"python3 {HookScriptName}",
                        ["cwd"] = cwd,
                        ["timeoutSec"] = 10
                    }
                }
            }
        };

        // Normalize to '\n'; WriteIndented emits '\r\n' on Windows.
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }).ReplaceLineEndings("\n");
    }
}
