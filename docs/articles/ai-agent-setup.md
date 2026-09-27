# AI Agent Setup

Each supported agent gets a hook that rewrites `dotnet …` to `dtk dotnet …` before the command runs, so the filtering happens whether or not the agent remembers to ask for it. One `dtk init` command installs it (`dtk integrate` is an alias).

> [!NOTE]
> The hooks run `dtk hook <provider>`, so the agent only needs `dtk` on its `PATH` — no Python, no `jq`,
> and the same registration works from sh, bash, Git Bash and PowerShell.

## Installing globally

Pass `--global` (`-g`) to install into your home directory instead of a project, so the integration applies across every project you touch:

```sh
dtk init claude      --global   # ~/.claude
dtk init gemini      --global   # ~/.gemini
dtk init codex       --global   # ~/.codex (or $CODEX_HOME), ~/.agents/skills
dtk init opencode    --global   # ~/.config/opencode, ~/.agents/skills
dtk init antigravity --global   # ~/.gemini/config, ~/.gemini/GEMINI.md
dtk init pi          --global   # <pi agent dir> (~/.pi/agent or $PI_CODING_AGENT_DIR), ~/.agents/skills
dtk init oh-my-pi    --global   # ~/.omp/agent (or $PI_CODING_AGENT_DIR), ~/.agents/skills
dtk init aider       --global   # ~/.aider.conf.yml
dtk init copilot-cli --global   # ~/.copilot/hooks
dtk init cursor      --global   # ~/.cursor/hooks.json (hook only)
dtk init devin       --global   # ~/.config/devin/config.json, global_rules.md
dtk init droid       --global   # ~/.factory, ~/.agents/skills
dtk init crush       --global   # ~/.config/crush, ~/.agents/skills
```

`--global` is supported only for the providers with a home config — **claude**, **gemini**, **codex**, **opencode**, **antigravity**, **pi**, **oh-my-pi**, **aider**, **copilot-cli**, **cursor**, **devin**, **droid**, and **crush** — and cannot be combined with `--dir`. Every other provider below is repository-scoped.

## Uninstalling

`--uninstall` removes what `dtk init <provider>` installed, in the project (or `--dir`) or, with `--global`, in your
home config. Run it before `dotnet tool uninstall -g DotnetTokenKiller`, so no harness is left calling a `dtk` that is
gone:

```sh
dtk init claude --uninstall
dtk init codex --global --uninstall
```

It removes only dtk's own parts, and reports each file as `removed`, `unchanged` or `kept`:

- **Hook registrations** merged into a settings file (`settings.json`, `hooks.json`): only dtk's entries — the same
  ones `dtk init` treats as its own, including a Python-era `dotnet-to-dtk.py` entry — are removed; a matcher group,
  event or hook group left empty goes with them, and a file left as `{}` is deleted.
- **Instruction sections** (`AGENTS.md`, `GEMINI.md`, `.github/copilot-instructions.md`, `.junie/guidelines.md`,
  `.aider.conf.yml`): only the section between dtk's markers is removed; the file is deleted when nothing else is left
  in it. For Aider, the instructions file is also taken back out of your own `read:` key when `dtk init` merged it
  there.
- **Generated files** (`SKILL.md`, the OpenCode plugin, the pi and oh-my-pi extensions, Copilot CLI's
  `dtk-dotnet.json`, the Cursor and Devin rules, `.aider-dtk-instructions.md`): deleted only when dtk can prove the
  content is its own — a provenance stamp that still verifies, or content identical to what this dtk or an earlier
  release wrote. An edited file is `kept`, with a note; to remove it anyway, run `dtk init <provider> --force` to
  restore dtk's version, then `--uninstall`.

Directories the removal leaves empty are deleted too, up to the project or home directory. A second run finds nothing
to remove. `--force` cannot be combined with `--uninstall`.

Some things are deliberately left alone:

- **Shared instructions still in use.** Codex CLI, OpenCode, Antigravity CLI, pi and oh-my-pi share `AGENTS.md` and
  the `.agents/skills` skill; Gemini CLI and Antigravity CLI share `~/.gemini/GEMINI.md`; GitHub Copilot and Copilot CLI
  share `.github/copilot-instructions.md`. While another of these still has its dtk hook registered in the same scope,
  the shared file keeps dtk's section and is reported `unchanged`, with a note naming that provider. (GitHub Copilot
  has no hook, so it never holds the file back for Copilot CLI.)
- **Other tools' configs.** If `dtk init` excluded `dotnet` in rtk's `config.toml`, the exclusion stays — dtk cannot
  tell whether it or you added it — and a note says how to remove it.
- **Codex's `config.toml`.** dtk never writes it; the approval Codex recorded for dtk's hook stays there. Codex keys
  approvals by position in `hooks.json`, so hooks that followed dtk's may need approving again under `/hooks`.

