---
_layout: landing
---

# DotnetTokenKiller

A .NET CLI proxy that reduces LLM token usage by filtering the verbose output of `dotnet` commands down to only what matters.

When you feed `dotnet build` or `dotnet test` output to an LLM, most of it is noise — SDK banners, MSBuild headers, progress lines, ANSI escape codes, duplicate error messages. **DTK strips all of that and returns a compact, signal-only result.** Fewer tokens in means lower cost and less context consumed.

## Quick Start

```sh
# Install (requires .NET 10 SDK — full SDK, not just runtime)
dotnet tool install -g DotnetTokenKiller

# Use — just prefix any supported dotnet command with dtk
dtk dotnet build
dtk dotnet test
dtk dotnet restore
dtk dotnet clean
dtk dotnet format
dtk dotnet list package --outdated
```

## Key Features

- **Build filtering** — strips MSBuild noise, keeps only errors, warnings, and a compact summary (~78% savings)
- **Test filtering** — removes xUnit/NUnit/MSTest adapter banners, license warnings, and reflection stack frames; shows only failures with clean relative paths (~84% savings)
- **Restore/Clean filtering** — condenses restore and clean output to essentials (~47–98% savings)
- **Format filtering** — shows only violations with workspace-relative paths
- **`list package` filtering** — collapses per-TFM duplication across plain, `--outdated`,
  `--deprecated`, and `--vulnerable` (~80.9% savings)
- **Token analytics** — tracks per-command token savings over time with `dtk gain`
- **Log teeing** — optionally saves raw output to disk for post-mortem inspection
- **AI agent integration** — `dtk integrate` installs hooks for 7 providers: Claude Code, GitHub Copilot, Gemini CLI, Cursor, Windsurf, Aider, and JetBrains AI
- **Self-diagnostics** — `dtk doctor` validates your setup in one command
- **Shell completion** — bash, zsh, fish, and PowerShell via `dtk completion`

## How It Works

DTK intercepts `dotnet` subcommands, runs them, and applies per-command output filters before returning the result. The filtered output is what your LLM — or you — actually needs:

| Command | Typical Savings |
|---------|----------------|
| build   | ~78%           |
| test    | ~84%           |
| clean   | ~98%           |
| restore | ~47%           |
| format  | varies         |
| list package | ~80.9%    |

## Documentation

- [Getting Started](articles/getting-started.md) — installation and first use
- [Usage Guide](articles/usage.md) — all supported commands and flags
- [Configuration](articles/configuration.md) — customize DTK behavior via JSON config
- [Token Analytics](articles/token-analytics.md) — track and view your savings
- [Output Examples](articles/examples/index.md) — side-by-side raw vs filtered output
- [Architecture](articles/architecture.md) — how the codebase is organized
- [AI Agent Setup](articles/ai-agent-setup.md) — integrate DTK with Claude Code, Copilot, Gemini, Cursor, Windsurf, Aider, or JetBrains AI
