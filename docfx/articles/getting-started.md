# Getting Started

DotnetTokenKiller (DTK) is a .NET CLI proxy that reduces LLM token usage by filtering the verbose output of `dotnet` commands.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download) or later

## Installation

Install DTK as a global .NET tool:

```sh
dotnet tool install -g DotnetTokenKiller
```

To update an existing installation:

```sh
dotnet tool update -g DotnetTokenKiller
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
| `dtk gain`           | Show token savings analytics |
| `dtk reset`          | Clear all tracking data |

Any other `dotnet` subcommand (e.g., `dtk dotnet publish`) is passed through to `dotnet` unchanged.

## Next Steps

- [Usage Guide](usage.md) — all flags and command details
- [Configuration](configuration.md) — customize behavior via JSON config
- [AI Agent Setup](ai-agent-setup.md) — integrate with Claude Code
