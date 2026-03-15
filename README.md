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

All positional arguments are forwarded to the underlying process:

```sh
dtk dotnet build --configuration Release
dtk dotnet test --filter "Category=Unit"
dtk dotnet build src/MyProject/MyProject.csproj
```

Use `-v` / `--verbose` to increase output verbosity (pass it twice for maximum detail):

```sh
dtk dotnet build -v
```

Unknown subcommands are passed through to `dotnet` unchanged.

## Output Examples

**Build — no errors:**

```text
✓ dotnet build (1 project, 0.51s)
```

**Build — with errors:**

```text
dotnet build: 1 error, 0 warnings
---
samples/SampleApp.Broken/BrokenClass.cs (1 error)
  (5,33) CS0029: Cannot implicitly convert type 'string' to 'int'
Top codes: CS0029 (1x)
```

**Test — all passing:**

```text
✓ dotnet test: 3 passed (1 project, 0.02s)
```

**Test — with failures:**

```text
FAILURES (1):
  SampleApp.Tests.IntentionallyFailingTests.AlwaysFails [1 ms]
    Intentional failure
    at samples/SampleApp.Tests/IntentionallyFailingTests.cs:line 8
dotnet test: 1 failed, 3 passed (1 project, 0.02s)
```

**Restore — missing package:**

```text
dotnet restore: 1 error
  NU1101: Unable to find package DotnetTokenKiller.DoesNotExist. No packages exist with this id in source(s): nuget.org (samples/SampleApp.BadPackage/SampleApp.BadPackage.csproj)
```

**Restore — up to date:**

```text
✓ dotnet restore (all up-to-date)
```

**Clean:**

```text
✓ dotnet clean
```

## Token Savings Analytics

DTK tracks token counts for every run using OpenAI's `cl100k_base` tokenizer. View your cumulative savings:

```sh
dtk gain               # last 30 days
dtk gain --days 7      # last 7 days
dtk gain --project     # current project only
dtk gain --json        # machine-readable JSON output
```

Example output:

```text
 Command   Runs   Without Tool   Used by Tool   Saved    Avg Savings
 build     5      45,230         12,100         33,130   73.2%
 test      8      89,430         22,510         66,920   74.8%
 restore   3      12,340         8,900          3,440    27.9%
 TOTAL     16     147,000        43,510         103,490  70.4%
```

To reset all tracking data:

```sh
dtk reset          # prompts for confirmation
dtk reset --force  # skips confirmation
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

| Key             | Default | Description                                         |
|-----------------|---------|-----------------------------------------------------|
| `enabled`       | `true`  | Enable or disable token tracking                    |
| `retentionDays` | `90`    | How many days of history to keep                    |
| `dbPath`        | `null`  | Custom SQLite path (defaults to `%LOCALAPPDATA%/dtk/tracking.db`) |

### Display

| Key      | Default | Description                  |
|----------|---------|------------------------------|
| `colors` | `true`  | Enable colored terminal output |
| `emoji`  | `true`  | Enable emoji characters (✓, etc.) |
| `width`  | `120`   | Display width in characters  |

### Tee Logs

DTK can save the raw, unfiltered output to disk so you can inspect it when needed.

| Key                | Default       | Description                                          |
|--------------------|---------------|------------------------------------------------------|
| `mode`             | `"failures"`  | `"failures"` saves only failed runs; `"always"` saves all |
| `directory`        | `null`        | Log directory (defaults to `%LOCALAPPDATA%/dtk/tee`, overridden by `DTK_TEE_DIR` env var) |
| `maxFiles`         | `20`          | Maximum log files to keep; oldest are deleted first  |
| `maxFileSizeBytes` | `1048576`     | Maximum size per log file (1 MB)                    |

When a log is saved, DTK prints its path:

```text
[full output: /home/user/.local/share/dtk/tee/20260315T143022_abc123_build.log]
```
