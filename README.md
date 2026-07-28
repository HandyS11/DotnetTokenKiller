<div align="center">

<img src="./icon.png" alt="DotnetTokenKiller" width="128" />

# DotnetTokenKiller

**A .NET CLI proxy that reduces LLM token usage by filtering the verbose output of `dotnet` commands
down to only what matters.**
Prefix `build`, `test`, `restore`, `clean`, `format`, and `list package` with `dtk` for 60–90% fewer
tokens — per-command filters, token analytics, and one-command setup for 8 AI coding agents.

[![CI](https://github.com/HandyS11/DotnetTokenKiller/actions/workflows/ci.yml/badge.svg)](https://github.com/HandyS11/DotnetTokenKiller/actions/workflows/ci.yml)
[![CD](https://github.com/HandyS11/DotnetTokenKiller/actions/workflows/publish.yml/badge.svg)](https://github.com/HandyS11/DotnetTokenKiller/actions/workflows/publish.yml)
[![Docs](https://github.com/HandyS11/DotnetTokenKiller/actions/workflows/doc-publish.yml/badge.svg)](https://handys11.github.io/DotnetTokenKiller/)

![.NET](https://img.shields.io/badge/.NET-10-512BD4)
[![License](https://img.shields.io/github/license/HandyS11/DotnetTokenKiller)](./LICENSE)
[![codecov](https://codecov.io/github/HandyS11/DotnetTokenKiller/graph/badge.svg?token=AP66I96X3E)](https://codecov.io/github/HandyS11/DotnetTokenKiller)
[![Mutation testing badge](https://img.shields.io/endpoint?style=flat&url=https%3A%2F%2Fbadge-api.stryker-mutator.io%2Fgithub.com%2FHandyS11%2FDotnetTokenKiller%2Fdevelop)](https://dashboard.stryker-mutator.io/reports/github.com/HandyS11/DotnetTokenKiller/develop)

[![DotnetTokenKiller NuGet](https://img.shields.io/nuget/v/DotnetTokenKiller?label=NuGet&logo=nuget)](https://www.nuget.org/packages/DotnetTokenKiller)
[![DotnetTokenKiller Downloads](https://img.shields.io/nuget/dt/DotnetTokenKiller?label=downloads&logo=nuget)](https://www.nuget.org/packages/DotnetTokenKiller)

[Getting Started](https://handys11.github.io/DotnetTokenKiller/articles/getting-started.html) ·
[Documentation](https://handys11.github.io/DotnetTokenKiller/) ·
[Samples](./samples/README.md)

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
- **`list package` filtering** — collapses per-TFM duplication across plain, `--outdated`,
  `--deprecated`, and `--vulnerable` (~80.9% savings)
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
dtk dotnet list package --outdated
```

Unknown subcommands pass through to `dotnet` unchanged.

### Filtering output dtk did not produce

`dtk pipe` applies a filter to output on stdin — CI logs, or any invocation the hook missed.

```bash
# Accurate verdict: the producing command's exit code is passed explicitly.
dotnet build > build.log 2>&1; dtk pipe build --exit-code $? < build.log

# Convenient form. Bash cannot give a pipeline's right-hand side its predecessor's
# status, so without --exit-code the verdict is assumed to be success.
dotnet build 2>&1 | dtk pipe build

dotnet list package --outdated 2>&1 | dtk pipe list package
```

`dtk pipe` exits with whatever `--exit-code` it was given, so a CI step wrapping a failed build
still fails. Piped runs are tracked separately from runs dtk executed itself — see the `Source`
column in `dtk gain --coverage`.

### `dtk log` — get a previous run's full output back

When the filtered output is not enough, retrieve what was captured instead of re-running the build.

```bash
dtk log                  # newest log for this project: last 100 lines
dtk log build            # newest build log for this project
dtk log --list           # what is available
dtk log --index 3        # the 3rd newest
dtk log --lines 300      # a wider window
dtk log --full           # everything
dtk log --all            # include other projects
```

Logs are written by the tee feature, which defaults to `tee.mode = Failures` — only failed runs are
saved, and output under 500 characters is never saved. Use `dtk config set tee.mode Always` to keep
every run. Logs written by dtk 0.6.0 or earlier have no project metadata and appear only under `--all`.

Passthrough subcommands (anything dtk has no filter for, such as `publish`) are not tee'd, so
`dtk log` will not find them.

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

| Provider               | Command                     | What it creates                                            |
|------------------------|-----------------------------|------------------------------------------------------------|
| **Claude Code**        | `dtk integrate claude`      | Skill file, PreToolUse hook, settings merge                |
| **GitHub Copilot**     | `dtk integrate copilot`     | Section in `.github/copilot-instructions.md`               |
| **GitHub Copilot CLI** | `dtk integrate copilot-cli` | preToolUse hook in `.github/hooks/` + instructions section |
| **Gemini CLI**         | `dtk integrate gemini`      | BeforeTool hook, settings merge, `GEMINI.md` section       |
| **Cursor**             | `dtk integrate cursor`      | `.cursor/rules/dtk.mdc`                                    |
| **Windsurf**           | `dtk integrate windsurf`    | `.windsurf/rules/dtk.md`                                   |
| **Aider**              | `dtk integrate aider`       | Instructions file, `.aider.conf.yml` section               |
| **JetBrains AI**       | `dtk integrate jetbrains`   | Section in `.junie/guidelines.md`                          | `dtk integrate jetbrains`   | Section in `.junie/guidelines.md`                    |

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
dtk gain --coverage    # report which commands run unfiltered, ranked by tokens at stake
```

Example:

```sh
DTK Token Savings (Global Scope)
════════════════════════════════════════════════════════════

Total commands:    1059
Without tool:      1.0M
Used by tool:      92.9K
Tokens saved:      911.2K (90.7%)
Total exec time:   26m28s (avg 1.5s)
Efficiency meter: ██████████████████████░░ 90.7%

By Command
──────────────────────────────────────────────────────────────────────
 Command        Runs  Without Tool  Used by Tool     Saved    Avg%  Impact
 build (ok)      151        104.5K          2.3K    102.2K   97.8%  ██░░░░░░░░
 build (fail)     37         47.7K          6.9K     40.8K   85.5%  █░░░░░░░░░

 clean (ok)        3        241.9K            18    241.9K  100.0%  ██████░░░░

 format (ok)     103             0          1.0K     -1.0K    0.0%  ░░░░░░░░░░

 restore (ok)     89         12.3K          6.5K      5.8K   47.2%  █░░░░░░░░░

 test (ok)       442        420.6K          7.8K    412.8K   98.1%  ██████████
 test (fail)     234        177.1K         68.4K    108.7K   61.4%  ███░░░░░░░
```

To reset tracking data: `dtk reset` (or `dtk reset --force` to skip confirmation).

### Filter Coverage

`dtk gain --coverage` reports which commands run unfiltered (no filter exists, or the filter
degraded to a fallback), ranked by how many raw tokens are at stake — a punch list for where the
next filter would pay off most. It composes with `--days`, `--project`, `--command`, and `--json`,
but not with `--export`.

Example:

```sh
DTK Filter Coverage (Global Scope)
════════════════════════════════════════════════════════════

Total runs:        2049
Unfiltered tokens: 48.0K

╭─────────┬──────────────────────┬──────┬────────────╮
│ Command │ Outcome              │ Runs │ Raw tokens │
├─────────┼──────────────────────┼──────┼────────────┤
│ build   │ Filtered             │  435 │     442.3K │
│ publish │ PassthroughMeasured  │    4 │      48.0K │
╰─────────┴──────────────────────┴──────┴────────────╯
```

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
