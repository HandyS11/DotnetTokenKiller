# DotnetTokenKiller

A .NET CLI proxy that reduces LLM token usage by filtering the verbose output of `dotnet` commands
down to only what matters. Prefix `build`, `test`, `restore`, `clean`, `format`, and `list package`
with `dtk` for 60–90% fewer tokens — per-command filters, token analytics, and one-command setup for
8 AI coding agents.

## Why

When AI coding agents run `dotnet build` or `dotnet test`, the output is packed with noise — SDK
banners, MSBuild headers, progress indicators, ANSI escape codes, and framework internals. A typical
`dotnet test` run can produce 200+ lines where only 5–10 actually matter. Every extra line burns
tokens, inflates cost, and wastes context window space. DTK strips the noise and returns a compact,
signal-only result.

## Install

Requires the .NET 10 SDK (full SDK, not just the runtime).

```sh
dotnet tool install -g DotnetTokenKiller
```

Update or uninstall:

```sh
dotnet tool update -g DotnetTokenKiller
dotnet tool uninstall -g DotnetTokenKiller
```

## Usage

Prefix any supported `dotnet` command with `dtk`:

```sh
dtk dotnet build
dtk dotnet test --filter "Category=Unit"
dtk dotnet restore
dtk dotnet clean
dtk dotnet format
dtk dotnet list package --outdated
```

Unknown subcommands pass through to `dotnet` unchanged.

## Features

- Build filtering — keeps only errors, warnings, and a compact summary (~78% savings)
- Test filtering — removes adapter banners, license warnings, and reflection stack frames (~84% savings)
- Restore/Clean filtering — condenses output to essentials (~47–98% savings)
- Format filtering — shows only violations with workspace-relative paths
- `list package` filtering — collapses per-TFM duplication across plain, `--outdated`,
  `--deprecated`, and `--vulnerable` (~80.9% savings)
- 8 AI agent integrations — Claude Code, GitHub Copilot, GitHub Copilot CLI, Gemini CLI, Cursor, Windsurf, Aider, JetBrains AI
- Token analytics — tracks per-command savings over time with `dtk gain`
- Self-diagnostics — `dtk doctor` validates your setup in one command
- Shell completion — bash, zsh, fish, and PowerShell

## AI Agent Setup

Install integration artifacts with one command. Installing globally is the recommended way — do it
once and every project your agent touches picks it up automatically:

```sh
dtk integrate claude --global   # ~/.claude
```

`--global` is supported for `claude`, `gemini`, `aider`, and `copilot-cli`. The other providers
(`copilot`, `cursor`, `windsurf`, `jetbrains`) are repository-scoped — run `dtk integrate <provider>`
inside the project.

## Documentation

- [Getting Started](https://handys11.github.io/DotnetTokenKiller/articles/getting-started.html)
- [Full documentation](https://handys11.github.io/DotnetTokenKiller/)
- [Source & issues](https://github.com/HandyS11/DotnetTokenKiller)

Licensed under the MIT License.
