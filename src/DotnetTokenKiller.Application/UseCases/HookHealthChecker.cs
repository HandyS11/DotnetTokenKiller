using System.Text.Json;
using System.Text.Json.Nodes;
using DotnetTokenKiller.Application.Integration;
using DotnetTokenKiller.Domain;
using DotnetTokenKiller.Domain.Execution;

namespace DotnetTokenKiller.Application.UseCases;

/// <summary>
/// Checks that the rewrite hooks dtk installed are present, registered, current, and actually
/// firing.
/// </summary>
/// <remarks>
/// These are the checks <c>doctor</c> was missing. The SDK, config, database, and tee directory it
/// already verified rarely break; the hook — a generated Python script, registered by an absolute
/// command string, invoked by an interpreter that may not exist under that name — breaks silently
/// and takes the whole integration with it.
/// </remarks>
/// <param name="runner">Runs the installed hook for the probe.</param>
internal sealed class HookHealthChecker(ICommandRunner runner)
{
    /// <summary>How long the probe waits before declaring the interpreter wedged.</summary>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);

    /// <summary>Runs the status check and probe for every hook installed in either scope.</summary>
    /// <param name="integrators">The hook-installing providers to inspect.</param>
    /// <param name="projectDirectory">The directory to treat as the project root.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    internal async Task<IReadOnlyList<DiagnosticCheck>> RunAsync(
        IReadOnlyList<IHookIntegrator> integrators,
        string projectDirectory,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(integrators);

        var checks = new List<DiagnosticCheck>();

        foreach (var integrator in integrators)
        {
            foreach (var scope in new[] { HookScope.Project, HookScope.Global })
            {
                foreach (var installation in integrator.DescribeHooks(projectDirectory, scope))
                {
                    if (!File.Exists(installation.Script.Path))
                    {
                        continue;
                    }

                    var (registered, registrationMessage) = await ReadRegisteredCommandAsync(
                        installation, cancellationToken).ConfigureAwait(false);
                    checks.Add(await BuildStatusCheckAsync(installation, registered, registrationMessage, cancellationToken)
                        .ConfigureAwait(false));

                    if (registered is not null)
                    {
                        checks.Add(await ProbeAsync(installation, registered, cancellationToken)
                            .ConfigureAwait(false));
                    }
                }
            }
        }

        if (checks.Count == 0)
        {
            checks.Add(new DiagnosticCheck(
                "hook integration",
                true,
                "No rewrite hook found in this directory or your home config. "
                + "Run 'dtk integrate <provider>' to install one."));
        }