A settings file dtk merged into is rewritten in the same format `dtk init` writes it in, so hand-formatting there is
not preserved.

## Claude Code

A pre-built hook automatically rewrites `dotnet build|test|restore|clean|format|list package|publish|pack` commands to use `dtk`.

### Installation

From your project root, run:

```sh
dtk init claude
```

This creates two files:

- `.claude/skills/dotnet-token-killer/SKILL.md` — instructs Claude Code to prefer `dtk`
- `.claude/settings.json` — registers `dtk hook claude` under `PreToolUse` (merges with any existing settings)

Re-running the command refreshes `SKILL.md` if dtk wrote it and leaves an edited copy alone unless you
pass `--force`. `.claude/settings.json` is always merged: the hook entry is added if missing, and a
registration from an older dtk that ran `dotnet-to-dtk.py` is replaced in place — never duplicated. The
old `.claude/hooks/dotnet-to-dtk.py` is deleted when that run replaced its registration, no hook in
`.claude/settings.json` or `.claude/settings.local.json` still runs it, and dtk can prove it wrote it.
Otherwise it is kept and a note says why; for an edited copy, `--force` on the migrating run deletes it too:

```sh
dtk init claude --force
```

To target a directory other than the current one:

```sh
dtk init claude --dir /path/to/project
```

### How It Works

With the hook in place, any time Claude Code runs `dotnet build`, `dotnet test`, `dotnet restore`, `dotnet clean`, `dotnet format`, `dotnet list package`, `dotnet publish`, or `dotnet pack`, the command is silently rewritten to `dtk dotnet ...` before execution. The agent receives the filtered output without any extra configuration.

### Manual Installation

Add the following to `.claude/settings.json`:

```json
{
  "hooks": {
    "PreToolUse": [
      {
        "matcher": "Bash",
        "hooks": [
          {
            "type": "command",
            "command": "dtk hook claude"
          }
        ]
      }
    ]
  }
}
```

## GitHub Copilot (VS Code)

### Installation

From your project root, run:

```sh
dtk init copilot
```

This creates `.github/copilot-instructions.md` with a `dtk` instructions section, wrapped in `<!-- dtk -->` / `<!-- /dtk -->` markers, if the file does not exist yet. If the file already exists, it is left completely untouched unless you pass `--force`. With `--force`, the section is merged in: an existing dtk section (identified by the markers) is replaced in place, or the section is appended after your existing content if no dtk section is present yet:

```sh
dtk init copilot --force
```

### Manual Installation

Add to your `.github/copilot-instructions.md`, wrapped in `<!-- dtk -->` / `<!-- /dtk -->`
markers so a future `dtk init copilot --force` can safely replace just this section:

````markdown
<!-- dtk -->
## DotnetTokenKiller (dtk)

Use `dtk` instead of raw `dotnet` for build, test, restore, clean, format, list package, publish, and pack commands.
`dtk` filters output to actionable signal only, reducing noise by 50-97%.

```sh
dtk dotnet build
dtk dotnet test
dtk dotnet restore
dtk dotnet clean
dtk dotnet format
dtk dotnet list package --outdated
dtk dotnet publish -c Release
dtk dotnet pack
```
<!-- /dtk -->
````

## GitHub Copilot CLI

### Installation

```sh
dtk init copilot-cli
```

This creates `.github/hooks/dtk-dotnet.json`, which registers the `preToolUse` hook, and a dtk section in
`.github/copilot-instructions.md`. `dtk init copilot-cli --global` writes the hook to `~/.copilot/hooks/`.

### Approval

When the hook rewrites a command it also replies with a `permissionDecision`, and `allow` skips Copilot CLI's own
confirmation. dtk replies `allow` only for a single, plain `dotnet build`, `dotnet test`, `dotnet restore`,
`dotnet clean`, `dotnet format` or `dotnet list package` invocation: the command must start with `dotnet` and hold
nothing but words, quoted text and blanks. Everything else it rewrites gets `ask`, so Copilot CLI prompts as usual:

- `dotnet publish` and `dotnet pack`, which write artifacts and, with a publish profile, can deploy;
- a command with an environment-variable prefix (`FOO=1 dotnet build`) or any other word before `dotnet`;
- a chained, piped or backgrounded command (`;`, `&&`, `||`, `|`, `&`), a subshell, or a line break;
- a redirection (`>`, `<`, here-documents), a command substitution (`$(…)` or backticks, even inside double
  quotes), a `${…}` expansion, a `!` outside single quotes (history expansion), a comment, or an unterminated quote.

A command dtk does not rewrite gets no reply, which leaves it to your own Copilot CLI policy.

### Manual Installation

Create `.github/hooks/dtk-dotnet.json`. Copilot CLI denies the tool call when a hook exits non-zero, so
`; exit 0` keeps a missing `dtk` from blocking every command:

