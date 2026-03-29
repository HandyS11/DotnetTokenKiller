# AI Agent Setup

DTK integrates with AI coding agents to automatically reduce token usage from `dotnet` commands.

> [!IMPORTANT]
> **Python 3 requirement**: The Claude Code and Gemini CLI integrations install Python-based hooks that run at command interception time. Make sure `python3` is available on your `PATH` before using `dtk integrate claude` or `dtk integrate gemini`. Other providers (Copilot, Cursor, Windsurf, Aider, JetBrains) do not require Python.

## Claude Code

A pre-built hook automatically rewrites `dotnet build|test|restore|clean` commands to use `dtk`.

### Installation

From your project root, run:

```sh
dtk integrate claude
```

This creates three files:

- `.claude/skills/dotnet-token-killer/SKILL.md` — instructs Claude Code to prefer `dtk`
- `.claude/hooks/dotnet-to-dtk.py` — the Python rewrite hook
- `.claude/settings.json` — registers the hook under `PreToolUse` (merges with any existing settings)

Re-running the command is safe: existing files are skipped. Use `--force` to overwrite:

```sh
dtk integrate claude --force
```

To target a directory other than the current one:

```sh
dtk integrate claude --dir /path/to/project
```

### How It Works

With the hook in place, any time Claude Code runs `dotnet build`, `dotnet test`, `dotnet restore`, or `dotnet clean`, the command is silently rewritten to `dtk dotnet ...` before execution. The agent receives the filtered output without any extra configuration.

### Manual Installation

If you prefer not to use `dtk integrate`, it requires `curl` and `python3`. From your project root:

```sh
mkdir -p .claude/hooks
curl -sSL https://raw.githubusercontent.com/HandyS11/DotnetTokenKiller/develop/.claude/hooks/dotnet-to-dtk.py \
  -o .claude/hooks/dotnet-to-dtk.py
```

Then add the following to `.claude/settings.json`:

```json
{
  "hooks": {
    "PreToolUse": [
      {
        "matcher": "Bash",
        "hooks": [
          {
            "type": "command",
            "command": "python3 .claude/hooks/dotnet-to-dtk.py"
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

This appends a `dtk` instructions section to `.github/copilot-instructions.md`, creating the file if it does not exist. The section is wrapped in `<!-- dtk -->` / `<!-- /dtk -->` markers so re-running the command is safe. Use `--force` to refresh the section:

```sh
dtk integrate copilot --force
```

### Manual Installation

Add to your `.github/copilot-instructions.md`:

```markdown
Use `dtk` instead of raw `dotnet` for build, test, restore, and clean to reduce token usage.

```sh
dtk dotnet build
dtk dotnet test
dtk dotnet restore
dtk dotnet clean
```

## Gemini CLI

A pre-built hook automatically rewrites `dotnet build|test|restore|clean` commands to use `dtk`.

### Installation

From your project root, run:

```sh
dtk integrate gemini
```

This creates three files:

- `GEMINI.md` — appends a `dtk` instructions section (creating the file if it does not exist)
- `.gemini/hooks/dotnet-to-dtk.py` — the Python rewrite hook
- `.gemini/settings.json` — registers the hook under `BeforeTool` (merges with any existing settings)

Re-running the command is safe: existing files are skipped. Use `--force` to overwrite:

```sh
dtk integrate gemini --force
```

To target a directory other than the current one:

```sh
dtk integrate gemini --dir /path/to/project
```

### How It Works

With the hook in place, any time Gemini CLI runs `dotnet build`, `dotnet test`, `dotnet restore`, or `dotnet clean`, the command is silently rewritten to `dtk dotnet ...` before execution. The agent receives the filtered output without any extra configuration.

### Manual Installation

If you prefer not to use `dtk integrate`, it requires `curl` and `python3`. From your project root:

```sh
mkdir -p .gemini/hooks
curl -sSL https://raw.githubusercontent.com/HandyS11/DotnetTokenKiller/develop/.gemini/hooks/dotnet-to-dtk.py \
  -o .gemini/hooks/dotnet-to-dtk.py
```

Then add the following to `.gemini/settings.json`:

