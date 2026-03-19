---
_layout: landing
---

# DotnetTokenKiller

A .NET CLI proxy that reduces LLM token usage by filtering the verbose output of `dotnet` commands down to only what matters.

When you feed `dotnet build` or `dotnet test` output to an LLM, most of it is noise — SDK banners, MSBuild headers, progress lines, ANSI escape codes, duplicate error messages. **DTK strips all of that and returns a compact, signal-only result.** Fewer tokens in means lower cost and less context consumed.

## Quick Start

```sh
# Install
dotnet tool install -g DotnetTokenKiller

# Use — just prefix any supported dotnet command with dtk
dtk dotnet build
dtk dotnet test
dtk dotnet restore
dtk dotnet clean
```

## Key Features

- **Build filtering** — strips MSBuild noise, keeps only errors, warnings, and a compact summary
- **Test filtering** — removes xUnit/NUnit/MSTest adapter banners, license warnings, and reflection stack frames; shows only failures with clean relative paths
- **Restore/Clean filtering** — condenses restore and clean output to essentials
- **Token analytics** — tracks per-command token savings over time with `dtk gain`
- **Log teeing** — optionally saves raw output to disk for post-mortem inspection
- **AI agent integration** — pre-built Claude Code hook auto-rewrites `dotnet` commands to `dtk`

## How It Works

DTK intercepts `dotnet` subcommands, runs them, and applies per-command output filters before returning the result. The filtered output is what your LLM — or you — actually needs:

| Command | Typical Savings |
|---------|----------------|
| build   | ~78%           |
| test    | ~84%           |
| clean   | ~98%           |
| restore | ~47%           |

## Documentation

- [Getting Started](articles/getting-started.md) — installation and first use
- [Usage Guide](articles/usage.md) — all supported commands and flags
- [Configuration](articles/configuration.md) — customize DTK behavior via JSON config
- [Token Analytics](articles/token-analytics.md) — track and view your savings
- [Output Examples](articles/examples/index.md) — side-by-side raw vs filtered output
- [Architecture](articles/architecture.md) — how the codebase is organized
- [AI Agent Setup](articles/ai-agent-setup.md) — integrate DTK with Claude Code and other agents
