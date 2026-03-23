<div align="center">

# DotnetTokenKiller

A .NET CLI proxy that reduces LLM token usage by filtering the verbose output of `dotnet` commands down to only what matters.

[![CI](https://github.com/HandyS11/DotnetTokenKiller/actions/workflows/ci.yml/badge.svg)](https://github.com/HandyS11/DotnetTokenKiller/actions/workflows/ci.yml)
[![CD](https://github.com/HandyS11/DotnetTokenKiller/actions/workflows/publish.yml/badge.svg)](https://github.com/HandyS11/DotnetTokenKiller/actions/workflows/publish.yml)
[![License](https://img.shields.io/github/license/HandyS11/DotnetTokenKiller)](./LICENSE)

[![DotnetTokenKiller NuGet](https://img.shields.io/nuget/v/DotnetTokenKiller?label=CLI&logo=nuget)](https://www.nuget.org/packages/DotnetTokenKiller)
[![DotnetTokenKiller Downloads](https://img.shields.io/nuget/dt/DotnetTokenKiller?label=CLI%20downloads&logo=nuget)](https://www.nuget.org/packages/DotnetTokenKiller)

</div>

When you feed `dotnet build` or `dotnet test` output to an LLM, most of it is noise — SDK banners, MSBuild headers, progress lines, ANSI escape codes, duplicate messages. DTK strips all of that and returns a compact, signal-only result. Fewer tokens in means lower cost and less context consumed.

## Installation

```sh
dotnet tool install -g DotnetTokenKiller   # requires .NET 10 SDK
```

## Usage

Prefix any supported `dotnet` command with `dtk`:

```sh
dtk dotnet build
dtk dotnet test --filter "Category=Unit"
dtk dotnet restore
dtk dotnet clean
```

A full test run reduces to:

```sh
FAILURES (1):
  SampleApp.Tests.IntentionallyFailingTests.AlwaysFails [5 ms]
    Intentional failure
    at samples/SampleApp.Tests/IntentionallyFailingTests.cs:line 8
dotnet test: 1 failed, 3 passed (1 project, 0.07s)
```

Unknown subcommands pass through to `dotnet` unchanged.

## AI Agent Setup

Install integration artifacts with one command:

| Provider | Command | What it creates |
|----------|---------|-----------------|
| **Claude Code** | `dtk integrate claude` | Skill file, PreToolUse hook, settings merge |
| **GitHub Copilot** | `dtk integrate copilot` | Section in `.github/copilot-instructions.md` |
| **Gemini CLI** | `dtk integrate gemini` | BeforeTool hook, settings merge, `GEMINI.md` section |
| **Cursor** | `dtk integrate cursor` | `.cursor/rules/dtk.mdc` |
| **Windsurf** | `dtk integrate windsurf` | `.windsurf/rules/dtk.md` |
| **Aider** | `dtk integrate aider` | Instructions file, `.aider.conf.yml` section |
| **JetBrains AI** | `dtk integrate jetbrains` | Section in `.junie/guidelines.md` |

All commands are idempotent — re-running is safe. Pass `--force` to refresh existing files.

See [AI Agent Setup](https://handys11.github.io/DotnetTokenKiller/articles/ai-agent-setup.html) for per-provider details and manual installation steps.

## Token Savings Analytics

```sh
dtk gain               # last 30 days
dtk gain --days 7
dtk gain --project     # current project only
dtk gain --json
```

Example:

```sh
┌─────────┬──────┬──────────────┬──────────────┬───────┬─────────────┐
│ Command │ Runs │ Without Tool │ Used by Tool │ Saved │ Avg Savings │
├─────────┼──────┼──────────────┼──────────────┼───────┼─────────────┤
│ build   │   44 │        30720 │         5178 │ 25542 │       77.6% │
│ test    │   41 │        17939 │         2434 │ 15505 │       84.1% │
│ TOTAL   │  129 │        60062 │         8742 │ 51320 │       85.4% │
└─────────┴──────┴──────────────┴──────────────┴───────┴─────────────┘
```

To reset tracking data: `dtk reset` (or `dtk reset --force` to skip confirmation).

## Configuration

Manage settings via CLI or edit `~/.config/dtk/config.json` directly:

```sh
dtk config show                              # display all current values
dtk config set tracking.enabled false
dtk config set tracking.retentionDays 30
dtk config set display.width 100
dtk config set tee.mode Always
```

Supported keys: `tracking.enabled`, `tracking.retentionDays`, `tracking.dbPath`, `tracking.tokenizer`, `display.colors`, `display.emoji`, `display.width`, `tee.mode`, `tee.directory`, `tee.maxFiles`, `tee.maxFileSizeBytes`.

See [Configuration](https://handys11.github.io/DotnetTokenKiller/articles/configuration.html) for defaults, valid values, and the full JSON schema.

## Diagnostics

```sh
dtk doctor
```

Checks dotnet SDK, config file, tracking database, and tee directory. Exits `1` if any check fails.

## Shell Completion

```sh
dtk completion bash       >> ~/.bashrc
dtk completion zsh        >> ~/.zshrc
dtk completion fish       > ~/.config/fish/completions/dtk.fish
dtk completion powershell >> $PROFILE
```

## Log Files

DTK saves raw command output to disk for failed runs by default (`tee.mode = failures`). Pass `--show-log` to print the log path after any run:

```sh
dtk dotnet test --show-log
```

For more details see the [full documentation](https://handys11.github.io/DotnetTokenKiller/).
