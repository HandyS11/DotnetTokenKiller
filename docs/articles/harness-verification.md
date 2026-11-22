# Harness verification

Some integrations could not be run against the real harness when they were written, because the harness needs an
account. Their hooks follow the harness's documentation and are covered by payload tests, but the points below are
unconfirmed. If you can run one, please report the result in an issue.

## Claude Code

1. On Windows, which form the `SessionStart`/`SubagentStart` payload's `cwd` takes (`C:\…`, or Git Bash's `/c/…`,
   which dtk does not read as a path): the context hooks were run against the real Claude Code on Linux only.

## Cursor

1. `{}` is accepted as a neutral reply: with the hook installed, ask the agent to run `ls`; it must run normally.
2. A rewrite lands: ask the agent to run `dotnet build`; the command shown must be `dtk dotnet build`, in the IDE and in `cursor-agent -p`.
3. Whether `permission: "allow"` skips Cursor's approval prompt for the rewritten command (if it does not, dtk could
   also rewrite chained commands and `publish`/`pack`).
4. With dtk's Claude Code hook also in `~/.claude/settings.json`: the command is rewritten once, not `dtk dtk`.
5. With a dtk that prints nothing (0.8.0 or earlier) on PATH, does Cursor block the command or let it run?
6. With dtk's Claude Code hooks in `~/.claude/settings.json`, whether Cursor runs their `SessionStart` and
   `SubagentStart` entries, under which event name, and that a subagent still starts. Cursor documents that it loads
   Claude Code hooks, but not how it maps their events; its `subagentStart` is a permission hook that blocks the
   subagent on output outside its schema (`permission` appears required). So a payload carrying `cursor_version`
   gets a reply in Cursor's schema: `{"permission":"allow"}` for subagent start, `{}` for session start; Cursor reads
   dtk's rules from its own rule file instead.

## Devin (Devin Local and Devin CLI)

1. The matcher `exec` selects the shell tool: `dotnet build` becomes `dtk dotnet build` in `devin` and in Devin Desktop.
2. Which shell runs the hook command on Windows.
3. Whether a workspace in Restricted Mode runs the project hook.
4. Whether the legacy Cascade agent reads .devin/rules (dtk removes its own .windsurf/rules/dtk.md).
5. Devin CLI runs the hooks of Claude Code's settings files under the same event names, and its `SessionStart`
   honours `hookSpecificOutput.additionalContext` (docs.devin.ai, CLI › Hooks); it has no `SubagentStart`, and output
   it cannot read is logged, never blocking. So dtk's Claude Code context hook should give Devin sessions the rules in
   a .NET project: whether it does.

## Factory Droid

1. `dotnet build` becomes `dtk dotnet build` in `droid` on the current release.
2. Which shell runs the hook command on Windows.
3. With the user's own `settings.json` `PreToolUse` hooks already registered, both theirs and dtk's run.

## Amp

1. `dotnet build` becomes `dtk dotnet build` in `amp`.
2. Which input field the shell tool actually holds the command in (`cmd` or `command`).
3. Whether a `{ action: "allow" }` reply from dtk's plugin overrides another plugin's `reject-and-continue` for the
   same call, rather than leaving it in force as intended.
4. Whether the plugin can spawn `dtk hook amp` under Amp's Bun runtime on Windows.
