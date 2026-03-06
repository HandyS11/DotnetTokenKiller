# 03 — CLI Parsing with Spectre.Console.Cli

## Why Spectre.Console?

Spectre.Console provides a mature, well-maintained CLI framework for .NET with:

- **`CommandApp`** — declarative command tree with typed settings
- **Rich rendering** — tables, markup, colors, emoji for the `dtk gain` analytics display
- **Dependency injection** — built-in `TypeRegistrar` pattern for wiring up services
- **Strongly typed settings** — each command gets a settings class with validated options

This replaces `System.CommandLine` (which is still in prerelease) with a stable, production-ready alternative.

---

## Entry Point: `Program.cs`

The composition root configures DI and the Spectre.Console `CommandApp`:

- Register all Domain interfaces with their Infrastructure implementations
- Build a command tree: `dtk dotnet build`, `dtk dotnet test`, etc.
- Configure global options (verbosity)

The `CommandApp` is configured with a `TypeRegistrar` that wraps `Microsoft.Extensions.DependencyInjection` (or a simple custom container), allowing Spectre.Console to resolve command instances with their dependencies injected.

---

## Command Tree Structure

```
dtk
├── dotnet                    ← Branch command (groups subcommands)
│   ├── build [args...]       ← DotnetBuildCommand
│   ├── test [args...]        ← DotnetTestCommand
│   ├── restore [args...]     ← DotnetRestoreCommand
│   ├── publish [args...]     ← DotnetPublishCommand
│   ├── pack [args...]        ← DotnetPackCommand
│   ├── clean [args...]       ← DotnetCleanCommand
│   ├── run [args...]         ← DotnetRunCommand
│   ├── ef [args...]          ← DotnetEfCommand
│   ├── format [args...]      ← DotnetFormatCommand
│   └── nuget [args...]       ← DotnetNugetCommand
├── gain                      ← GainCommand (token savings analytics)
└── (default)                 ← Help / version info
```

---

## Spectre.Console Command Pattern

Each `dotnet` subcommand follows the same pattern:

1. **Settings class** — defines the command's options and arguments (shared `DotnetCommandSettings` base)
2. **Command class** — inherits `AsyncCommand<TSettings>`, receives use cases via constructor injection
3. **Delegation** — the command's `ExecuteAsync` simply delegates to the appropriate `FilteredRunUseCase` with the correct filter

### Shared Settings

All `dotnet` subcommands share a common settings base that captures:
- **Verbosity** (`-v` / `--verbose`) — controls debug output level (0 = normal, 1 = debug, 2 = trace/raw)
- **Remaining arguments** — everything after the subcommand name is forwarded to the real `dotnet` command

### Command Implementation

Each command class:
1. Receives `FilteredRunUseCase` (or similar) via constructor injection
2. Calls the use case with the subcommand name, forwarded arguments, the appropriate filter, and verbosity
3. Returns the exit code from the underlying `dotnet` process

This keeps command classes thin — they are pure adapters between Spectre.Console and the Application layer.

---

## Argument Forwarding

A critical requirement: `dtk dotnet build --configuration Release --no-restore` must pass all arguments after `build` to the real `dotnet build` process unchanged.

Spectre.Console handles this through remaining arguments in the settings class. All unmatched tokens are collected and forwarded verbatim to the underlying `dotnet` command.

**Important**: Flags like `--configuration`, `--no-restore`, `--verbosity`, etc. must not be consumed by DTK — they belong to the `dotnet` CLI.

---

## Passthrough / Fallback

For unrecognized `dotnet` subcommands (e.g., `dtk dotnet new`, `dtk dotnet add`, `dtk dotnet watch`):

- DTK runs the command in passthrough mode (inherit stdin/stdout/stderr)
- No filtering is applied
- Token usage is still tracked for analytics
- Exit code is preserved

This is handled by a default/fallback command in the `dotnet` branch that catches any subcommand not explicitly registered.

---

## `dtk gain` Command

The `gain` command displays token savings analytics using Spectre.Console's rich rendering:

- **Tables** — show command history with savings percentages
- **Markup** — colored output for savings highlights
- **Summary** — total tokens saved, average savings percentage, time range

This is the primary showcase for Spectre.Console's rendering capabilities.

### Options

| Option | Description |
|---|---|
| `--days N` | Show analytics for last N days (default: 30) |
| `--project` | Filter by current project path |
| `--json` | Output raw JSON (for LLM consumption) |

---

## DI / TypeRegistrar

Spectre.Console.Cli supports dependency injection through its `TypeRegistrar` / `TypeResolver` pattern. The CLI project implements these to bridge Spectre.Console with the service container:

- **TypeRegistrar** — wraps `IServiceCollection`, registers services during configuration
- **TypeResolver** — wraps `IServiceProvider`, resolves command instances at runtime

This allows command classes to receive Application use cases and Infrastructure services via constructor injection, maintaining Clean Architecture's dependency inversion.

### Registration Flow

1. Register Domain interfaces → Infrastructure implementations
2. Register Application use cases
3. Register filter implementations
4. Pass `TypeRegistrar` to `CommandApp` constructor
5. Spectre.Console resolves command instances with all dependencies injected

---

## Command Routing Summary

```
dtk dotnet build [args...]     → DotnetBuildCommand → FilteredRunUseCase + DotnetBuildFilter
dtk dotnet test [args...]      → DotnetTestCommand → FilteredRunUseCase + DotnetTestFilter
dtk dotnet restore [args...]   → DotnetRestoreCommand → FilteredRunUseCase + DotnetRestoreFilter
dtk dotnet publish [args...]   → DotnetPublishCommand → FilteredRunUseCase + DotnetPublishFilter
dtk dotnet pack [args...]      → DotnetPackCommand → FilteredRunUseCase + DotnetPackFilter
dtk dotnet clean [args...]     → DotnetCleanCommand → FilteredRunUseCase + DotnetCleanFilter
dtk dotnet run [args...]       → DotnetRunCommand → FilteredRunUseCase + DotnetRunFilter
dtk dotnet ef [args...]        → DotnetEfCommand → FilteredRunUseCase + DotnetEfFilter
dtk dotnet format [args...]    → DotnetFormatCommand → FilteredRunUseCase + DotnetFormatFilter
dtk dotnet nuget [args...]     → DotnetNugetCommand → FilteredRunUseCase + DotnetNugetFilter
dtk gain                       → GainCommand → GainReportUseCase
dtk dotnet <other> [args...]   → Passthrough → PassthroughRunUseCase
```
