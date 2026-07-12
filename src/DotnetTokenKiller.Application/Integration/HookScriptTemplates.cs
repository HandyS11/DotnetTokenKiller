namespace DotnetTokenKiller.Application.Integration;

/// <summary>
/// Shared Python source for the pre-tool-execution hooks shipped by <see cref="ClaudeCodeIntegrator"/>,
/// <see cref="GeminiCliIntegrator"/>, and <see cref="CopilotCliIntegrator"/>.
/// </summary>
/// <remarks>
/// All three hooks rewrite qualifying <c>dotnet &lt;sub&gt;</c> invocations to <c>dtk dotnet &lt;sub&gt;</c> using
/// the identical subcommand pattern, quote-aware boundary scanner, and <c>rewrite()</c> function. They differ only
/// in how the host CLI's payload is read and in the shape of the JSON printed back:
/// <list type="bullet">
///   <item><description>Claude Code expects <c>hookSpecificOutput.updatedInput</c>.</description></item>
///   <item><description>Gemini CLI expects <c>hookSpecificOutput.tool_input</c> alongside a <c>decision</c> field.</description></item>
///   <item><description>GitHub Copilot CLI expects <c>permissionDecision</c>/<c>modifiedArgs</c>.</description></item>
/// </list>
/// </remarks>
internal static class HookScriptTemplates
{
    /// <summary>4-quote raw string literals so embedded Python triple-quoted docstrings need no escaping.</summary>
    private const string ClaudeHeader = """"
        #!/usr/bin/env python3
        """Claude Code PreToolUse hook: rewrites `dotnet build|test|restore|clean|format` to `dtk dotnet ...`.

        Reads the Bash tool input from stdin (JSON with a "tool_input" object whose
        "command" field holds the shell command) and, when a qualifying dotnet command
        is found, emits the PreToolUse `updatedInput` payload so Claude Code executes
        the rewritten command. Prints nothing when no rewrite is needed.
        """


        """";

    private const string GeminiHeader = """"
        #!/usr/bin/env python3
        """Gemini CLI BeforeTool hook: rewrites `dotnet build|test|restore|clean|format` to `dtk dotnet ...`.

        Reads the BeforeTool event from stdin (JSON with a "tool_input" field) and, when a
        qualifying dotnet command is found, emits the BeforeTool `hookSpecificOutput.tool_input`
        payload so Gemini CLI executes the rewritten command.
        """


        """";

    /// <summary>
    /// Imports, the subcommand pattern, the quote-aware boundary scanner, and <c>rewrite()</c> — identical for
    /// both hooks.
    /// </summary>
    private const string SharedCore = """"
        import json
        import re
        import sys

        _DTK_SUBCOMMANDS = ("build", "clean", "format", "restore", "test")

        _PATTERN = re.compile(r"\bdotnet\s+(" + "|".join(_DTK_SUBCOMMANDS) + r")\b")

        # Characters that may legitimately precede the `dotnet` token at a command
        # boundary. Anything else (a slash, a quote, a letter) means we are inside a
        # path, a string literal, or another word — do not rewrite.
        _BOUNDARY_CHARS = " \t;&|({`\n"


        def _inside_quotes(command: str, index: int) -> bool:
            """Whether `index` falls inside a shell quote region, honoring nesting and backslash escapes.

            A single-quoted region does not process backslash escapes and cannot be
            ended by a double quote; a double-quoted region cannot be ended by a single
            quote. Good enough to keep `dotnet <sub>` inside quoted arguments untouched.
            """
            in_single = False
            in_double = False
            i = 0
            while i < index:
                char = command[i]
                if char == "\\" and not in_single:
                    i += 2  # backslash escapes the next character outside single quotes
                    continue
                if char == "'" and not in_double:
                    in_single = not in_single
                elif char == '"' and not in_single:
                    in_double = not in_double
                i += 1
            return in_single or in_double


        def rewrite(command: str) -> str:
            """Prefix matching `dotnet <sub>` invocations with `dtk`, unless already prefixed."""

            def _replace(match: re.Match) -> str:
                start = match.start()
                if start > 0 and command[start - 1] not in _BOUNDARY_CHARS:
                    return match.group(0)  # path like /usr/lib64/dotnet/dotnet or ./dotnet
                if _inside_quotes(command, start):
                    return match.group(0)  # e.g. git commit -m "fix dotnet build"
                preceding = command[:start].rstrip()
                last_token = preceding.split()[-1] if preceding else ""
                if last_token in ("dtk", "dtk.exe"):
                    return match.group(0)
                return f"dtk dotnet {match.group(1)}"

            return _PATTERN.sub(_replace, command)



        """";

