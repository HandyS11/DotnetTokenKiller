# AI Agent Setup

DTK integrates with AI coding agents to automatically reduce token usage from `dotnet` commands.

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

## Other Agents

For any AI agent that runs terminal commands, the general approach is:

1. Install DTK globally: `dotnet tool install -g DotnetTokenKiller`
2. Configure the agent to prefix `dotnet build|test|restore|clean` with `dtk`
3. The agent receives compact, filtered output — reducing token usage by 50–98%
