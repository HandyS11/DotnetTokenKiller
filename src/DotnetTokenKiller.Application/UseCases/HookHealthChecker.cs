using System.Text.Json;
using System.Text.Json.Nodes;
using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Application.Integration;
using DotnetTokenKiller.Application.Integration.Hooks;
using DotnetTokenKiller.Domain;
using DotnetTokenKiller.Domain.Execution;

namespace DotnetTokenKiller.Application.UseCases;

/// <summary>
/// Checks that the rewrite hooks dtk installed are registered and that the <c>dtk</c> on <c>PATH</c> answers them.
/// </summary>
/// <remarks>
/// A registration is only a command string, <c>dtk hook &lt;provider&gt;</c>; what breaks silently is the
/// <c>dtk</c> it names — missing from <c>PATH</c>, or too old to know <c>hook</c>. The probe runs exactly that,
/// resolved from <c>PATH</c> by <see cref="ExecutableSearch"/> and started by its absolute path: a bare name
/// would be found beside the running dtk first, so every probe would test the dtk running doctor.
/// A registration still naming the Python <c>dotnet-to-dtk.py</c> script is an install from before
/// <c>dtk hook</c>, reported with the command that migrates it.
/// </remarks>
/// <param name="runner">Runs the installed hook for the probe.</param>
/// <param name="locateDtk">
/// Returns the absolute path of the <c>dtk</c> a harness would run, or <see langword="null"/> when <c>PATH</c> has none.
/// </param>
internal sealed class HookHealthChecker(ICommandRunner runner, Func<string?> locateDtk)
{
    /// <summary>Creates a checker that resolves <c>dtk</c> from this process's <c>PATH</c>.</summary>
    /// <param name="runner">Runs the installed hook for the probe.</param>
    public HookHealthChecker(ICommandRunner runner)
        : this(runner, static () => ExecutableSearch.FindOnProcessPath(DtkCommand))
    {
    }

    /// <summary>The command name every registration runs.</summary>
    private const string DtkCommand = "dtk";

    /// <summary>How long the probe waits before declaring the hook wedged.</summary>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);

    private const string UpdateCommand = "dotnet tool update -g DotnetTokenKiller";

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
                    var registration = await ReadRegistrationAsync(installation, cancellationToken).ConfigureAwait(false);
                    if (registration.Kind == RegistrationKind.Absent
                        && (installation.LegacyScriptPath is null || !File.Exists(installation.LegacyScriptPath)))
                    {
                        continue;
                    }

