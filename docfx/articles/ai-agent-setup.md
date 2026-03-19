# AI Agent Setup

DTK integrates with AI coding agents to automatically reduce token usage from `dotnet` commands.

## Claude Code

A pre-built hook automatically rewrites `dotnet build|test|restore|clean` commands to use `dtk`.

### Installation

It requires `curl` and `python3`. From your project root:

```sh
mkdir -p .claude/hooks
curl -sSL https://raw.githubusercontent.com/HandyS11/DotnetTokenKiller/develop/.claude/hooks/dotnet-to-dtk.py \
  -o .claude/hooks/dotnet-to-dtk.py
```

If you don't have `curl`, manually download [dotnet-to-dtk.py](https://github.com/HandyS11/DotnetTokenKiller/blob/develop/.claude/hooks/dotnet-to-dtk.py) and place it at `.claude/hooks/dotnet-to-dtk.py`.

### Configuration

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
            "command": "python3 .claude/hooks/dotnet-to-dtk.py"
          }
        ]
      }
    ]
  }
}
```

### How It Works

With the hook in place, any time Claude Code runs `dotnet build`, `dotnet test`, `dotnet restore`, or `dotnet clean`, the command is silently rewritten to `dtk dotnet ...` before execution. The agent receives the filtered output without any extra configuration.

## GitHub Copilot (VS Code)

For GitHub Copilot in VS Code, you can instruct it to use DTK via a custom instructions file. Add to your `.github/copilot-instructions.md`:

```markdown
Use `dtk` instead of raw `dotnet` for build, test, restore, and clean to reduce token usage.

```bash
dtk dotnet build
dtk dotnet test
dtk dotnet restore
dtk dotnet clean
```​
```

## Other Agents

For any AI agent that runs terminal commands, the general approach is:

1. Install DTK globally: `dotnet tool install -g DotnetTokenKiller`
2. Configure the agent to prefix `dotnet build|test|restore|clean` with `dtk`
3. The agent receives compact, filtered output — reducing token usage by 50–98%