```json
{
  "version": 1,
  "hooks": {
    "preToolUse": [
      {
        "type": "command",
        "matcher": "bash",
        "bash": "dtk hook copilot-cli; exit 0",
        "powershell": "dtk hook copilot-cli; exit 0",
        "timeoutSec": 10
      }
    ]
  }
}
```

## Gemini CLI

A pre-built hook automatically rewrites `dotnet build|test|restore|clean|format|list package|publish|pack` commands to use `dtk`.

### Installation

From your project root, run:

```sh
dtk init gemini
```

This creates two files:

- `GEMINI.md` — a `dtk` instructions section, created if the file does not exist yet
- `.gemini/settings.json` — registers `dtk hook gemini; exit 0` under `BeforeTool` (merges with any existing settings)

Re-running the command replaces `GEMINI.md`'s dtk section only with `--force`; without it, an
existing `GEMINI.md` is left untouched. `.gemini/settings.json` is always merged: the hook entry
is added if missing, and a registration from an older dtk that ran `dotnet-to-dtk.py` is replaced
in place — never duplicated. The old `.gemini/hooks/dotnet-to-dtk.py` is deleted when that run
replaced its registration, no hook left in `.gemini/settings.json` still runs it, and dtk can prove it
wrote it. Otherwise it is kept and a note says why; for an edited copy, `--force` on the migrating run
deletes it too:

```sh
dtk init gemini --force
```

To target a directory other than the current one:

```sh
dtk init gemini --dir /path/to/project
```

### How It Works

With the hook in place, any time Gemini CLI runs `dotnet build`, `dotnet test`, `dotnet restore`, `dotnet clean`, `dotnet format`, `dotnet list package`, `dotnet publish`, or `dotnet pack`, the command is silently rewritten to `dtk dotnet ...` before execution. The agent receives the filtered output without any extra configuration.

dtk always replies `{"decision":"allow", ...}`, but that does not bypass your own confirmation: Gemini CLI's
`BeforeTool` hook contract only treats `"ask"` and `"deny"`/`"block"` specially, so `"allow"` is inert there and
the policy engine (your trust rules and approval mode) still decides whether the rewritten command runs.

### Manual Installation

Add the following to `.gemini/settings.json`. Gemini CLI blocks the shell command when a hook exits
with a code other than 0 or 1, so `; exit 0` keeps a missing `dtk` from blocking every command:

```json
{
  "hooks": {
    "BeforeTool": [
      {
        "matcher": "run_shell_command",
        "hooks": [
          {
            "type": "command",
            "command": "dtk hook gemini; exit 0"
          }
        ]
      }
    ]
  }
}
```

And append the following to your `GEMINI.md`, wrapped in `<!-- dtk -->` / `<!-- /dtk -->`
markers so a future `dtk init gemini --force` can safely replace just this section without
touching the rest of the file:

```markdown
<!-- dtk -->
## DotnetTokenKiller (dtk)

Use `dtk` instead of raw `dotnet` for build, test, restore, clean, format, list package, publish, and pack commands.
`dtk` filters output to actionable signal only, reducing noise by 50-97%.
<!-- /dtk -->
```

## Codex CLI

A `PreToolUse` hook rewrites `dotnet build|test|restore|clean|format|list package|publish|pack` commands to use `dtk`. It needs
Codex CLI 0.131 or later, the first release whose hooks can change a command, and was verified against Codex CLI 0.154.

### Installation

From your project root, run:

```sh
dtk init codex
```

This creates three files:

- `AGENTS.md` — a `dtk` instructions section, created if the file does not exist yet; an existing `AGENTS.md`
  gets the section only when you pass `--force`, and keeps the rest of its content
- `.agents/skills/dotnet-token-killer/SKILL.md` — the dtk skill, which Codex loads when it is relevant
- `.codex/hooks.json` — registers `dtk hook codex` under `PreToolUse` (merges with any existing hooks)

`dtk init codex --global` writes `~/.codex/AGENTS.md` and `~/.codex/hooks.json` (under `$CODEX_HOME` when it is
set) and `~/.agents/skills/dotnet-token-killer/SKILL.md`. The `AGENTS.md` section and the skill are shared: providers
that write the same files leave one copy of each.

In a linked git worktree, Codex reads hooks from the main checkout's `.codex/` folder, so run `dtk init codex` in the
main checkout (or commit `.codex/hooks.json`).

### Approving the hook

Codex runs a hook only after you approve that exact definition. Open Codex after `dtk init codex`: it lists the new
hook for review at startup, and `/hooks` shows it at any time. Until you approve it, `codex exec` skips the hook
without saying so and commands run unrewritten. Codex also reads a project's `.codex/` folder only once you trust the
project. `dtk doctor` warns while either is missing, and while the hook is turned off under `/hooks`.

dtk does not approve the hook for you: the approval is Codex's record that you reviewed what runs before every shell
command.

### How It Works