```json
{
  "hooks": {
    "BeforeTool": [
      {
        "matcher": "run_shell_command",
        "hooks": [
          {
            "type": "command",
            "command": "python3 .gemini/hooks/dotnet-to-dtk.py"
          }
        ]
      }
    ]
  }
}
```

And append the following to your `GEMINI.md`:

```markdown
## DotnetTokenKiller (dtk)

Use `dtk` instead of raw `dotnet` for build, test, restore, and clean commands.
`dtk` filters output to actionable signal only, reducing noise by 50-97%.
```

## Cursor

### Installation

```sh
dtk integrate cursor
```

This creates `.cursor/rules/dtk.mdc` — a Cursor rule file with `alwaysApply: false` that instructs the agent to use `dtk` for dotnet commands.

Use `--force` to overwrite an existing file. Use `--dir` to target a specific project directory.

### How It Works

Cursor loads `.mdc` rule files from `.cursor/rules/` and applies them based on their `alwaysApply` setting. The generated rule tells the agent to prefer `dtk dotnet build|test|restore|clean|format` over raw `dotnet` commands. No hook or Python dependency is needed — it's a plain text instruction file.

### Manual Installation

Create `.cursor/rules/dtk.mdc`:

````markdown
---
alwaysApply: false
---

Use `dtk` instead of raw `dotnet` for build, test, restore, clean, and format commands.
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

Windsurf loads rule files from `.windsurf/rules/` and applies them as system-level instructions. The generated file tells the agent to use `dtk dotnet build|test|restore|clean|format` to reduce token usage. No hook or Python dependency is needed.

### Manual Installation

Create `.windsurf/rules/dtk.md`:

```markdown
Use `dtk` instead of raw `dotnet` for build, test, restore, clean, and format commands.
`dtk` filters output to actionable signal only, reducing noise by 50–97%.
```

## Aider

### Installation

```sh
dtk integrate aider
```

This creates two files:

- `.aider-dtk-instructions.md` — standalone instructions file referenced by Aider
- `.aider.conf.yml` — a `# dtk` / `# /dtk` section is appended (creating the file if needed)

Re-running is safe; use `--force` to refresh the section.

### Manual Installation

Add to your `.aider.conf.yml`:

```yaml
# dtk
read:
  - .aider-dtk-instructions.md
# /dtk
```

And create `.aider-dtk-instructions.md`:

```markdown
Use `dtk` instead of raw `dotnet` for build, test, restore, clean, and format commands.
`dtk` filters output to actionable signal only, reducing noise by 50–97%.
```

### How It Works

Aider reads configuration from `.aider.conf.yml`, which can reference additional instruction files via the `read:` key. The integration adds a reference to `.aider-dtk-instructions.md`, which tells Aider to prefer `dtk` over raw `dotnet` commands. No hook or Python dependency is needed beyond Aider's own Python runtime.

## JetBrains AI

### Installation

```sh
dtk integrate jetbrains
```

This appends a `<!-- dtk -->` / `<!-- /dtk -->` instructions section to `.junie/guidelines.md`, creating the file if it does not exist. Re-running is safe; use `--force` to refresh the section.

### How It Works

JetBrains AI (including Junie) reads project guidelines from `.junie/guidelines.md`. The integrated section instructs the agent to use `dtk` for all supported dotnet commands. The `<!-- dtk -->` markers allow safe re-generation without affecting other content in the guidelines file.

### Manual Installation

Add to your `.junie/guidelines.md`:

````markdown
<!-- dtk -->
## DotnetTokenKiller (dtk)

Use `dtk` instead of raw `dotnet` for build, test, restore, clean, and format commands.
`dtk` filters output to actionable signal only, reducing noise by 50–97%.

```sh
dtk dotnet build
dtk dotnet test
dtk dotnet restore
dtk dotnet clean
dtk dotnet format
```
<!-- /dtk -->
````

## Other Agents

For any AI agent that runs terminal commands, the general approach is:

1. Install DTK globally: `dotnet tool install -g DotnetTokenKiller`
2. Configure the agent to prefix `dotnet build|test|restore|clean` with `dtk`
3. The agent receives compact, filtered output — reducing token usage by 50–98%
