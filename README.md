# DotnetTokenKiller

A .NET CLI proxy that reduces LLM token usage by filtering the verbose output of `dotnet` commands down to only what matters.

When you feed `dotnet build` or `dotnet test` output to an LLM, most of it is noise — SDK banners, MSBuild headers, progress lines, ANSI escape codes, duplicate error messages. DTK strips all of that and returns a compact, signal-only result. Fewer tokens in means lower cost and less context consumed.

## Installation

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) or later

### Install

```sh
dotnet tool install -g DotnetTokenKiller
```

### AI Agent Setup

DTK can install integration artifacts for AI coding agents automatically.

#### Claude Code

From your project root, run:

```sh
dtk integrate claude
```

This creates three files:

- `.claude/skills/dotnet-token-killer/SKILL.md` — instructs Claude Code to prefer `dtk`
- `.claude/hooks/dotnet-to-dtk.py` — a Python hook that rewrites `dotnet` commands
- `.claude/settings.json` — registers the hook under `PreToolUse` (merges with any existing settings)

With the hook in place, any time Claude Code runs `dotnet build`, `dotnet test`, `dotnet restore`, or `dotnet clean`, it is silently rewritten to `dtk dotnet ...` before execution.

Re-running the command is safe: existing files are skipped. Use `--force` to overwrite:

```sh
dtk integrate claude --force
```

#### GitHub Copilot (VS Code)

```sh
dtk integrate copilot
```

This appends a `dtk` instructions section to `.github/copilot-instructions.md`, creating the file if it does not exist. Re-running is safe; use `--force` to refresh the section.

