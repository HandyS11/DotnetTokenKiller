# DotnetTokenKiller

A .NET CLI proxy that reduces LLM token usage by filtering the verbose output of `dotnet` commands down to only what matters.

When you feed `dotnet build` or `dotnet test` output to an LLM, most of it is noise — SDK banners, MSBuild headers, progress lines, ANSI escape codes, duplicate error messages. DTK strips all of that and returns a compact, signal-only result. Fewer tokens in means lower cost and less context consumed.

## Installation

```sh
dotnet tool install -g DotnetTokenKiller
```

Requires [.NET 10 SDK](https://dotnet.microsoft.com/download).

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

## Reference

### `dtk --help`

```sh
USAGE:
    dtk [OPTIONS] <COMMAND>

OPTIONS:
    -h, --help       Prints help information
    -v, --version    Prints version information

COMMANDS:
    dotnet    Run dotnet commands with filtered output
    gain      Show token savings analytics
    reset     Clear all tracking data
```

### `dtk dotnet build --help`

```sh
DESCRIPTION:
Run dotnet build with filtered output

USAGE:
    dtk dotnet build [args] [OPTIONS]

ARGUMENTS:
    [args]    Arguments to forward to the underlying dotnet process

OPTIONS:
    -h, --help        Prints help information
    -v, --verbose     Increase verbosity (use -v for level 1, -v -v for level 2)
        --show-log    Print the path to the full log file when the output was saved
```

> `dtk dotnet test`, `dtk dotnet restore`, and `dtk dotnet clean` accept the same options.

### `dtk gain --help`

```sh
DESCRIPTION:
Show token savings analytics

USAGE:
    dtk gain [OPTIONS]

OPTIONS:
    -h, --help       Prints help information
        --days       Number of days of history to include (default: 30)
        --project    Filter by current project directory
        --json       Output raw JSON instead of table
```

### `dtk reset --help`

```sh
DESCRIPTION:
Clear all tracking data

USAGE:
    dtk reset [OPTIONS]

OPTIONS:
    -h, --help     Prints help information
    -f, --force    Skip confirmation prompt
```

## Output Examples

### Build

Successful builds show a concise summary:

```sh
✓ dotnet build (2 projects, 2.78s)
```

Errors are grouped by file with line numbers and error codes:

```sh
dotnet build: 1 error, 0 warnings
---
samples/SampleApp.Broken/BrokenClass.cs (1 error)
  (5,33) CS0029: Cannot implicitly convert type 'string' to 'int'
Top codes: CS0029 (1x)
```

### Test

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

DTK tracks token counts for every run using OpenAI's `cl100k_base` tokenizer. View your cumulative savings:

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
    "dbPath": null
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

| Key             | Default | Description                                                           |
|-----------------|---------|-----------------------------------------------------------------------|
| `enabled`       | `true`  | Enable or disable token tracking                                      |
| `retentionDays` | `90`    | How many days of history to keep                                      |
| `dbPath`        | `null`  | Custom SQLite path (defaults to `%LOCALAPPDATA%/dtk/tracking.db`)    |

### Display

| Key      | Default | Description                        |
|----------|---------|------------------------------------|
| `colors` | `true`  | Enable colored terminal output     |
| `emoji`  | `true`  | Enable emoji characters (✓, etc.) |
| `width`  | `120`   | Display width in characters        |

### Tee Logs

| Key                | Default      | Description                                                       |
|--------------------|--------------|-------------------------------------------------------------------|
| `mode`             | `"failures"` | `"failures"` saves only failed runs; `"always"` saves all runs   |
| `directory`        | `null`       | Log directory (defaults to `%LOCALAPPDATA%/dtk/tee`)             |
| `maxFiles`         | `20`         | Maximum log files to keep; oldest are deleted first               |
| `maxFileSizeBytes` | `1048576`    | Maximum size per log file (1 MB)                                  |