Before each shell command, Codex sends it to `dtk hook codex`, which replies with `dtk dotnet …` for a matching
`dotnet …` command. Codex still applies its approval policy and sandbox to the rewritten command. A "don't ask again"
approval you saved for a `dotnet …` command does not match the rewritten `dtk dotnet …` command, so Codex may ask
again once.

### Manual Installation

Add the following to `.codex/hooks.json` (or `~/.codex/hooks.json`). Codex runs the original command when a hook
fails, so the command needs no guard:

```json
{
  "hooks": {
    "PreToolUse": [
      {
        "matcher": "Bash",
        "hooks": [
          {
            "type": "command",
            "command": "dtk hook codex",
            "timeout": 10
          }
        ]
      }
    ]
  }
}
```

Then approve it in Codex under `/hooks`.

## OpenCode

OpenCode runs plugins rather than hook commands, so dtk installs a small plugin that rewrites
`dotnet build|test|restore|clean|format|list package|publish|pack` commands to use `dtk`. It needs OpenCode 1.x, not the v2 beta,
and was verified against OpenCode 1.18.31.

### Installation

From your project root, run:

```sh
dtk init opencode
```

This creates three files:

- `AGENTS.md` — a `dtk` instructions section, created if the file does not exist yet (an existing `AGENTS.md`
  gets it only with `--force`)
- `.agents/skills/dotnet-token-killer/SKILL.md` — the dtk skill
- `.opencode/plugins/dtk.js` — the plugin

`dtk init opencode --global` writes `~/.config/opencode/AGENTS.md` and `~/.config/opencode/plugins/dtk.js` (under
`$XDG_CONFIG_HOME` when it is set) and `~/.agents/skills/dotnet-token-killer/SKILL.md`. OpenCode also reads
`.claude/skills/`, so a project set up for Claude Code too logs a harmless duplicate-skill warning.

Re-running the command refreshes `dtk.js` if dtk wrote it and leaves an edited copy alone unless you pass `--force`.

### How It Works

Before OpenCode runs a `bash` command that mentions `dotnet`, the plugin passes it to `dtk hook opencode` and runs
the `dtk dotnet …` command it gets back. Other commands never start `dtk`. The plugin looks for `dtk` on `PATH` only,
never in the project directory. If `dtk` is not on `PATH`, fails or takes longer than five seconds, the original command
runs unchanged. OpenCode checks its permission rules against the rewritten
command.

## pi

`dtk init pi` writes a generated extension, `.pi/extensions/dtk.js`, plus the shared `AGENTS.md` section and the
`.agents/skills/dotnet-token-killer` skill. `--global` writes `<pi agent dir>/extensions/dtk.js` and
`<pi agent dir>/AGENTS.md`, where `<pi agent dir>` is `$PI_CODING_AGENT_DIR` (a leading `~` is expanded) and defaults to
`~/.pi/agent`, and the skill to `~/.agents/skills`.

The extension handles pi's `tool_call` event: for a `bash` call whose command contains `dotnet`, it asks
`dtk hook pi` for the rewrite and changes the command before pi runs it. It finds `dtk` on `PATH` itself and never
blocks a tool call: if dtk is missing, slow or fails, the command runs unchanged. Commands you type with `!` are not
rewritten.

pi 0.73 and later load `.pi/extensions` only in a trusted project: approve it when pi asks, or run `/trust`. Print,
JSON and RPC modes (`pi -p`) skip project extensions until then, so use `--global` for those. Requires pi 0.73.1 or
later.

If rtk's pi extension (`rtk init --agent pi`) is installed, `dtk init pi` adds `dotnet` to rtk's `exclude_commands`
so dtk owns dotnet commands.

## oh-my-pi

`dtk init oh-my-pi` writes the same extension to `.omp/extensions/dtk.js` (`--global`: `~/.omp/agent/extensions/dtk.js`
and `~/.omp/agent/AGENTS.md`), plus the shared `AGENTS.md` section and skill. oh-my-pi does not read pi's `.pi`
folder, so install each harness you use. Requires oh-my-pi 18.2.1 or later; older versions load the extension but
run commands unchanged.

Without a profile (`OMP_PROFILE` and `PI_PROFILE` unset), oh-my-pi also honors `PI_CODING_AGENT_DIR`, and so does
`dtk init oh-my-pi --global`. When that makes pi and oh-my-pi share one agent directory, they load one extension: `dtk init
oh-my-pi --global` then writes exactly what `dtk init pi --global` writes and says so, its `--uninstall` removes nothing
and points to `dtk init pi --global --uninstall`, and `dtk doctor` reports the extension once, under pi. Named profiles
(`~/.omp/profiles/<name>/agent`) and `PI_CONFIG_DIR` are not supported: dtk installs into the default profile only.

oh-my-pi's shell minimizer (`shellMinimizer.enabled`, on by default) has its own `dotnet` filter, chosen by program
name. Once a command runs as `dtk dotnet …` that filter no longer applies, so the output is filtered once, by dtk.
dtk does not change oh-my-pi's `config.yml`. To have oh-my-pi leave other commands unfiltered too, list them in
`shellMinimizer.except`.

