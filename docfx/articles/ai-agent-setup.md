# AI Agent Setup

DTK integrates with AI coding agents to automatically reduce token usage from `dotnet` commands.

> [!IMPORTANT]
> **Python 3 requirement**: The Claude Code and Gemini CLI integrations install Python-based hooks that run at command interception time. Make sure `python3` is available on your `PATH` before using `dtk integrate claude` or `dtk integrate gemini`. Other providers (Copilot, Cursor, Windsurf, Aider, JetBrains) do not require Python.

## Installing globally

Pass `--global` (`-g`) to install into your home directory instead of a project, so the integration applies across every project you touch:

```sh
dtk integrate claude      --global   # ~/.claude
dtk integrate gemini      --global   # ~/.gemini
dtk integrate aider       --global   # ~/.aider.conf.yml
dtk integrate copilot-cli --global   # ~/.copilot/hooks
```

`--global` is supported only for the providers with a home config — **claude**, **gemini**, **aider**, and **copilot-cli** — and cannot be combined with `--dir`. Every other provider below is repository-scoped.

## Claude Code

A pre-built hook automatically rewrites `dotnet build|test|restore|clean|format|list package` commands to use `dtk`.

### Installation

From your project root, run:

```sh
dtk integrate claude
```

This creates three files:

- `.claude/skills/dotnet-token-killer/SKILL.md` — instructs Claude Code to prefer `dtk`
- `.claude/hooks/dotnet-to-dtk.py` — the Python rewrite hook
- `.claude/settings.json` — registers the hook under `PreToolUse` (merges with any existing settings)

Re-running the command without `--force` leaves any already-existing files untouched
(`SKILL.md`, the hook script). `.claude/settings.json` is always safely merged: the hook entry
is added if missing, or upgraded in place if it still carries the pre-`$CLAUDE_PROJECT_DIR`
command from an older version of dtk — either way it is never duplicated. To write into
existing `SKILL.md`/hook-script files (replacing them with the latest version), pass `--force`:

```sh
dtk integrate claude --force
```

To target a directory other than the current one:

```sh
dtk integrate claude --dir /path/to/project
```

### How It Works

With the hook in place, any time Claude Code runs `dotnet build`, `dotnet test`, `dotnet restore`, `dotnet clean`, `dotnet format`, or `dotnet list package`, the command is silently rewritten to `dtk dotnet ...` before execution. The agent receives the filtered output without any extra configuration.

### Manual Installation

If you prefer not to use `dtk integrate`, it requires `curl` and `python3`. From your project root:

```sh
mkdir -p .claude/hooks
curl -sSL https://raw.githubusercontent.com/HandyS11/DotnetTokenKiller/develop/.claude/hooks/dotnet-to-dtk.py \
  -o .claude/hooks/dotnet-to-dtk.py
```

Then add the following to `.claude/settings.json`. The command is rooted at
`$CLAUDE_PROJECT_DIR` (the absolute project root Claude Code exports to hooks, quoted so the
path survives spaces) so the hook resolves regardless of Claude's current working directory:

```json
{
  "hooks": {
    "PreToolUse": [
      {
        "matcher": "Bash",
        "hooks": [
          {
            "type": "command",
            "command": "python3 \"$CLAUDE_PROJECT_DIR\"/.claude/hooks/dotnet-to-dtk.py"
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
dtk integrate copilot
```

This creates `.github/copilot-instructions.md` with a `dtk` instructions section, wrapped in `<!-- dtk -->` / `<!-- /dtk -->` markers, if the file does not exist yet. If the file already exists, it is left completely untouched unless you pass `--force`. With `--force`, the section is merged in: an existing dtk section (identified by the markers) is replaced in place, or the section is appended after your existing content if no dtk section is present yet:

```sh
dtk integrate copilot --force
```

### Manual Installation

Add to your `.github/copilot-instructions.md`, wrapped in `<!-- dtk -->` / `<!-- /dtk -->`
markers so a future `dtk integrate copilot --force` can safely replace just this section:

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

## Gemini CLI

A pre-built hook automatically rewrites `dotnet build|test|restore|clean|format|list package` commands to use `dtk`.

### Installation

From your project root, run:

```sh
dtk integrate gemini
```

This creates three files:

- `GEMINI.md` — a `dtk` instructions section, created if the file does not exist yet
- `.gemini/hooks/dotnet-to-dtk.py` — the Python rewrite hook
- `.gemini/settings.json` — registers the hook under `BeforeTool` (merges with any existing settings)

Re-running the command without `--force` leaves any already-existing files untouched
(`GEMINI.md`, the hook script). `.gemini/settings.json` is always safely merged: the hook entry
is added if missing, or upgraded in place if it still carries the pre-`$GEMINI_PROJECT_DIR`
command from an older version of dtk — either way it is never duplicated. To write into an
existing `GEMINI.md` (its dtk section, marked by `<!-- dtk -->` / `<!-- /dtk -->`, is replaced;
the rest of the file is preserved) or the hook script, pass `--force`:

```sh
dtk integrate gemini --force
```

To target a directory other than the current one:

```sh
dtk integrate gemini --dir /path/to/project
```

### How It Works

With the hook in place, any time Gemini CLI runs `dotnet build`, `dotnet test`, `dotnet restore`, `dotnet clean`, `dotnet format`, or `dotnet list package`, the command is silently rewritten to `dtk dotnet ...` before execution. The agent receives the filtered output without any extra configuration.

### Manual Installation

If you prefer not to use `dtk integrate`, it requires `curl` and `python3`. From your project root:

```sh
mkdir -p .gemini/hooks
curl -sSL https://raw.githubusercontent.com/HandyS11/DotnetTokenKiller/develop/.gemini/hooks/dotnet-to-dtk.py \
  -o .gemini/hooks/dotnet-to-dtk.py
```

Then add the following to `.gemini/settings.json`. The command is rooted at
`$GEMINI_PROJECT_DIR` (the absolute project root Gemini CLI exports to hooks, quoted so the
path survives spaces) so the hook resolves regardless of the CLI's current working directory:

```json
{
  "hooks": {
    "BeforeTool": [
      {
        "matcher": "run_shell_command",
        "hooks": [
          {
            "type": "command",
            "command": "python3 \"$GEMINI_PROJECT_DIR\"/.gemini/hooks/dotnet-to-dtk.py"
          }
        ]
      }
    ]
  }
}
```

And append the following to your `GEMINI.md`, wrapped in `<!-- dtk -->` / `<!-- /dtk -->`
markers so a future `dtk integrate gemini --force` can safely replace just this section without
touching the rest of the file:

```markdown
<!-- dtk -->
## DotnetTokenKiller (dtk)

Use `dtk` instead of raw `dotnet` for build, test, restore, clean, format, and list package commands.
`dtk` filters output to actionable signal only, reducing noise by 50-97%.
<!-- /dtk -->
```

## Cursor

### Installation

```sh
dtk integrate cursor
```

This creates `.cursor/rules/dtk.mdc` — a Cursor rule file with `alwaysApply: false` that instructs the agent to use `dtk` for dotnet commands.

Use `--force` to overwrite an existing file. Use `--dir` to target a specific project directory.

### How It Works

Cursor loads `.mdc` rule files from `.cursor/rules/` and applies them based on their `alwaysApply` setting. The generated rule tells the agent to prefer `dtk dotnet build|test|restore|clean|format|list package` over raw `dotnet` commands. No hook or Python dependency is needed — it's a plain text instruction file.

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
dtk integrate windsurf
```

This creates `.windsurf/rules/dtk.md` — a Windsurf rule file that instructs the agent to prefer `dtk` over raw `dotnet` commands.

Use `--force` to overwrite. Use `--dir` to target a specific project directory.

### How It Works

Windsurf loads rule files from `.windsurf/rules/` and applies them as system-level instructions. The generated file tells the agent to use `dtk dotnet build|test|restore|clean|format|list package` to reduce token usage. No hook or Python dependency is needed.

### Manual Installation

Create `.windsurf/rules/dtk.md`:

```markdown
Use `dtk` instead of raw `dotnet` for build, test, restore, clean, format, and list package commands.
`dtk` filters output to actionable signal only, reducing noise by 50–97%.
```

## Aider

### Installation

```sh
dtk integrate aider
```

This creates two files:

- `.aider-dtk-instructions.md` — standalone instructions file referenced by Aider
- `.aider.conf.yml` — a `# dtk` / `# /dtk` section, created if the file doesn't exist yet

If either file already exists, it is left completely untouched unless you pass `--force`:

```sh
dtk integrate aider --force
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

Aider reads configuration from `.aider.conf.yml`, which can reference additional instruction files via the `read:` key. The integration adds a reference to `.aider-dtk-instructions.md`, merging it into an existing top-level `read:` key when present rather than declaring a second one, which tells Aider to prefer `dtk` over raw `dotnet` commands. No hook or Python dependency is needed beyond Aider's own Python runtime.

## JetBrains AI

### Installation

```sh
dtk integrate jetbrains
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

The hook installed in your project carries the list of subcommands dtk filters, so a dtk release
that adds one leaves your installed hook a version behind. Re-run the integration after upgrading:

```sh
dotnet tool update -g DotnetTokenKiller
dtk integrate claude          # refreshes the hook and skill in place
```

dtk stamps the files it generates, so an artifact you have not edited is refreshed without
`--force`; one you have edited is left alone and reported. To check the state of an installation
without changing anything, run `dtk doctor`.