See [AI Agent Setup](https://handys11.github.io/DotnetTokenKiller/articles/ai-agent-setup.html) in the docs for manual installation steps and details on what each provider installs.

## Usage

Prefix any supported `dotnet` command with `dtk`:

```sh
dtk dotnet build
dtk dotnet test
dtk dotnet restore
dtk dotnet clean
```

All positional arguments and flags are forwarded to the underlying process:

```sh
dtk dotnet build --configuration Release
dtk dotnet test --filter "Category=Unit"
dtk dotnet build src/MyProject/MyProject.csproj
```

Unknown subcommands are passed through to `dotnet` unchanged.

## Examples

Classic `dotnet test` output is verbose and noisy:

```sh
Restore complete (0.7s)
  SampleApp.Tests net10.0 succeeded (0.4s) → samples\SampleApp.Tests\bin\Debug\net10.0\SampleApp.Tests.dll
[xUnit.net 00:00:00.00] xUnit.net VSTest Adapter v3.1.5+1b188a7b0a (64-bit .NET 10.0.4)
[xUnit.net 00:00:00.17]   Discovering: SampleApp.Tests
[xUnit.net 00:00:00.28]   Discovered:  SampleApp.Tests
[xUnit.net 00:00:00.34]   Starting:    SampleApp.Tests
     Warning:
     The component "Fluent Assertions" is governed by the rules defined in the Xceed License Agreement and
     the Xceed Fluent Assertions Community License. You may use Fluent Assertions free of charge for
     non-commercial use only. An active subscription is required to use Fluent Assertions for commercial use.
     Please contact Xceed Sales mailto:sales@xceed.com to acquire a subscription at a very low cost.
     A paid commercial license supports the development and continued increasing support of
     Fluent Assertions users under both commercial and community licenses. Help us
     keep Fluent Assertions at the forefront of unit testing.
     For more information, visit https://xceed.com/products/unit-testing/fluent-assertions/
[xUnit.net 00:00:00.48]     SampleApp.Tests.IntentionallyFailingTests.AlwaysFails [FAIL]
[xUnit.net 00:00:00.48]       Intentional failure
[xUnit.net 00:00:00.48]       Stack Trace:
[xUnit.net 00:00:00.49]         D:\DotnetTokenKiller\samples\SampleApp.Tests\IntentionallyFailingTests.cs(8,0): at SampleApp.Tests.IntentionallyFailingTests.AlwaysFails()
[xUnit.net 00:00:00.49]            at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)
[xUnit.net 00:00:00.49]            at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)
[xUnit.net 00:00:00.56]   Finished:    SampleApp.Tests
  SampleApp.Tests test net10.0 failed with 1 error(s) (0.5s)
    D:\DotnetTokenKiller\samples\SampleApp.Tests\IntentionallyFailingTests.cs(8): error TESTERROR:
          SampleApp.Tests.IntentionallyFailingTests.AlwaysFails (5ms):
            Error Message: Intentional failure
            Stack Trace:
               at SampleApp.Tests.IntentionallyFailingTests.AlwaysFails() in D:\DotnetTokenKiller\samples\SampleApp.Tests\IntentionallyFailingTests.cs:line 8
               at System.Reflection.MethodBaseInvoker.InterpretedInvoke_Method(Object obj, IntPtr* args)
               at System.Reflection.MethodBaseInvoker.InvokeWithNoArgs(Object obj, BindingFlags invokeAttr)
Test summary: total: 4, failed: 1, succeeded: 3, skipped: 0, duration: 0.5s
Build failed with 1 error(s) in 5.4s
```

Passes and failures are summarized. Detailed stack traces are provided for failures:

```sh
FAILURES (1):
  SampleApp.Tests.IntentionallyFailingTests.AlwaysFails [5 ms]
    Intentional failure
    at samples/SampleApp.Tests/IntentionallyFailingTests.cs:line 8
dotnet test: 1 failed, 3 passed (1 project, 0.07s)
```

For more examples including multi-project builds, warnings, and restore/clean output, see [`samples/examples/`](samples/examples/).

## Token Savings Analytics

DTK tracks token counts for every run using a configurable tiktoken tokenizer (defaults to OpenAI's `cl100k_base`). View
your cumulative savings:

```sh
dtk gain               # last 30 days
dtk gain --days 7      # last 7 days
dtk gain --project     # current project only
dtk gain --json        # machine-readable JSON output
```

Example output:

```sh
┌─────────┬──────┬──────────────┬──────────────┬───────┬─────────────┐
│ Command │ Runs │ Without Tool │ Used by Tool │ Saved │ Avg Savings │
├─────────┼──────┼──────────────┼──────────────┼───────┼─────────────┤
│ build   │   44 │        30720 │         5178 │ 25542 │       77.6% │
│ clean   │   18 │         8752 │          108 │  8644 │       97.9% │
│ restore │   26 │         2651 │         1022 │  1629 │       47.0% │
│ test    │   41 │        17939 │         2434 │ 15505 │       84.1% │
│         │      │              │              │       │             │
│ TOTAL   │  129 │        60062 │         8742 │ 51320 │       85.4% │
└─────────┴──────┴──────────────┴──────────────┴───────┴─────────────┘
```

To reset all tracking data:

```sh
dtk reset          # prompts for confirmation
dtk reset --force  # skips confirmation
```

## Log Files

DTK can save the raw, unfiltered command output to disk so you can inspect it later. Controlled via `tee.mode` in the config (see below); by default only failed runs are saved. To print the log path after a command, pass `--show-log`:

```sh
dtk dotnet test --show-log
```

## Configuration

Optional JSON config at `~/.config/dtk/config.json`:

```json
{
  "tracking": {
    "enabled": true,
    "retentionDays": 90,
    "dbPath": null,
    "tokenizer": "Cl100kBase"
  },
  "display": {
    "colors": true,
    "emoji": true,
    "width": 120
  },
  "tee": {
    "mode": "failures",
    "directory": null,
    "maxFiles": 20,
    "maxFileSizeBytes": 1048576
  }
}
```

### Tracking

| Key             | Default        | Description                                                                         |
|-----------------|----------------|-------------------------------------------------------------------------------------|
| `enabled`       | `true`         | Enable or disable token tracking                                                    |
| `retentionDays` | `90`           | How many days of history to keep                                                    |
| `dbPath`        | `null`         | Custom SQLite path (defaults to `%LOCALAPPDATA%/dtk/tracking.db`)                   |
| `tokenizer`     | `"Cl100kBase"` | Tokenizer model used for token counting (see [Tokenizer Models](#tokenizer-models)) |

### Display

| Key      | Default | Description                         |
|----------|---------|-------------------------------------|
| `colors` | `true`  | Enable colored terminal output      |
| `emoji`  | `true`  | Enable emoji characters (✓, etc.)   |
| `width`  | `120`   | Display width in characters         |

### Tee Logs

| Key                | Default      | Description                                                        |
|--------------------|--------------|--------------------------------------------------------------------|
| `mode`             | `"failures"` | `"failures"` saves only failed runs; `"always"` saves all runs     |
| `directory`        | `null`       | Log directory (defaults to `%LOCALAPPDATA%/dtk/tee`)               |
| `maxFiles`         | `20`         | Maximum log files to keep; oldest are deleted first                |
| `maxFileSizeBytes` | `1048576`    | Maximum size per log file (1 MB)                                   |

### Tokenizer Models

The `tokenizer` field accepts one of the following values:

| Value        | Encoding      | Typical Models                       |
|--------------|---------------|--------------------------------------|
| `Cl100kBase` | `cl100k_base` | GPT-4, GPT-3.5-turbo                 |
| `O200kBase`  | `o200k_base`  | GPT-4o                               |
| `P50kBase`   | `p50k_base`   | Codex, text-davinci                  |
| `P50kEdit`   | `p50k_edit`   | text-davinci-edit, code-davinci-edit |
| `R50kBase`   | `r50k_base`   | GPT-3                                |
