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
✓ dotnet build (3 projects, 2.45s)
```

**Build — with errors:**

```text
dotnet build: 2 errors, 1 warning

src/MyApp/Program.cs(12,5): error CS0103: The name 'Foo' does not exist in the current context
src/MyApp/Program.cs(18,9): warning CS8600: Converting null literal to non-nullable type
src/MyLib/Service.cs(34,1): error CS1002: ; expected

Top error codes: CS0103 (1), CS1002 (1)
```

**Test — all passing:**

```text
✓ dotnet test: 47 passed (2 projects, 3.12s)
```

**Test — with failures:**

```text
FAILURES (2):

MyApp.Tests.OrderServiceTests.CalculateTotal_WithDiscount_ReturnsCorrectAmount [23 ms]
  Expected: 85.00
  Actual:   90.00
  at OrderServiceTests.cs:42

MyApp.Tests.UserServiceTests.GetUser_WhenNotFound_ThrowsException [8 ms]
  Expected exception of type KeyNotFoundException but none was thrown.
  at UserServiceTests.cs:67
```

**Restore:**

```text
✓ dotnet restore (4 projects, 1.89s)
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