                    checks.AddRange(await CheckAsync(integrator, installation, registration, projectDirectory, cancellationToken)
                        .ConfigureAwait(false));
                }
            }
        }

        if (checks.Count == 0)
        {
            checks.Add(new DiagnosticCheck(
                "hook integration",
                true,
                "No rewrite hook found in this directory or your home config. "
                + "Run 'dtk init <provider>' to install one."));
        }

        return checks;
    }

    private async Task<IReadOnlyList<DiagnosticCheck>> CheckAsync(
        IHookIntegrator integrator, HookInstallation installation, Registration registration, string projectDirectory,
        CancellationToken cancellationToken)
    {
        var name = CheckName(installation, "hook");

        return registration.Kind switch
        {
            RegistrationKind.Legacy =>
            [
                new DiagnosticCheck(
                    name,
                    false,
                    $"legacy Python hook — {installation.RegistrationPath} still runs {IntegratorHelpers.LegacyHookScriptName}. "
                    + $"Run '{RemedyCommand(installation)}' to migrate it to '{installation.Command}'.")
            ],
            RegistrationKind.Current =>
            [
                new DiagnosticCheck(name, true, "registered"),
                await ProbeAsync(installation, cancellationToken).ConfigureAwait(false),
                .. ApprovalChecks(integrator, installation, projectDirectory)
            ],
            _ => [new DiagnosticCheck(name, false, $"{registration.Problem}. Run '{RemedyCommand(installation)}'.")]
        };
    }

    /// <summary>The harness's approval requirements for a current registration, as checks; empty for most harnesses.</summary>
    /// <param name="integrator">The provider that described the installation.</param>
    /// <param name="installation">The registered installation.</param>
    /// <param name="projectDirectory">The directory to treat as the project root.</param>
    private static IReadOnlyList<DiagnosticCheck> ApprovalChecks(
        IHookIntegrator integrator, HookInstallation installation, string projectDirectory)
    {
        if (integrator is not IHookApprovalInspector inspector)
        {
            return [];
        }

        // Doctor must never crash: whatever reading the harness's own config throws becomes one warning.
        try
        {
            return
            [
                .. inspector.InspectApproval(installation, projectDirectory).Select(finding => finding.Satisfied
                    ? new DiagnosticCheck(CheckName(installation, finding.Label), true, finding.Message)
                    : DiagnosticCheck.Warning(CheckName(installation, finding.Label), finding.Message))
            ];
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return
            [
                DiagnosticCheck.Warning(
                    CheckName(installation, "hook approval"),
                    $"dtk could not check whether {installation.ProviderName} will run this hook: {ex.Message}")
            ];
        }
    }

    /// <summary>What the registration file says about this hook.</summary>
    private enum RegistrationKind
    {
        /// <summary>No file, or a file with no dtk hook in it.</summary>
        Absent = 0,

        /// <summary>A file dtk could not read or parse.</summary>
        Unreadable = 1,

        /// <summary>A registration still running the Python script.</summary>
        Legacy = 2,

        /// <summary>A registration running <c>dtk hook &lt;provider&gt;</c>.</summary>
        Current = 3
    }

    /// <summary>The classification of one registration file.</summary>
    /// <param name="Kind">What the file holds.</param>
    /// <param name="Problem">Why no current registration was found, for <see cref="RegistrationKind.Absent"/> and <see cref="RegistrationKind.Unreadable"/>.</param>
    private sealed record Registration(RegistrationKind Kind, string Problem);

    /// <summary>
    /// Classifies the registration by searching every string in its JSON — which spans Claude Code's and
    /// Gemini CLI's nested <c>hooks[event][].hooks[].command</c> and Copilot CLI's <c>hooks.preToolUse[].bash</c>
    /// with no per-provider branching.
    /// </summary>
    /// <param name="installation">The installation whose registration to read.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private static async Task<Registration> ReadRegistrationAsync(
        HookInstallation installation, CancellationToken cancellationToken)
    {
        var path = installation.RegistrationPath;
        if (!File.Exists(path))
        {
            return new Registration(RegistrationKind.Absent, $"not registered — {path} does not exist");
        }

        string content;
        try
        {
            content = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new Registration(RegistrationKind.Unreadable, $"{path} could not be read: {ex.Message}");
        }

        // JsonNode.Parse accepts a repeated key and throws ArgumentException only when the object is
        // enumerated, so the search belongs inside the same guard as the parse.
        try
        {
            var root = JsonNode.Parse(content, documentOptions: IntegratorHelpers.LenientJson);

            if (FindStringContaining(root, IntegratorHelpers.LegacyHookScriptName) is not null)
            {
                return new Registration(RegistrationKind.Legacy, string.Empty);
            }

            return FindStringContaining(root, HookCommands.Invocation(installation.ProviderName)) is not null
                ? new Registration(RegistrationKind.Current, string.Empty)
                : new Registration(RegistrationKind.Absent, $"not registered — no entry in {path} runs '{installation.Command}'");
        }
        catch (Exception ex) when (ex is JsonException or ArgumentException)
        {
            return new Registration(RegistrationKind.Unreadable, $"{path} could not be read as JSON: {ex.Message}");
        }
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

    /// <summary>Feeds a payload through the <c>dtk</c> on <c>PATH</c> and asserts every subcommand is rewritten.</summary>
    /// <param name="installation">The installation to probe.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    private async Task<DiagnosticCheck> ProbeAsync(HookInstallation installation, CancellationToken cancellationToken)
    {
        var name = CheckName(installation, "hook probe");

        var dtk = locateDtk();
        if (dtk is null)
        {
            return new DiagnosticCheck(
                name,
                false,
                $"'{DtkCommand}' was not found on PATH, and the harness runs it from there. Add the .NET tools directory "
                + "(~/.dotnet/tools) to PATH, or install dtk with 'dotnet tool install -g DotnetTokenKiller'.");
        }

        var hook = $"{dtk} {HookCommands.Verb} {installation.ProviderName}";
        var command = string.Join("; ", DotnetSubcommands.Ordered.Select(sub => $"dotnet {sub}"));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(ProbeTimeout);

        try
        {
            var result = await runner
                .RunCapturedWithInputAsync(
                    dtk,
                    [HookCommands.Verb, installation.ProviderName],
                    BuildPayload(installation.PayloadKind, command),
                    timeout.Token)
                .ConfigureAwait(false);

            if (result.ExitCode != 0)
            {
                return new DiagnosticCheck(
                    name,
                    false,
                    $"'{hook}' exited with code {result.ExitCode}: {FirstErrorLine(result)} "
                    + $"A dtk older than 'dtk hook' cannot answer it; run '{UpdateCommand}'.");
            }

            var missing = DotnetSubcommands.Ordered
                .Where(sub => !result.StdOut.Contains($"dtk dotnet {sub}", StringComparison.Ordinal))
                .ToList();

            return missing.Count == 0
                ? new DiagnosticCheck(name, true, $"{dtk} rewrites all {DotnetSubcommands.Ordered.Count} subcommands")
                : new DiagnosticCheck(
                    name,
                    false,
                    $"{dtk} does not rewrite: {string.Join(", ", missing)}. It is older than this dtk; run '{UpdateCommand}'.");
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new DiagnosticCheck(name, false, $"'{hook}' did not respond within {ProbeTimeout.TotalSeconds:F0}s");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return new DiagnosticCheck(name, false, $"could not run {dtk}: {ex.Message}.");
        }
    }

    /// <summary>
    /// The first non-blank line of a failed probe's stderr, or of its stdout when stderr is empty — a dtk too old
    /// to know <c>hook</c> reports "Unknown command" on stdout, because its argument parser writes errors there.
    /// </summary>
    /// <param name="result">The failed probe's captured output.</param>
    private static string FirstErrorLine(CommandResult result)
    {
        var output = string.IsNullOrWhiteSpace(result.StdErr) ? result.StdOut : result.StdErr;
        var line = output.Split('\n').Select(text => text.Trim()).FirstOrDefault(text => text.Length > 0) ?? "(no output)";
        return line.EndsWith('.') ? line : line + ".";
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

    /// <summary>
    /// Renders the <c>dtk init</c> remedy command for one installation, appending
    /// <c>--global</c> whenever that installation lives in the user's home config. Every remedy
    /// message must go through this so a global-hook failure can never be pointed at the plain,
    /// project-scoped command — which refreshes the wrong installation and leaves doctor red.
    /// </summary>
    /// <param name="installation">The installation the remedy command targets.</param>
    private static string RemedyCommand(HookInstallation installation)
    {
        var scopeFlag = installation.Scope == HookScope.Global ? " --global" : string.Empty;
        return $"dtk init {installation.ProviderName}{scopeFlag}";
    }
}
