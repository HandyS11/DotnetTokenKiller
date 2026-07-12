<div align="center">

# DotnetTokenKiller

A .NET CLI proxy that reduces LLM token usage by filtering the verbose output of `dotnet` commands down to only what
matters.

[![CI](https://github.com/HandyS11/DotnetTokenKiller/actions/workflows/ci.yml/badge.svg)](https://github.com/HandyS11/DotnetTokenKiller/actions/workflows/ci.yml)
[![CD](https://github.com/HandyS11/DotnetTokenKiller/actions/workflows/publish.yml/badge.svg)](https://github.com/HandyS11/DotnetTokenKiller/actions/workflows/publish.yml)
[![License](https://img.shields.io/github/license/HandyS11/DotnetTokenKiller)](./LICENSE)

[![codecov](https://codecov.io/github/HandyS11/DotnetTokenKiller/graph/badge.svg?token=AP66I96X3E)](https://codecov.io/github/HandyS11/DotnetTokenKiller)
[![Mutation testing badge](https://img.shields.io/endpoint?style=flat&url=https%3A%2F%2Fbadge-api.stryker-mutator.io%2Fgithub.com%2FHandyS11%2FDotnetTokenKiller%2Fdevelop)](https://dashboard.stryker-mutator.io/reports/github.com/HandyS11/DotnetTokenKiller/develop)

[![DotnetTokenKiller NuGet](https://img.shields.io/nuget/v/DotnetTokenKiller?label=CLI&logo=nuget)](https://www.nuget.org/packages/DotnetTokenKiller)
[![DotnetTokenKiller Downloads](https://img.shields.io/nuget/dt/DotnetTokenKiller?label=CLI%20downloads&logo=nuget)](https://www.nuget.org/packages/DotnetTokenKiller)

</div>

## The Problem

When AI coding agents run `dotnet build` or `dotnet test`, the output is packed with noise — SDK banners, MSBuild
headers, progress indicators, ANSI escape codes, duplicate messages, and framework internals. A typical `dotnet test`
run can produce **200+ lines** where only 5–10 actually matter. Every extra line burns tokens, inflates cost, and
wastes precious context window space.

<details>
<summary><strong>Example: 27 lines of raw <code>dotnet test</code> output → 5 lines with dtk</strong></summary>

**Before (raw `dotnet test`):**

```sh
Restore complete (0.4s)
  SampleApp.Tests succeeded (0.1s) → bin/Debug/net10.0/SampleApp.Tests.dll
  SampleApp succeeded (0.1s) → bin/Debug/net10.0/SampleApp.dll
  Build succeeded in 0.8s
Test run for /home/user/samples/SampleApp.Tests/bin/Debug/net10.0/SampleApp.Tests.dll (.NETCoreApp,Version=v10.0)
VSTest version 17.13.0 (x64)
Starting test execution, please wait...
A total of 1 test files matched the specified pattern.
  Failed AlwaysFails [5 ms]
  Error Message:
   Intentional failure
  Stack Trace:
     at SampleApp.Tests.IntentionallyFailingTests.AlwaysFails() in /home/user/samples/SampleApp.Tests/IntentionallyFailingTests.cs:line 8

Failed!  - Failed:     1, Passed:     3, Skipped:     0, Total:     4, Duration: 42 ms
```

**After (`dtk dotnet test`):**

```sh
FAILURES (1):
  SampleApp.Tests.IntentionallyFailingTests.AlwaysFails [5 ms]
    Intentional failure
    at samples/SampleApp.Tests/IntentionallyFailingTests.cs:line 8
dotnet test: 1 failed, 3 passed (1 project, 0.07s)
```

</details>

DTK strips all the noise and returns a compact, signal-only result. Fewer tokens in means lower cost and less
context consumed.

## Features at a Glance

- **Build filtering** — strips MSBuild noise, keeps only errors, warnings, and a compact summary (~78% savings)
- **Test filtering** — removes adapter banners, license warnings, and reflection stack frames (~84% savings)
- **Restore/Clean filtering** — condenses output to essentials (~47–98% savings)
- **Format filtering** — shows only violations with workspace-relative paths
- **8 AI agent integrations** — Claude Code, GitHub Copilot, GitHub Copilot CLI, Gemini CLI, Cursor, Windsurf, Aider, JetBrains AI
- **Token analytics** — tracks per-command savings over time with `dtk gain`
- **Self-diagnostics** — `dtk doctor` validates your setup in one command
- **Shell completion** — bash, zsh, fish, and PowerShell
- **Log teeing** — optionally saves raw output to disk for post-mortem inspection

## Installation

```sh
# Requires .NET 10 SDK (https://dotnet.microsoft.com/download) — full SDK, not just runtime
dotnet tool install -g DotnetTokenKiller
```

To update an existing installation:

```sh
dotnet tool update -g DotnetTokenKiller
```

To uninstall:

```sh
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
dtk dotnet format --verify-no-changes
```

Unknown subcommands pass through to `dotnet` unchanged.

## AI Agent Setup

Install integration artifacts with one command. **Installing globally is the recommended way to set
dtk up** — do it once and every project your agent touches picks it up automatically, with no
per-repo setup.

### Recommended: install globally

For the providers with a home config, add `--global` (`-g`) to install into your home directory so
the integration applies across **all** projects:

```sh
dtk integrate claude      --global   # ~/.claude
dtk integrate gemini      --global   # ~/.gemini
dtk integrate aider       --global   # ~/.aider.conf.yml
dtk integrate copilot-cli --global   # ~/.copilot/hooks
```

Run this once per machine and you're done — new projects need no extra setup. `--global` is supported
for **claude**, **gemini**, **aider**, and **copilot-cli** (the providers with a home config).

### Per-project install

For the other providers — or when you want dtk scoped to a single repository — run
`dtk integrate <provider>` inside the project (without `--global`):

| Provider               | Command                     | What it creates                                      |
|------------------------|-----------------------------|------------------------------------------------------|
| **Claude Code**        | `dtk integrate claude`      | Skill file, PreToolUse hook, settings merge          |
| **GitHub Copilot**     | `dtk integrate copilot`     | Section in `.github/copilot-instructions.md`         |
| **GitHub Copilot CLI** | `dtk integrate copilot-cli` | preToolUse hook in `.github/hooks/`                  |
| **Gemini CLI**         | `dtk integrate gemini`      | BeforeTool hook, settings merge, `GEMINI.md` section |
| **Cursor**             | `dtk integrate cursor`      | `.cursor/rules/dtk.mdc`                              |
| **Windsurf**           | `dtk integrate windsurf`    | `.windsurf/rules/dtk.md`                             |
| **Aider**              | `dtk integrate aider`       | Instructions file, `.aider.conf.yml` section         |
| **JetBrains AI**       | `dtk integrate jetbrains`   | Section in `.junie/guidelines.md`                    |

`copilot`, `cursor`, `windsurf`, and `jetbrains` are repository-scoped and have no global mode.
`copilot-cli` is distinct from `copilot` (instruction-only, Copilot IDE) and supports `--global`.
`--global` cannot be combined with `--dir`.

All commands are idempotent — re-running is safe. Pass `--force` to refresh existing files.

On machines that also run the rtk hook, `dtk integrate claude` automatically excludes `dotnet` from rtk so the two proxies don't both rewrite `dotnet` commands.

See [AI Agent Setup](https://handys11.github.io/DotnetTokenKiller/articles/ai-agent-setup.html) for per-provider details
and manual installation steps.

## Token Savings Analytics

```sh
dtk gain               # last 30 days
dtk gain --days 7
dtk gain --project     # current project only
dtk gain --json
```

Example:

```sh
┌─────────┬──────┬──────────────┬──────────────┬─────────┬─────────────┐
│ Command │ Runs │ Without Tool │ Used by Tool │   Saved │ Avg Savings │
├─────────┼──────┼──────────────┼──────────────┼─────────┼─────────────┤
│ build   │  749 │      1925317 │       327044 │ 1598273 │       79.4% │
│ clean   │  145 │        85875 │          870 │   85005 │       98.0% │
│ restore │  242 │        28793 │         8959 │   19834 │       46.9% │
│ test    │ 1011 │       795760 │       201134 │  594626 │       78.9% │
│         │      │              │              │         │             │
│ TOTAL   │ 2147 │      2835745 │       538007 │ 2297738 │       81.0% │
└─────────┴──────┴──────────────┴──────────────┴─────────┴─────────────┘
```

To reset tracking data: `dtk reset` (or `dtk reset --force` to skip confirmation).

## Diagnostics

```sh
dtk doctor
```

Checks dotnet SDK, config file, tracking database, and tee directory. Exits `1` if any check fails.

## Configuration

Manage settings via CLI or edit `~/.config/dtk/config.json` directly:

```sh
dtk config show                              # display all current values
dtk config set tracking.enabled false
dtk config set tracking.retentionDays 30
dtk config set display.emoji false
dtk config set tee.mode Always
```

Supported keys: `tracking.enabled`, `tracking.retentionDays`, `tracking.dbPath`, `tracking.tokenizer`,
`display.emoji`, `tee.mode`, `tee.directory`, `tee.maxFiles`, `tee.maxFileSizeBytes`.

See [Configuration](https://handys11.github.io/DotnetTokenKiller/articles/configuration.html) for defaults, valid
values, and the full JSON schema.

## Shell Completion

**bash** — write to a dedicated file (preferred) or to the system-wide completions directory:

```sh
dtk completion bash > ~/.bash_completion
# or system-wide:
dtk completion bash > /etc/bash_completion.d/dtk
```

**zsh** — the script must live in `$fpath`, not be sourced inline:

```sh
mkdir -p ~/.zfunc && dtk completion zsh > ~/.zfunc/_dtk
```

Then add these two lines to `~/.zshrc` (before any existing `compinit` call):

```sh
fpath=(~/.zfunc $fpath)
autoload -Uz compinit && compinit
```

**fish**

```sh
dtk completion fish > ~/.config/fish/completions/dtk.fish
```

**PowerShell**

```pwsh
dtk completion powershell >> $PROFILE
```

## Log Files

DTK saves raw command output to disk for failed runs by default (`tee.mode = failures`). Pass `--show-log` to print the
log path after any run:

```sh
dtk dotnet test --show-log
```

## Inspiration

Inspired by [rtk](https://github.com/rtk-ai/rtk). DTK takes the same idea — filtering noisy CLI output for AI
agents — and rebuilds it natively in .NET with per-command filters, a richer feature set (token analytics, shell
completion, multi-agent integration), and first-class support for the full `dotnet` CLI surface.

## Documentation

For more details see the [full documentation](https://handys11.github.io/DotnetTokenKiller/).