## Antigravity CLI

A `PreToolUse` hook rewrites `dotnet build|test|restore|clean|format|list package|publish|pack` commands to use `dtk` in Google's
Antigravity CLI (`agy`). Checked against Antigravity CLI 1.2.3 on Linux x64 (hook firing and the empty reply also on
1.2.2).

### Installation

From your project root, run:

```sh
dtk init antigravity
```

This creates three files:

- `AGENTS.md` — a `dtk` instructions section, created if the file does not exist yet (an existing `AGENTS.md`
  gets it only with `--force`)
- `.agents/skills/dotnet-token-killer/SKILL.md` — the dtk skill
- `.agents/hooks.json` — a `dtk` hook group running `dtk hook antigravity || exit 0` for `run_command` (other
  hook groups in the file are left alone)

Antigravity loads a workspace's `.agents/hooks.json` only once you trust the workspace.

`dtk init antigravity --global` writes the hook to `~/.gemini/config/hooks.json`, the skill to
`~/.gemini/config/skills/dotnet-token-killer/SKILL.md`, and the instructions section to `~/.gemini/GEMINI.md` — the
same section `dtk init gemini --global` writes, so the two never conflict.

### How It Works

Before Antigravity runs a terminal command, it sends it to `dtk hook antigravity`, which replaces a matching
`dotnet …` command with `dtk dotnet …`. dtk replies `ask`, never `allow`: your permission rules and "Always Allow"
choices still decide whether the command runs. The `|| exit 0` guard keeps a missing `dtk` from blocking terminal
commands, which Antigravity does when a hook fails.

### Permissions and print mode

Antigravity matches your permission rules against the rewritten command: an allow rule such as `command(dotnet)`
does not cover `dtk dotnet build`, so a rewritten command prompts, and approving it with "Always Allow" offers
commands that start with `dtk`. Allow `command(dtk)` wherever you already allow `command(dotnet)` to cover it up
front: `command(dtk)` allows every dtk command, which includes running any `dotnet` command through it, so it
grants no more than `command(dotnet)` already did. dtk itself never writes permission rules.

Print mode (`agy -p`) loads only the global `~/.gemini/config/hooks.json`, never a project's `.agents/hooks.json`, so
headless runs are rewritten only with `dtk init antigravity --global`. Print mode cannot prompt, so the rewritten
command also needs an allow rule of its own to run there — except under `--dangerously-skip-permissions`, which
still runs the rewritten command, with no prompt or denial.

Keep a dtk 0.8.0 or later first on `PATH`: an older dtk prints an error on stdout, and Antigravity blocks a
command when its hook prints something that is not JSON. `dtk doctor`'s hook probe reports it.

### Manual Installation

Add a `dtk` group to `.agents/hooks.json` (or `~/.gemini/config/hooks.json`):

```json
{
  "dtk": {
    "PreToolUse": [
      {
        "matcher": "run_command",
        "hooks": [
          {
            "type": "command",
            "command": "dtk hook antigravity || exit 0",
            "timeout": 10
          }
        ]
      }
    ]
  }
}
```

## Cursor

A `preToolUse` hook rewrites `dotnet build|test|restore|clean|format|list package` commands to use `dtk`, alongside
the existing rule file.

### Installation

```sh
dtk init cursor
```

This creates two files:

- `.cursor/rules/dtk.mdc` — a Cursor rule with `alwaysApply: false` that instructs the agent to prefer `dtk`
- `.cursor/hooks.json` — registers `dtk hook cursor` under `preToolUse` for the `Shell` tool (merges with any
  existing hooks; `version: 1` is added if it is missing)

`dtk init cursor --global` writes only `~/.cursor/hooks.json`: Cursor keeps user rules in its settings UI, not a
file, so the global install has no rule to write — run `dtk init cursor` in a project too for the rule.

Use `--force` to overwrite an existing rule file. Use `--dir` to target a specific project directory.

### How It Works

When the hook fires, Cursor's `preToolUse` reply can carry `permission: "allow"`, which skips its own approval
prompt for the command it rewrote. dtk only takes that shortcut for a command
`DotnetCommandRewriter.IsAutoApprovable` accepts — a single, plain `dotnet build`, `test`, `restore`, `clean`,
`format` or `list package` invocation, the same rule Copilot CLI's `allow` reply uses — so it never auto-approves
something riskier on Cursor's behalf. A chained, piped or backgrounded command, and `dotnet publish`/`pack` (which
write artifacts and, with a publish profile, can deploy), run as written and are left to the rule instead. Every
reply the hook does not rewrite is exactly `{}`, never nothing, because Cursor blocks the tool call on output that
does not match its schema.

