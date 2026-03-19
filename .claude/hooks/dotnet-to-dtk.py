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

# Matches `dotnet <subcommand>` anywhere in the command; the replacement function
# checks the actual preceding token (ignoring any amount of whitespace) so that
# `dtk  dotnet build`, `dtk\tdotnet build`, etc. are all treated as already-prefixed.
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
        # Output decision: proceed with the rewritten command
        print(json.dumps({"decision": "proceed", "tool_input": tool_input}))
    else:
        # No change needed — let it proceed as-is
        print(json.dumps({"decision": "proceed"}))


if __name__ == "__main__":
    main()
