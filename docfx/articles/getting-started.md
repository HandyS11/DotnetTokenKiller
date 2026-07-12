# Getting Started

DotnetTokenKiller (DTK) is a .NET CLI proxy that reduces LLM token usage by filtering the verbose output of `dotnet` commands.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) or later (full SDK, not just the runtime)
- **OS**: Windows, macOS, or Linux — any platform supported by the .NET SDK
- **Shell**: Works with bash, zsh, fish, PowerShell, and cmd
- **Python 3** (optional): Required only if you use `dtk integrate claude` or `dtk integrate gemini`, which install Python-based hooks

## Installation

Install DTK as a global .NET tool:

```sh
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

## First Use

Prefix any supported `dotnet` command with `dtk`:

```sh
dtk dotnet build
dtk dotnet test
```

DTK runs the underlying `dotnet` command, filters the output, and prints a compact summary. For example, a successful build that normally prints restore progress, project paths, and timing becomes:

```sh
✓ dotnet build (1 project, 1.86s)
```

A test run with one failure strips adapter banners, license warnings, and framework internals down to:

```sh
FAILURES (1):
  SampleApp.Tests.IntentionallyFailingTests.AlwaysFails [5 ms]
    Intentional failure
    at samples/SampleApp.Tests/IntentionallyFailingTests.cs:line 8
dotnet test: 1 failed, 3 passed (1 project, 0.07s)
```

## Supported Commands

| Command | Description |
|---------|-------------|
| `dtk dotnet build`   | Build with filtered output |
| `dtk dotnet test`    | Test with filtered output |
| `dtk dotnet restore` | Restore with filtered output |
| `dtk dotnet clean`   | Clean with filtered output |
| `dtk dotnet format`  | Format with filtered output |
| `dtk gain`           | Show token savings analytics |
| `dtk reset`          | Clear all tracking data |
| `dtk config show`    | Display current configuration |
| `dtk config set`     | Update a configuration value |
| `dtk doctor`         | Run self-diagnostic checks |
| `dtk completion`     | Print a shell completion script |
| `dtk integrate`      | Install AI agent integration artifacts |

Any other `dotnet` subcommand (e.g., `dtk dotnet publish`) is passed through to `dotnet` unchanged.

## Troubleshooting

### `dtk: command not found`

The .NET global tools directory is not on your `PATH`. Add it:

```sh
# Linux / macOS (bash/zsh)
export PATH="$PATH:$HOME/.dotnet/tools"

# Windows (PowerShell)
$env:PATH += ";$env:USERPROFILE\.dotnet\tools"
```

Add the line to your shell profile (`~/.bashrc`, `~/.zshrc`, or `$PROFILE`) to make it permanent.

### `dtk` is installed but outdated

```sh
dotnet tool update -g DotnetTokenKiller
```

### Proxy or corporate firewall errors during install

If `dotnet tool install` fails behind a proxy, configure NuGet to use your proxy:

```sh
# Linux / macOS
export HTTP_PROXY=http://proxy:port
export HTTPS_PROXY=http://proxy:port

# Windows (PowerShell)
$env:HTTP_PROXY = "http://proxy:port"
$env:HTTPS_PROXY = "http://proxy:port"
```

Then retry the install command.

### `dtk integrate claude` / `dtk integrate gemini` fails

These commands generate Python-based hooks that require `python3` to be available on your `PATH` at runtime. Verify with:

```sh
python3 --version
```

## Next Steps

- [Usage Guide](usage.md) — all flags and command details
- [Configuration](configuration.md) — customize behavior via CLI or JSON config
- [AI Agent Setup](ai-agent-setup.md) — integrate with Claude Code, Copilot, Gemini, Cursor, Windsurf, Aider, or JetBrains AI