Cursor runs project hooks only in trusted workspaces, and `cursor-agent` needs `--trust` to run headless; the
global hook has no such requirement.

Known gaps: Cursor sends the hook no payload in remote Linux workspaces, and may drop rewrites for subagents'
shell calls; those commands run as written, guided only by the rule.

Once `.cursor/hooks.json` is committed, every machine that opens the project needs a dtk release newer than 0.8.0
(the first with `dtk hook cursor`) on `PATH`: an older dtk prints nothing but a usage line on stderr, and Cursor
blocks the tool call — every tool call, not just a missed rewrite — when a `preToolUse` hook's output isn't valid
JSON. Update dtk on every machine before committing the file.

If rtk's own Cursor hook is found — `rtk hook cursor` in `hooks.json`, or its legacy
`~/.cursor/hooks/rtk-rewrite.sh` script — `dtk init cursor` excludes `dotnet` from rtk so the two proxies don't
both rewrite it.

Cursor imports hooks from a Claude settings file (`~/.claude/settings.json`, `.claude/settings*.json`) by default.
When dtk's Claude hook is also registered in a settings file Cursor imports, the install prints a note about it; a
double rewrite is harmless, because the rewriter skips commands `dtk` already ran. `dtk doctor` probes the Cursor
hook with one simple command per auto-approvable subcommand.

> [!NOTE]
> Not verified against a live run — see [Harness verification](harness-verification.md).

### Manual Installation

Create `.cursor/hooks.json` (or `~/.cursor/hooks.json`):

```json
{
  "version": 1,
  "hooks": {
    "preToolUse": [
      {
        "command": "dtk hook cursor",
        "matcher": "Shell",
        "timeout": 10
      }
    ]
  }
}
```

And, in a project, `.cursor/rules/dtk.mdc`:

````markdown
---
alwaysApply: false
---

Use `dtk` instead of raw `dotnet` for build, test, restore, clean, format, list package, publish, and pack commands.
`dtk` filters output to actionable signal only, reducing noise by 50–97%.
````

## Devin (formerly Windsurf)

A `PreToolUse` hook rewrites `dotnet build|test|restore|clean|format|list package|publish|pack` commands to use
`dtk` for Devin Local and Devin CLI, alongside a rule file. `windsurf` is an alias of `devin`: `dtk init windsurf`
runs `dtk init devin` and prints a note saying so.

### Installation

```sh
dtk init devin
```

This creates two files:

- `.devin/rules/dtk.md` — the same rule text `dtk init windsurf` used to write
- `.devin/hooks.v1.json` — the whole file is the hooks object, registering `dtk hook devin` under `PreToolUse` for
  the `exec` tool

If the project has a `.windsurf/rules/dtk.md` an older dtk wrote, this run deletes it — Devin still reads
`.windsurf/rules` as a fallback and would otherwise load both — unless it was edited, in which case it is kept
with a note.

`dtk init devin --global` writes the hook under the `hooks` key of `~/.config/devin/config.json`
(`%APPDATA%\devin\config.json` on Windows) and adds a `<!-- dtk -->` section to Devin Desktop's always-on
`~/.codeium/windsurf/memories/global_rules.md`.

Use `--force` to overwrite an existing rule file. Use `--dir` to target a specific project directory.

### How It Works

Before Devin Local or Devin CLI runs a shell command, it sends it to `dtk hook devin`, which replies with Claude's
`hookSpecificOutput.updatedInput` and no decision, so Devin's own approval still applies to the rewritten command.
The legacy Cascade agent cannot rewrite commands; whether it reads `.devin/rules` is not yet verified (see
[Harness verification](harness-verification.md)).

Devin Desktop runs no hooks while a workspace is in Restricted Mode: trust the workspace for the project hook to
run.

Devin also imports hooks from a Claude settings file (`~/.claude/settings.json`, `.claude/settings*.json`) by
default; when dtk's Claude hook is registered there too, the install prints a note — a double rewrite is harmless,
because the rewriter skips commands `dtk` already ran.

> [!NOTE]
> Not verified against a live run — see [Harness verification](harness-verification.md).

### Manual Installation

Create `.devin/hooks.v1.json`:

```json
{
  "PreToolUse": [
    {
      "matcher": "exec",
      "hooks": [
        {
          "type": "command",
          "command": "dtk hook devin",
          "timeout": 10
        }
      ]
    }
  ]
}
```

And `.devin/rules/dtk.md`:

```markdown
Use `dtk` instead of raw `dotnet` for build, test, restore, clean, format, list package, publish, and pack commands.
`dtk` filters output to actionable signal only, reducing noise by 50–97%.
```

## Factory Droid

A `PreToolUse` hook rewrites `dotnet build|test|restore|clean|format|list package|publish|pack` commands to use
`dtk` in Factory Droid.

### Installation

```sh
dtk init droid
```

This creates:

- `AGENTS.md` — a `dtk` instructions section, created if the file does not exist yet (an existing `AGENTS.md`
  gets it only with `--force`)
