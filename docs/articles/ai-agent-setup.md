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
dtk init aider       --global   # ~/.aider.conf.yml
dtk init copilot-cli --global   # ~/.copilot/hooks
```

`--global` is supported only for the providers with a home config — **claude**, **gemini**, **codex**, **opencode**, **aider**, and **copilot-cli** — and cannot be combined with `--dir`. Every other provider below is repository-scoped.

## Claude Code

A pre-built hook automatically rewrites `dotnet build|test|restore|clean|format|list package` commands to use `dtk`.

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

With the hook in place, any time Claude Code runs `dotnet build`, `dotnet test`, `dotnet restore`, `dotnet clean`, `dotnet format`, or `dotnet list package`, the command is silently rewritten to `dtk dotnet ...` before execution. The agent receives the filtered output without any extra configuration.

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

Use `dtk` instead of raw `dotnet` for build, test, restore, clean, format, and list package commands.
`dtk` filters output to actionable signal only, reducing noise by 50-97%.

```sh
dtk dotnet build
dtk dotnet test
dtk dotnet restore
dtk dotnet clean
dtk dotnet format
dtk dotnet list package --outdated
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

A pre-built hook automatically rewrites `dotnet build|test|restore|clean|format|list package` commands to use `dtk`.

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

With the hook in place, any time Gemini CLI runs `dotnet build`, `dotnet test`, `dotnet restore`, `dotnet clean`, `dotnet format`, or `dotnet list package`, the command is silently rewritten to `dtk dotnet ...` before execution. The agent receives the filtered output without any extra configuration.

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

Use `dtk` instead of raw `dotnet` for build, test, restore, clean, format, and list package commands.
`dtk` filters output to actionable signal only, reducing noise by 50-97%.
<!-- /dtk -->
```

## Codex CLI

A `PreToolUse` hook rewrites `dotnet build|test|restore|clean|format|list package` commands to use `dtk`. It needs
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
`dotnet build|test|restore|clean|format|list package` commands to use `dtk`. It targets OpenCode 1.x.

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

## Cursor

### Installation

```sh
dtk init cursor
```

This creates `.cursor/rules/dtk.mdc` — a Cursor rule file with `alwaysApply: false` that instructs the agent to use `dtk` for dotnet commands.

Use `--force` to overwrite an existing file. Use `--dir` to target a specific project directory.

### How It Works

Cursor loads `.mdc` rule files from `.cursor/rules/` and applies them based on their `alwaysApply` setting. The generated rule tells the agent to prefer `dtk dotnet build|test|restore|clean|format|list package` over raw `dotnet` commands. No hook is needed — it's a plain text instruction file.

### Manual Installation

Create `.cursor/rules/dtk.mdc`:

````markdown
---
alwaysApply: false
---

Use `dtk` instead of raw `dotnet` for build, test, restore, clean, format, and list package commands.
`dtk` filters output to actionable signal only, reducing noise by 50–97%.
````

## Windsurf

### Installation

```sh
dtk init windsurf
```

This creates `.windsurf/rules/dtk.md` — a Windsurf rule file that instructs the agent to prefer `dtk` over raw `dotnet` commands.

Use `--force` to overwrite. Use `--dir` to target a specific project directory.

### How It Works

Windsurf loads rule files from `.windsurf/rules/` and applies them as system-level instructions. The generated file tells the agent to use `dtk dotnet build|test|restore|clean|format|list package` to reduce token usage. No hook is needed.

### Manual Installation

Create `.windsurf/rules/dtk.md`:

```markdown
Use `dtk` instead of raw `dotnet` for build, test, restore, clean, format, and list package commands.
`dtk` filters output to actionable signal only, reducing noise by 50–97%.
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
Use `dtk` instead of raw `dotnet` for build, test, restore, clean, format, and list package commands.
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

Use `dtk` instead of raw `dotnet` for build, test, restore, clean, format, and list package commands.
`dtk` filters output to actionable signal only, reducing noise by 50–97%.

```sh
dtk dotnet build
dtk dotnet test
dtk dotnet restore
dtk dotnet clean
dtk dotnet format
dtk dotnet list package --outdated
```
<!-- /dtk -->
````

## Other Agents

For any AI agent that runs terminal commands, the general approach is:

1. Install DTK globally: `dotnet tool install -g DotnetTokenKiller`
2. Configure the agent to prefix `dotnet build|test|restore|clean|format|list package` with `dtk`
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
