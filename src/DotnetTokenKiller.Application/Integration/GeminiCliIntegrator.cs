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

    /// <summary>4-quote raw string literal so Python triple-quoted docstrings embed without escaping.</summary>
    private const string HookScript = """"
                                      #!/usr/bin/env python3
                                      """Gemini CLI BeforeTool hook: rewrites `dotnet build|test|restore|clean|format` to `dtk dotnet ...`.

                                      Reads the BeforeTool event from stdin (JSON with a "tool_input" field),
                                      rewrites qualifying dotnet commands to use dtk, and prints the
                                      modified JSON to stdout so Gemini CLI uses the rewritten command.
                                      """

                                      import json
                                      import re
                                      import sys


                                      _DTK_SUBCOMMANDS = {"build", "test", "restore", "clean", "format"}

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
                HookScript,
                Path.Combine(directory, ".gemini", "settings.json"),
                "BeforeTool",
                "run_shell_command",
                HookCommand),
            context, cancellationToken).ConfigureAwait(false);

        return context.ToResult();
    }
}
