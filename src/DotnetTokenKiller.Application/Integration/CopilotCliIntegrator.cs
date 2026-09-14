using System.Text.Json;
using System.Text.Json.Nodes;
using DotnetTokenKiller.Application.Integration.Hooks;
using DotnetTokenKiller.Domain.Integration;

namespace DotnetTokenKiller.Application.Integration;

/// <summary>Installs dtk integration artifacts for GitHub Copilot CLI.</summary>
/// <param name="home">Resolves the user's home directory for global (home-config) integration.</param>
/// <remarks>
/// Creates a hook-based integration modeled on <see cref="GeminiCliIntegrator"/>:
/// <list type="bullet">
///   <item><description>
///     <c>.github/hooks/dtk-dotnet.json</c> — a dtk-owned registration running <c>dtk hook copilot-cli</c>; a
///     Python hook left by an older dtk is migrated
///   </description></item>
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
    private const string HookJsonName = "dtk-dotnet.json";

    /// <summary>
    /// The dtk-managed section written into <c>.github/copilot-instructions.md</c>, between
    /// <see cref="SectionMarker"/> and <see cref="SectionEndMarker"/>. Internal (rather than
    /// private) so <c>SubcommandBindingTests</c> can pin this repo's own committed copy of that
    /// file to it.
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

        A `preToolUse` hook in `.github/hooks/dtk-dotnet.json` runs `dtk hook copilot-cli`, which rewrites
        `dotnet {IntegrationInstructions.SubcommandAlternation}` to `dtk dotnet ...` automatically.
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
                Path.Combine(hooksDir, HookJsonName),
                HookCommands.FailOpen(ProviderName),
                Path.Combine(hooksDir, IntegratorHelpers.LegacyHookScriptName),
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

        await WriteHookArtifactsAsync(directory, HookScope.Project, context, cancellationToken).ConfigureAwait(false);

        return context.ToResult();
    }

    /// <inheritdoc/>
    public async Task<IntegrationResult> IntegrateGlobalAsync(bool force, CancellationToken cancellationToken)
    {
        var context = new IntegrationContext(force);

        await WriteHookArtifactsAsync(home.Home, HookScope.Global, context, cancellationToken).ConfigureAwait(false);

        context.Notes.Add(
            "Copilot CLI instructions are repository-scoped; the global install adds the rewrite hook only. "
            + "Run 'dtk init copilot-cli' inside a project to also write .github/copilot-instructions.md.");

        return context.ToResult();
    }

    private async Task WriteHookArtifactsAsync(
        string directory, HookScope scope, IntegrationContext context, CancellationToken cancellationToken)
    {
        var hook = DescribeHooks(directory, scope)[0];
        var replaceable = await IsDtkRegistrationAsync(hook.RegistrationPath, cancellationToken).ConfigureAwait(false);
        var ranLegacyScript = await MentionsLegacyScriptAsync(hook.RegistrationPath, cancellationToken).ConfigureAwait(false);

        await IntegratorHelpers.WriteOwnedFileAsync(
            hook.RegistrationPath, BuildHookJson(hook.Command), replaceable, context, cancellationToken).ConfigureAwait(false);

        // A skipped registration may still run the Python script, and Copilot CLI denies the tool call when a
        // hook fails, so the script goes only once the registration no longer needs it — and no other hook
        // file does either, since Copilot CLI loads every JSON file in the hooks directory.
        var replacedLegacy = ranLegacyScript && !context.Skipped.Contains(hook.RegistrationPath);
        var hooksDirectory = Path.GetDirectoryName(hook.RegistrationPath)!;
        var hookFiles = Directory.Exists(hooksDirectory)
            ? Directory.GetFiles(hooksDirectory, "*.json").Order(StringComparer.Ordinal).ToList()
            : [];

        await IntegratorHelpers.RetireLegacyHookScriptAsync(
            hook.LegacyScriptPath, replacedLegacy, hookFiles, context, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Whether an existing registration file mentions the Python hook script, read before dtk rewrites it.</summary>
    /// <param name="path">The registration file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private static async Task<bool> MentionsLegacyScriptAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            var content = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            return content.Contains(IntegratorHelpers.LegacyHookScriptName, StringComparison.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>
    /// Whether an existing <c>dtk-dotnet.json</c> holds only dtk's own hooks — the Python-era
    /// <c>dotnet-to-dtk.py</c> command or <c>dtk hook copilot-cli</c> — so it can be replaced without <c>--force</c>.
    /// </summary>
    /// <param name="path">The registration file.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task<bool> IsDtkRegistrationAsync(string path, CancellationToken cancellationToken)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            var root = JsonNode.Parse(await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false));
            if (root?["hooks"]?["preToolUse"] is not JsonArray { Count: > 0 } entries)
            {
                return false;
            }

            return entries.All(IsDtkHookEntry);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException
                                       or InvalidOperationException or ArgumentException)
        {
            // ArgumentException: JsonNode.Parse accepts a repeated key and throws only when the object is indexed.
            return false;
        }
    }

    /// <summary>Whether a <c>preToolUse</c> entry runs a command, and every command it runs is dtk's.</summary>
    /// <param name="entry">One element of <c>hooks.preToolUse</c>.</param>
    private bool IsDtkHookEntry(JsonNode? entry)
    {
        if (entry is not JsonObject hook)
        {
            return false;
        }

        var invocation = HookCommands.Invocation(ProviderName);
        var sawCommand = false;
        foreach (var key in new[] { "bash", "powershell", "command" })
        {
            if (hook[key] is not JsonValue value || !value.TryGetValue<string>(out var text))
            {
                continue;
            }

            if (!text.Contains(IntegratorHelpers.LegacyHookScriptName, StringComparison.Ordinal)
                && !text.Contains(invocation, StringComparison.Ordinal))
            {
                return false;
            }

            sawCommand = true;
        }

        return sawCommand;
    }

    private static string BuildHookJson(string command)
    {
        var root = new JsonObject
        {
            ["version"] = 1,
            ["hooks"] = new JsonObject
            {
                ["preToolUse"] = new JsonArray(
                    new JsonObject
                    {
                        ["type"] = "command",
                        ["matcher"] = "bash",
                        ["bash"] = command,
                        ["powershell"] = command,
                        ["timeoutSec"] = 10
                    })
            }
        };

        // Normalize to '\n'; WriteIndented emits '\r\n' on Windows.
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }).ReplaceLineEndings("\n");
    }
}
