#!/usr/bin/env python3
"""Claude Code PreToolUse hook: rewrites `dotnet build|test|restore|clean|format` to `dtk dotnet ...`.

Reads the Bash tool input from stdin (JSON with a "tool_input" object whose
"command" field holds the shell command) and, when a qualifying dotnet command
is found, emits the PreToolUse `updatedInput` payload so Claude Code executes
the rewritten command. Prints nothing when no rewrite is needed.
"""

import json
import re
import sys

_DTK_SUBCOMMANDS = ("build", "clean", "format", "restore", "test")

# Multi-token subcommands are declared with spaces ("list package") but must match any run
# of whitespace between their tokens. Longest-first ordering matters because Python's
# alternation is first-match-wins: a subcommand that prefixes a longer one would shadow it.
_PATTERN = re.compile(
    r"\bdotnet\s+("
    + "|".join(
        s.replace(" ", r"\s+")
        for s in sorted(_DTK_SUBCOMMANDS, key=len, reverse=True)
    )
    + r")\b"
)

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