    private const string ClaudeMain = """
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
                print(json.dumps({
                    "hookSpecificOutput": {
                        "hookEventName": "PreToolUse",
                        "updatedInput": tool_input,
                    }
                }))
            # No output on the no-change path: Claude Code proceeds normally.


        if __name__ == "__main__":
            main()

        """;

    private const string GeminiMain = """
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

        """;

    private const string CopilotCliHeader = """"
        #!/usr/bin/env python3
        """GitHub Copilot CLI preToolUse hook: rewrites `dotnet build|test|restore|clean|format` to `dtk dotnet ...`.

        Reads the preToolUse event from stdin (JSON with "toolName" and "toolArgs";
        for the CLI's file-based hooks "toolArgs" is a JSON string holding {"command": ...}).
        When the tool is `bash` and a qualifying dotnet command is found, prints a
        preToolUse decision carrying the rewritten command via `modifiedArgs`:
        "allow" for a simple single invocation, "ask" for a compound command (e.g.
        `dotnet build && rm -rf x`) so Copilot still prompts before the other parts run.
        Prints nothing when no rewrite is needed. Always exits 0: Copilot CLI treats a
        non-zero exit as a denial.
        """


        """";

    private const string CopilotCliMain = """
        # Unquoted shell operators that can chain, pipe, or subshell another command.
        _CHAINING_CHARS = ";&|`\n()"


        def _is_simple_command(command: str) -> bool:
            # Whether `command` is a single invocation with no unquoted operators that
            # could run another command alongside the dotnet one. Only a simple command
            # is auto-approved ("allow"); anything compound (e.g. `dotnet build && rm -rf x`)
            # is downgraded to "ask" so Copilot CLI still prompts on the non-dotnet parts.
            i = 0
            while i < len(command):
                char = command[i]
                if char == "\\":
                    i += 2  # skip an escaped character
                    continue
                if char in _CHAINING_CHARS and not _inside_quotes(command, i):
                    return False
                i += 1
            return True


        def main() -> None:
            # Copilot CLI is fail-closed: a non-zero exit denies the tool call. Guard the
            # entire body so any unexpected shape (e.g. a top-level JSON array/string) is a
            # silent no-op rather than an uncaught exception.
            try:
                payload = json.load(sys.stdin)

                if payload.get("toolName") != "bash":
                    return

                tool_args = payload.get("toolArgs", {})
                if isinstance(tool_args, str):
                    tool_args = json.loads(tool_args)
                if not isinstance(tool_args, dict):
                    return

                command = tool_args.get("command", "")
                if not command:
                    return

                rewritten = rewrite(command)

                if rewritten != command:
                    modified = dict(tool_args)
                    modified["command"] = rewritten
                    # Only auto-approve a simple, single dotnet invocation. A compound
                    # command is still rewritten, but returns "ask" so Copilot prompts
                    # rather than silently approving its non-dotnet parts.
                    decision = "allow" if _is_simple_command(command) else "ask"
                    print(json.dumps({
                        "permissionDecision": decision,
                        "modifiedArgs": modified,
                    }))
                # No output on the no-change path: Copilot CLI proceeds normally.
            except Exception:
                return  # Allow, no change: never let an unexpected error deny the command.


        if __name__ == "__main__":
            main()

        """;

    /// <summary>Claude Code PreToolUse hook, verbatim identical to <c>.claude/hooks/dotnet-to-dtk.py</c> in this repo.</summary>
    internal static string ClaudeHook { get; } = ClaudeHeader + SharedCore + ClaudeMain;

    /// <summary>Gemini CLI BeforeTool hook, preserving Gemini's existing <c>hookSpecificOutput.tool_input</c> schema.</summary>
    internal static string GeminiHook { get; } = GeminiHeader + SharedCore + GeminiMain;

    /// <summary>GitHub Copilot CLI preToolUse hook, emitting the CLI's <c>permissionDecision</c>/<c>modifiedArgs</c> schema.</summary>
    internal static string CopilotCliHook { get; } = CopilotCliHeader + SharedCore + CopilotCliMain;
}