- `.agents/skills/dotnet-token-killer/SKILL.md` — the dtk skill
- a `PreToolUse` hook entry for the `Execute` tool, in whichever file Droid actually reads `PreToolUse` from (see
  below)

`dtk init droid --global` writes `~/.factory/AGENTS.md` and `~/.agents/skills/dotnet-token-killer/SKILL.md`, plus
the hook entry under the same `.factory` directory. Set `FACTORY_HOME_OVERRIDE` to use a different home directory
for the global install; `.factory` is still appended to it.

### Where the hook is registered

Droid reads `PreToolUse` from the root `.factory/hooks.json`, merged **per event key** over the `hooks` key of
`.factory/settings.json` (a legacy `.factory/hooks/hooks.json` is read only when the root file is absent). Writing
a `PreToolUse` array into a new `hooks.json` would silently shadow `PreToolUse` hooks the user already keeps in
`settings.json`, so dtk picks the file Droid actually reads `PreToolUse` from, in this order:

1. the live `hooks.json`, when it already defines a non-empty `PreToolUse` — unless that `PreToolUse` holds only
   dtk's own hook and `settings.json`'s is non-empty;
2. else `settings.json`, when its `hooks.PreToolUse` is non-empty;
3. else the live `hooks.json`, when one exists (even without a `PreToolUse` yet);
4. else a new `hooks.json`.

