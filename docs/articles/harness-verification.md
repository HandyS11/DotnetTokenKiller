# Harness verification

Some integrations could not be run against the real harness when they were written, because the harness needs an
account. Their hooks follow the harness's documentation and are covered by payload tests, but the points below are
unconfirmed. If you can run one, please report the result in an issue.

## Cursor

1. `{}` is accepted as a neutral reply: with the hook installed, ask the agent to run `ls`; it must run normally.
2. A rewrite lands: ask the agent to run `dotnet build`; the command shown must be `dtk dotnet build`, in the IDE and in `cursor-agent -p`.
3. Whether `permission: "allow"` skips Cursor's approval prompt for the rewritten command (if it does not, dtk could
   also rewrite chained commands and `publish`/`pack`).
4. With dtk's Claude Code hook also in `~/.claude/settings.json`: the command is rewritten once, not `dtk dtk`.

## Devin (Devin Local and Devin CLI)

1. The matcher `exec` selects the shell tool: `dotnet build` becomes `dtk dotnet build` in `devin` and in Devin Desktop.
2. Which shell runs the hook command on Windows.
3. Whether a workspace in Restricted Mode runs the project hook.