        return checks;
    }

    /// <summary>
    /// Finds the command the host CLI is configured to run for this hook, by locating any string in
    /// the registration JSON that names the hook script.
    /// </summary>
    /// <remarks>
    /// Matching on the script's file name rather than on dtk's exact command string is deliberate:
    /// the SKILL and instruction files tell Windows users to change <c>python3</c> to <c>python</c>
    /// in the registration, and an exact-command match would report that working install as broken.
    /// It also spans both registration shapes — the nested <c>hooks[event][].hooks[].command</c>
    /// used by Claude Code and Gemini CLI, and Copilot CLI's <c>hooks.preToolUse[].bash</c> — with
    /// no per-provider branching.
    /// </remarks>
    /// <param name="installation">The installation whose registration to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The registered command and an empty message, or <see langword="null"/> paired with a
    /// human-readable reason.
    /// </returns>
    private static async Task<(string? Command, string Message)> ReadRegisteredCommandAsync(
        HookInstallation installation, CancellationToken cancellationToken)
    {
        if (!File.Exists(installation.RegistrationPath))
        {
            return (null, $"not registered — {installation.RegistrationPath} does not exist");
        }

        JsonNode? root;
        try
        {
            var content = await File.ReadAllTextAsync(installation.RegistrationPath, cancellationToken)
                .ConfigureAwait(false);
            root = JsonNode.Parse(content);
        }
        catch (JsonException ex)
        {
            return (null, $"{installation.RegistrationPath} could not be read as JSON: {ex.Message}");
        }

        var scriptFileName = Path.GetFileName(installation.Script.Path);
        var command = FindStringContaining(root, scriptFileName);

        return command is null
            ? (null, $"not registered — no entry in {installation.RegistrationPath} runs {scriptFileName}")
            : (command, string.Empty);
    }

    /// <summary>Depth-first search for a string value containing <paramref name="needle"/>.</summary>
    /// <param name="node">The JSON node to search.</param>
    /// <param name="needle">The substring to look for.</param>
    private static string? FindStringContaining(JsonNode? node, string needle)
    {
        return node switch
        {
            JsonValue value when value.TryGetValue<string>(out var text) =>
                text.Contains(needle, StringComparison.Ordinal) ? text : null,
            JsonArray array =>
                array.Select(item => FindStringContaining(item, needle)).FirstOrDefault(m => m is not null),
            JsonObject obj =>
                obj.Select(pair => FindStringContaining(pair.Value, needle)).FirstOrDefault(m => m is not null),
            _ => null
        };
    }

    /// <summary>Builds the presence/registration/freshness check for one installation.</summary>
    /// <param name="installation">The installation being checked.</param>
    /// <param name="registeredCommand">The command found in the registration, or <see langword="null"/>.</param>
    /// <param name="registrationMessage">The reason no command was found, when applicable.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private static async Task<DiagnosticCheck> BuildStatusCheckAsync(
        HookInstallation installation,
        string? registeredCommand,
        string registrationMessage,
        CancellationToken cancellationToken)
    {
        var name = CheckName(installation, "hook");

        if (registeredCommand is null)
        {
            return new DiagnosticCheck(
                name,
                false,
                $"{registrationMessage}. Run 'dtk integrate {installation.ProviderName}'.");
        }

        var installed = (await File.ReadAllTextAsync(installation.Script.Path, cancellationToken)
            .ConfigureAwait(false)).ReplaceLineEndings("\n");
        var current = ArtifactStamping.Apply(installation.Script.Body, installation.Script.Style);

        if (string.Equals(installed, current, StringComparison.Ordinal))
        {
            return new DiagnosticCheck(name, true, "installed, registered, up to date");
        }

        // ArtifactStamping.TryParse (and therefore IsAuthentic) requires the stamp to be the last
        // line of the file, so a trailing hand-added edit — e.g. a "# my own change" comment tacked
        // on after the stamp — is correctly rejected here rather than misreported as merely stale.
        // The legacy check below keys on HasStamp, not on "TryParse failed", for the same reason:
        // otherwise that same trailing edit would read as an unstamped legacy file (it still
        // contains the legacy signature) and get silently refreshed through the other branch.
        var refreshable = ArtifactStamping.IsAuthentic(installed)
                          || (!ArtifactStamping.HasStamp(installed)
                              && installed.Contains(installation.Script.LegacySignature, StringComparison.Ordinal));

        return refreshable
            ? new DiagnosticCheck(
                name,
                false,
                $"stale — written by an older dtk. Run 'dtk integrate {installation.ProviderName}' to refresh.")
            : new DiagnosticCheck(
                name,
                false,
                "modified locally — dtk will not overwrite it. Run 'dtk integrate "
                + $"{installation.ProviderName} --force' to regenerate.");
    }

    /// <summary>Feeds a payload through the installed hook and asserts every subcommand is rewritten.</summary>
    /// <param name="installation">The installation to probe.</param>
    /// <param name="registeredCommand">The command the host CLI runs, used to resolve the interpreter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task<DiagnosticCheck> ProbeAsync(
        HookInstallation installation,
        string registeredCommand,
        CancellationToken cancellationToken)
    {
        var name = CheckName(installation, "hook probe");
        var interpreter = ResolveInterpreter(registeredCommand);
        var command = string.Join("; ", DotnetSubcommands.Ordered.Select(sub => $"dotnet {sub}"));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ProbeTimeout);

        try
        {
            var result = await runner
                .RunCapturedWithInputAsync(
                    interpreter,
                    [installation.Script.Path],
                    BuildPayload(installation.PayloadKind, command),
                    timeout.Token)
                .ConfigureAwait(false);

            if (result.ExitCode != 0)
            {
                return new DiagnosticCheck(
                    name, false, $"{interpreter} exited with code {result.ExitCode}: {result.StdErr.Trim()}");
            }

            var missing = DotnetSubcommands.Ordered
                .Where(sub => !result.StdOut.Contains($"dtk dotnet {sub}", StringComparison.Ordinal))
                .ToList();

            return missing.Count == 0
                ? new DiagnosticCheck(name, true, $"rewrites all {DotnetSubcommands.Ordered.Count} subcommands")
                : new DiagnosticCheck(
                    name,
                    false,
                    $"does not rewrite: {string.Join(", ", missing)}. "
                    + $"Run 'dtk integrate {installation.ProviderName}' to refresh the hook.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new DiagnosticCheck(
                name, false, $"{interpreter} did not respond within {ProbeTimeout.TotalSeconds:F0}s");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new DiagnosticCheck(name, false, $"could not run {interpreter}: {ex.Message}");
        }
    }

    /// <summary>
    /// Takes the interpreter from the registered command's first token, so an install whose command
    /// was edited is probed the way the host CLI will actually run it.
    /// </summary>
    /// <param name="registeredCommand">The command found in the registration file.</param>
    private static string ResolveInterpreter(string registeredCommand)
    {
        var first = registeredCommand.Split(
            [' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).FirstOrDefault();

        return string.IsNullOrEmpty(first) ? "python3" : first.Trim('"');
    }

    /// <summary>Builds the stdin payload in the shape the provider's host CLI sends.</summary>
    /// <param name="kind">Which host CLI's payload shape to build.</param>
    /// <param name="command">The shell command the payload should carry.</param>
    private static string BuildPayload(HookPayloadKind kind, string command)
    {
        JsonNode payload = kind switch
        {
            HookPayloadKind.CopilotCli => new JsonObject
            {
                ["toolName"] = "bash",
                ["toolArgs"] = new JsonObject { ["command"] = command }
            },
            _ => new JsonObject
            {
                ["tool_input"] = new JsonObject { ["command"] = command }
            }
        };

        return payload.ToJsonString();
    }

    /// <summary>Builds a check name such as <c>claude hook probe (project)</c>.</summary>
    /// <param name="installation">The installation the check belongs to.</param>
    /// <param name="label">The check label.</param>
    private static string CheckName(HookInstallation installation, string label)
        => $"{installation.ProviderName} {label} ({installation.Scope.ToString().ToLowerInvariant()})";
}