Having written the hook there, `dtk init droid` removes dtk's entry from the other candidate files, deleting a file
left empty, so the hook is registered exactly once, where Droid reads it — for example, when you add a `PreToolUse`
hook to `settings.json` after dtk created a `hooks.json` holding only its own, re-running `dtk init droid` moves
dtk's hook into `settings.json` and deletes that `hooks.json`, which would otherwise shadow your hook.
The reverse also happens: a `PreToolUse` added to `hooks.json` later (by hand or through Droid's `/hooks` UI) takes
precedence over the `settings.json` one holding dtk's hook, so `dtk doctor` flags a hook Droid no longer reads, and
running `dtk init droid` again moves dtk's hook to where Droid reads it.

`dtk init droid --uninstall` removes dtk's entry from all three candidate files — wherever an earlier run or the
user moved it — and leaves every other hook alone. A candidate file that isn't valid JSON is kept as is, with a
note, rather than aborting the whole uninstall: the hook may still live in one of the other files.

Droid snapshots hooks when a session starts, so restart any running `droid` session after installing or
uninstalling for the change to take effect.

### How It Works

Before Droid runs a shell command through its `Execute` tool, it sends it to `dtk hook droid`, which replies with
Claude's `hookSpecificOutput.updatedInput` and no decision, so Droid's own approval and sandboxing still apply to
the rewritten command.

If rtk's own Droid hook (`rtk hook droid`) is found in any of the candidate files, `dtk init droid` excludes
`dotnet` from rtk's config so the two proxies don't both rewrite it.

> [!NOTE]
> Not verified against a live run — see [Harness verification](harness-verification.md).

### Manual Installation

Add to `.factory/hooks.json` (or wherever `PreToolUse` already lives — see above):

```json
{
  "PreToolUse": [
    {
      "matcher": "Execute",
      "hooks": [
        {
          "type": "command",
          "command": "dtk hook droid",
          "timeout": 10
        }
      ]
    }
  ]
}
```

## Crush

A `PreToolUse` hook rewrites `dotnet build|test|restore|clean|format|list package|publish|pack` commands to use
`dtk`, registered as one line in `crushrc`, the Bash script Crush runs to build its configuration.

### Installation

```sh
dtk init crush
```

This creates:

- `AGENTS.md` — a `dtk` instructions section, created if the file does not exist yet (an existing `AGENTS.md`
  gets it only with `--force`)
- `.agents/skills/dotnet-token-killer/SKILL.md` — the dtk skill
- a marked section in `.crushrc` (or in an existing `crushrc`, without the dot, when the project has one and no
  `.crushrc`)

Re-running `dtk init crush` writes back into whichever of `.crushrc` or `crushrc` already holds dtk's section; the
existence rule above only applies the first time, before either file has one. `dtk init crush --global` writes the
same section into `crushrc` under `$CRUSH_GLOBAL_CONFIG`, else `$XDG_CONFIG_HOME/crush`, else `~/.config/crush`
(the same on Windows), plus a section in `CRUSH.md` in that directory and the skill in `~/.agents/skills`.

Requires Crush 0.88.0 or later, the first release that reads `crushrc` with `hook add`; an older Crush never sees
the section. dtk edits only the lines between its markers — the rest of `crushrc` (providers, other hooks, line
endings) is written back exactly as read. A file with a missing, duplicated or out-of-order marker is never
guessed at: install and uninstall both refuse, naming the file, and leave it untouched.

### How It Works

Before Crush runs a `bash` tool call, it sends it to `dtk hook crush`, which replies with its own envelope,
`{"version":1,"updated_input":{"command":"…"}}`, and no decision: unlike Droid's reply, Crush's own permission
prompt still runs for the rewritten command, because only a `decision` of `"allow"` would bypass it and dtk never
sends one. Crush runs hooks for the main agent's tool calls only, so a sub-agent's `dotnet` commands run as
written.

> [!NOTE]
> Verified against the real Crush binary (gate C, see `eng/gates/README.md`).

### Manual Installation

Add to `.crushrc` (or `crushrc`, or the global file):

```sh
# >>> dtk (DotnetTokenKiller) >>>
hook add PreToolUse --name dtk --matcher '^bash$' --command 'dtk hook crush'
# <<< dtk <<<
```

## Aider

### Installation

```sh
dtk init aider
```

This creates two files:

- `.aider-dtk-instructions.md` — standalone instructions file referenced by Aider
- `.aider.conf.yml` — a `# dtk` / `# /dtk` section, created if the file doesn't exist yet

If either file already exists, it is left completely untouched unless you pass `--force`:

```sh
dtk init aider --force
```

When `--force` writes into an existing `.aider.conf.yml`:

- if the file already declares a top-level `read:` key outside the dtk-managed section (either
  flow style, `read: [a, b]`, or block style, `read:\n  - a`), `.aider-dtk-instructions.md` is
  merged into that existing key instead of the `# dtk` section declaring a second `read:` key —
  YAML's last-key-wins semantics would otherwise let the second key silently shadow the first;
- otherwise the `# dtk` section declares its own `read:` key.

Re-running with `--force` is idempotent either way: the merge never adds a duplicate entry for
`.aider-dtk-instructions.md`.

### Manual Installation

Add to your `.aider.conf.yml`:

```yaml
# dtk
read:
  - .aider-dtk-instructions.md
# /dtk
```

If your `.aider.conf.yml` already has a top-level `read:` key, add
`.aider-dtk-instructions.md` to that existing list instead of declaring a second `read:` key.

And create `.aider-dtk-instructions.md`:

```markdown
Use `dtk` instead of raw `dotnet` for build, test, restore, clean, format, list package, publish, and pack commands.
`dtk` filters output to actionable signal only, reducing noise by 50–97%.
```

### How It Works

Aider reads configuration from `.aider.conf.yml`, which can reference additional instruction files via the `read:` key. The integration adds a reference to `.aider-dtk-instructions.md`, merging it into an existing top-level `read:` key when present rather than declaring a second one, which tells Aider to prefer `dtk` over raw `dotnet` commands. No hook is needed.

## JetBrains AI

### Installation

```sh
dtk init jetbrains
```

This creates `.junie/guidelines.md` with a `<!-- dtk -->` / `<!-- /dtk -->` instructions section if the file does not exist yet. If the file already exists, it is left completely untouched unless you pass `--force`. With `--force`, the section is merged in: an existing dtk section is replaced in place, or the section is appended after your existing content if none is present yet.

### How It Works

JetBrains AI (including Junie) reads project guidelines from `.junie/guidelines.md`. The integrated section instructs the agent to use `dtk` for all supported dotnet commands. The `<!-- dtk -->` markers allow safe re-generation without affecting other content in the guidelines file.

### Manual Installation

Add to your `.junie/guidelines.md`:

````markdown
<!-- dtk -->
## DotnetTokenKiller (dtk)

Use `dtk` instead of raw `dotnet` for build, test, restore, clean, format, list package, publish, and pack commands.
`dtk` filters output to actionable signal only, reducing noise by 50–97%.

```sh
dtk dotnet build
dtk dotnet test
dtk dotnet restore
dtk dotnet clean
dtk dotnet format
dtk dotnet list package --outdated
dtk dotnet publish -c Release
dtk dotnet pack
```
<!-- /dtk -->
````

## Other Agents

For any AI agent that runs terminal commands, the general approach is:

1. Install DTK globally: `dotnet tool install -g DotnetTokenKiller`
2. Configure the agent to prefix `dotnet build|test|restore|clean|format|list package|publish|pack` with `dtk`
3. The agent receives compact, filtered output — reducing token usage by 50–98%

## Upgrading dtk

The hooks run the installed `dtk`, so upgrading the tool upgrades the rewrite — a new subcommand is
covered as soon as `dotnet tool update -g DotnetTokenKiller` finishes. Re-run `dtk init <provider>` after
upgrading to refresh the skill and instruction files, and once to migrate a project set up by a dtk that
installed a Python hook:

```sh
dotnet tool update -g DotnetTokenKiller
dtk init claude          # refreshes the skill; migrates a Python hook if one is left
```

dtk stamps the files it generates, so an artifact you have not edited is refreshed without `--force`; one
you have edited is left alone and reported. To check an installation without changing anything, run
`dtk doctor`.
