using DotnetTokenKiller.Domain.Integration;
using System.Text.Json.Nodes;

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

    /// <inheritdoc/>
    public string ProviderName => "gemini";

    /// <inheritdoc/>
    public async Task<IntegrationResult> IntegrateAsync(
        string directory,
        bool force,
        CancellationToken cancellationToken)
    {
        var created = new List<string>();
        var updated = new List<string>();
        var skipped = new List<string>();

        await WriteGeminiMdAsync(
            Path.Combine(directory, "GEMINI.md"),
            force, created, updated, skipped, cancellationToken).ConfigureAwait(false);

        await WriteFileAsync(
            Path.Combine(directory, ".gemini", "hooks", "dotnet-to-dtk.py"),
            HookScript,
            force, created, updated, skipped, cancellationToken).ConfigureAwait(false);

        await MergeSettingsJsonAsync(
            Path.Combine(directory, ".gemini", "settings.json"),
            created, updated, skipped, cancellationToken).ConfigureAwait(false);

        return new IntegrationResult(created, updated, skipped);
    }

    private static async Task WriteFileAsync(
        string path,
        string content,
        bool force,
        List<string> created,
        List<string> updated,
        List<string> skipped,
        CancellationToken cancellationToken)
    {
        var exists = File.Exists(path);

        if (exists && !force)
        {
            skipped.Add(path);
            return;
        }

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content, cancellationToken).ConfigureAwait(false);
        (exists ? updated : created).Add(path);
    }

    private static async Task WriteGeminiMdAsync(
        string path,
        bool force,
        List<string> created,
        List<string> updated,
        List<string> skipped,
        CancellationToken cancellationToken)
    {
        var exists = File.Exists(path);

        if (exists)
        {
            var current = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            if (current.Contains(SectionMarker, StringComparison.Ordinal))
            {
                if (!force)
                {
                    skipped.Add(path);
                    return;
                }

                var replaced = ReplaceDtkSection(current);
                await File.WriteAllTextAsync(path, replaced, cancellationToken).ConfigureAwait(false);
                updated.Add(path);
                return;
            }

            var trimmed = current.TrimEnd();
            var appended = string.IsNullOrWhiteSpace(trimmed)
                ? GeminiSection
                : trimmed + Environment.NewLine + GeminiSection;
            await File.WriteAllTextAsync(path, appended, cancellationToken).ConfigureAwait(false);
            updated.Add(path);
        }
        else
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await File.WriteAllTextAsync(path, GeminiSection, cancellationToken).ConfigureAwait(false);
            created.Add(path);
        }
    }

    private static string ReplaceDtkSection(string content)
    {
        const string endMarker = "<!-- /dtk -->";
        var start = content.IndexOf(SectionMarker, StringComparison.Ordinal);
        var end = content.IndexOf(endMarker, start, StringComparison.Ordinal);

        if (end < 0)
        {
            return content[..start] + GeminiSection;
        }

        return content[..start] + GeminiSection + content[(end + endMarker.Length)..];
    }

    private const string HooksKey = "hooks";
    private const string HookCommand = "python3 .gemini/hooks/dotnet-to-dtk.py";

    /// <summary>
    /// Merges the dtk BeforeTool hook into <c>.gemini/settings.json</c>.
    /// Existing content is preserved; the hook entry is only added if not already present.
    /// </summary>
    /// <param name="path">Path to the settings.json file.</param>
    /// <param name="created">Accumulator for newly created file paths.</param>
    /// <param name="updated">Accumulator for updated file paths.</param>
    /// <param name="skipped">Accumulator for skipped file paths.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="InvalidOperationException">Thrown if the settings file contains invalid JSON or an unexpected structure.</exception>
    private static async Task MergeSettingsJsonAsync(
        string path,
        List<string> created,
        List<string> updated,
        List<string> skipped,
        CancellationToken cancellationToken)
    {
        var exists = File.Exists(path);
        JsonObject root;
        if (exists)
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            JsonNode? parsed;
            try
            {
                parsed = JsonNode.Parse(json);
            }
            catch (System.Text.Json.JsonException ex)
            {
                throw new InvalidOperationException(
                    $"Failed to parse JSON settings file '{path}'. The file must contain a valid JSON object at the root.",
                    ex);
            }

            if (parsed is JsonObject obj)
            {
                root = obj;
            }
            else
            {
                var actualType = parsed?.GetType().Name ?? "null";
                throw new InvalidOperationException(
                    $"The settings file '{path}' must contain a JSON object at the root, but found '{actualType}'.");
            }
        }
        else
        {
            root = [];
        }

        root.TryGetPropertyValue(HooksKey, out var hooksNode);
        var hooks = hooksNode as JsonObject ?? [];

        hooks.TryGetPropertyValue("BeforeTool", out var beforeNode);
        var beforeTool = beforeNode as JsonArray ?? [];

        if (IsHookAlreadyRegistered(beforeTool, HookCommand))
        {
            skipped.Add(path);
            return;
        }

        beforeTool.Add(new JsonObject
        {
            ["matcher"] = "run_shell_command",
            [HooksKey] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "command",
                    ["command"] = HookCommand
                }
            }
        });

        hooks["BeforeTool"] = beforeTool;
        root[HooksKey] = hooks;

        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(
            path,
            root.ToJsonString(new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            }),
            cancellationToken).ConfigureAwait(false);

        (exists ? updated : created).Add(path);
    }

    private static bool IsHookAlreadyRegistered(JsonArray beforeTool, string hookCommand)
    {
        foreach (var item in beforeTool)
        {
            if (item is not JsonObject entry)
            {
                continue;
            }

            entry.TryGetPropertyValue(HooksKey, out var innerHooksNode);
            if (innerHooksNode is not JsonArray innerHooks)
            {
                continue;
            }

            foreach (var inner in innerHooks)
            {
                if (inner is JsonObject innerEntry &&
                    innerEntry["command"]?.GetValue<string>() == hookCommand)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private const string GeminiSection =
        """
        <!-- dtk -->
        ## DotnetTokenKiller (dtk)

        Use `dtk` instead of raw `dotnet` for build, test, restore, and clean commands.
        `dtk` filters output to actionable signal only, reducing noise by 50-97%.

        ```sh
        dtk dotnet build MyProject.slnx
        dtk dotnet test --filter "Category=Unit"
        dtk dotnet restore
        dtk dotnet clean
        ```

        - All arguments and flags are forwarded to `dotnet` unchanged.
        - Exit codes are preserved — CI pipelines work correctly.
        - Unknown subcommands (e.g. `run`, `publish`) pass through to `dotnet` unchanged.
        <!-- /dtk -->
        """;

    /// <summary>4-quote raw string literal so Python triple-quoted docstrings embed without escaping.</summary>
    private const string HookScript = """"
                                      #!/usr/bin/env python3
                                      """Gemini CLI BeforeTool hook: rewrites `dotnet build|test|restore|clean` to `dtk dotnet ...`.

                                      Reads the BeforeTool event from stdin (JSON with a "tool_input" field),
                                      rewrites qualifying dotnet commands to use dtk, and prints the
                                      modified JSON to stdout so Gemini CLI uses the rewritten command.
                                      """

                                      import json
                                      import re
                                      import sys


                                      _DTK_SUBCOMMANDS = {"build", "test", "restore", "clean"}

                                      _PATTERN = re.compile(r"\bdotnet\s+(" + "|".join(_DTK_SUBCOMMANDS) + r")\b")


                                      def rewrite(command: str) -> str:
                                          """Prefix matching `dotnet <sub>` invocations with `dtk`, unless already prefixed."""

                                          def _replace(match: re.Match) -> str:
                                              preceding = command[: match.start()].rstrip()
                                              last_token = preceding.split()[-1] if preceding else ""
                                              if last_token in ("dtk", "dtk.exe"):
                                                  return match.group(0)
                                              return f"dtk dotnet {match.group(1)}"

                                          return _PATTERN.sub(_replace, command)


                                      def main() -> None:
                                          try:
                                              payload = json.load(sys.stdin)
                                          except (json.JSONDecodeError, EOFError):
                                              return

                                          tool_input = payload.get("tool_input", {})
                                          command = tool_input.get("command", "")

                                          if not command:
                                              print(json.dumps({"decision": "allow"}))
                                              return

                                          rewritten = rewrite(command)

                                          if rewritten != command:
                                              print(json.dumps({
                                                  "decision": "allow",
                                                  "hookSpecificOutput": {
                                                      "tool_input": {"command": rewritten}
                                                  }
                                              }))
                                          else:
                                              print(json.dumps({"decision": "allow"}))


                                      if __name__ == "__main__":
                                          main()
                                      """";
}
