using DotnetTokenKiller.Domain.Integration;
using System.Text.Json.Nodes;

namespace DotnetTokenKiller.Application.Integration;

/// <summary>Installs dtk integration artifacts for Claude Code.</summary>
/// <remarks>
/// Creates:
/// <list type="bullet">
///   <item><description><c>.claude/skills/dotnet-token-killer/SKILL.md</c></description></item>
///   <item><description><c>.claude/hooks/dotnet-to-dtk.py</c></description></item>
///   <item><description><c>.claude/settings.json</c> (merged, never overwritten)</description></item>
/// </list>
/// </remarks>
public sealed class ClaudeCodeIntegrator : IProviderIntegrator
{
    private const string HookCommand = "python3 .claude/hooks/dotnet-to-dtk.py";

    private const string SkillMarkdown =
        """
        ---
        name: dotnet-token-killer
        description: 'Use `dtk` (DotnetTokenKiller) instead of raw `dotnet` commands to reduce token usage when building, testing, restoring, or cleaning .NET projects.'
        ---

        # DotnetTokenKiller (dtk)

        `dtk` wraps `dotnet` commands and filters output to actionable signal only, saving 50-97% of tokens by stripping SDK banners, MSBuild noise, progress lines, and duplicate diagnostics.

        ## Installation

        ```sh
        dotnet tool install -g DotnetTokenKiller  # requires .NET 10 SDK
        ```

        ## Usage

        Drop-in replacement for `dotnet build`, `test`, `restore`, and `clean`. All arguments and flags are forwarded unchanged:

        ```sh
        dtk dotnet build MyProject.slnx
        dtk dotnet test --filter "Category=Unit"
        dtk dotnet restore
        dtk dotnet clean
        ```

        Unknown subcommands (e.g. `run`, `publish`) pass through to `dotnet` unchanged.

        ## Flags

        | Flag         | Purpose                                           |
        |--------------|---------------------------------------------------|
        | `--show-log` | Print path to full unfiltered log after a run     |
        | `-v`         | Increase verbosity (repeatable: `-v -v`)          |

        ## Key Behaviors

        - Paths are workspace-relative (`src/Foo.cs`, not absolute)
        - Build errors grouped by file; warnings grouped by diagnostic code with frequency counts
        - Exit codes preserved — CI pipelines work correctly
        - Works with xUnit, NUnit, MSTest, and Reqnroll
        - Run `dtk dotnet clean` first for a full warning report (incremental builds skip unchanged files)

        ## Token Savings

        ```sh
        dtk gain               # last 30 days
        dtk gain --days 7
        dtk gain --project     # current project only
        dtk gain --json
        ```
        """;

    /// <summary>4-quote raw string literal so Python triple-quoted docstrings embed without escaping.</summary>
    private const string HookScript = """"
                                      #!/usr/bin/env python3
                                      """Claude Code PreToolUse hook: rewrites `dotnet build|test|restore|clean` to `dtk dotnet ...`.

                                      Reads the Bash tool input from stdin (JSON with a "command" field),
                                      rewrites qualifying dotnet commands to use dtk, and prints the
                                      modified JSON to stdout so Claude Code uses the rewritten command.
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
                                              return

                                          rewritten = rewrite(command)

                                          if rewritten != command:
                                              tool_input["command"] = rewritten
                                              payload["tool_input"] = tool_input
                                              print(json.dumps({"decision": "proceed", "tool_input": tool_input}))
                                          else:
                                              print(json.dumps({"decision": "proceed"}))


                                      if __name__ == "__main__":
                                          main()
                                      """";

    /// <inheritdoc/>
    public string ProviderName => "claude";

    /// <inheritdoc/>
    public async Task<IntegrationResult> IntegrateAsync(
        string directory,
        bool force,
        CancellationToken cancellationToken)
    {
        var created = new List<string>();
        var updated = new List<string>();
        var skipped = new List<string>();

        await IntegratorHelpers.WriteFileAsync(
            Path.Combine(directory, ".claude", "skills", "dotnet-token-killer", "SKILL.md"),
            SkillMarkdown,
            force, created, updated, skipped, cancellationToken).ConfigureAwait(false);

        await IntegratorHelpers.WriteFileAsync(
            Path.Combine(directory, ".claude", "hooks", "dotnet-to-dtk.py"),
            HookScript,
            force, created, updated, skipped, cancellationToken).ConfigureAwait(false);

        await IntegratorHelpers.MergeJsonSettingsAsync(
            Path.Combine(directory, ".claude", "settings.json"),
            "PreToolUse",
            new JsonObject
            {
                ["matcher"] = "Bash",
                ["hooks"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["type"] = "command",
                        ["command"] = HookCommand
                    }
                }
            },
            HookCommand,
            created, updated, skipped, cancellationToken).ConfigureAwait(false);

        return new IntegrationResult(created, updated, skipped);
    }
}
