# DTK (Dotnet Token Killer) — Project Overview

> A .NET 10 CLI tool inspired by [RTK (Rust Token Killer)](https://github.com/rtk-ai/rtk), focused exclusively on **`dotnet` CLI** command support for the .NET ecosystem.

## What is DTK?

DTK is a CLI proxy that sits between an LLM (like Claude Code) and your terminal. It intercepts `dotnet` command output and applies intelligent filtering to reduce token consumption by 60–90%. This means faster LLM responses, lower costs, and less noise in context windows.

The project draws inspiration from RTK's approach but is built natively in C# with .NET 10, following **Clean Architecture** principles and using **Spectre.Console** for rich CLI experiences.

## Why Build This in .NET?

- **Native .NET developer experience** — dogfood the ecosystem you're optimizing for
- **NuGet distribution** — `dotnet tool install -g dtk`
- **First-class `dotnet` CLI understanding** — parse MSBuild output, NuGet restore logs, test results natively
- **Rich terminal output** — Spectre.Console provides tables, colors, and structured rendering
- **Cross-platform** — .NET 10 runs everywhere (Windows, macOS, Linux)
- **Clean Architecture** — maintainable, testable, and extensible codebase

## Scope: `dotnet` Command Support Only

DTK covers **only** the `dotnet` CLI subcommands:

| DTK Command | Wraps | Filtering Strategy | Expected Savings |
|---|---|---|---|
| `dtk dotnet build` | `dotnet build` | Strip restore/compile noise, keep errors + summary | 80–90% |
| `dtk dotnet test` | `dotnet test` | Failures only, aggregated suite summary | 90–95% |
| `dtk dotnet run` | `dotnet run` | Error-only filtering | 60–80% |
| `dtk dotnet publish` | `dotnet publish` | Strip restore noise, keep output path + errors | 80–85% |
| `dtk dotnet restore` | `dotnet restore` | Compact: "✓ restored N packages (Xs)" | 90–95% |
| `dtk dotnet pack` | `dotnet pack` | Strip compile noise, keep .nupkg path | 85–90% |
| `dtk dotnet clean` | `dotnet clean` | "✓ clean" | 95%+ |
| `dtk dotnet ef` | `dotnet ef` | Strip verbose EF output, keep migration status | 70–80% |
| `dtk dotnet format` | `dotnet format` | Files changed only | 70–80% |
| `dtk dotnet nuget` | `dotnet nuget` | Strip progress, keep results | 75–85% |

Plus:

- **Passthrough** for any unrecognized `dotnet` subcommand
- **Token tracking** via SQLite
- **`dtk gain`** command to view savings analytics

## Technology Stack

| Concern | Technology | Purpose |
|---|---|---|
| Runtime | .NET 10 | Latest LTS, Native AOT support |
| CLI Framework | Spectre.Console.Cli | Command parsing, rich terminal output |
| Console Rendering | Spectre.Console | Tables, colors, markup, progress |
| Architecture | Clean Architecture | Separation of concerns, testability |
| Regex | `[GeneratedRegex]` source generators | Zero-runtime-cost compiled patterns |
| Database | Microsoft.Data.Sqlite | SQLite-based token tracking |
| Configuration | System.Text.Json | JSON config loading/saving |
| Distribution | .NET Global Tool / Native AOT | `dotnet tool install -g dtk` or single binary |

## Architecture: Clean Architecture

DTK follows Clean Architecture with four layers:

```
DotnetTokenKiller.Domain          ← Entities, interfaces, value objects (no dependencies)
DotnetTokenKiller.Application     ← Use cases, filter orchestration, command handlers
DotnetTokenKiller.Infrastructure  ← SQLite, config files, process execution, file I/O
DotnetTokenKiller.Cli             ← Spectre.Console entry point, command definitions
```

**Dependency rule**: inner layers never depend on outer layers. The Domain layer has zero external dependencies.

## Key Design Principles

1. **Clean Architecture** — strict layer separation, dependency inversion
2. **Single Responsibility** — each filter module handles one subcommand
3. **Minimal Overhead** — startup must be imperceptible
4. **Exit Code Preservation** — CI/CD reliability
5. **Fail-Safe** — if filtering fails, fall back to raw output
6. **Transparent** — `-v` flags show debug/raw output
7. **Testable** — domain and application layers are fully unit-testable without I/O

## Namespace Convention

All code lives under the `DotnetTokenKiller` root namespace. The CLI command remains `dtk` for brevity.

| Project | Namespace | Example |
|---|---|---|
| Domain | `DotnetTokenKiller.Domain` | `DotnetTokenKiller.Domain.Filters.IOutputFilter` |
| Application | `DotnetTokenKiller.Application` | `DotnetTokenKiller.Application.UseCases.FilteredRunUseCase` |
| Infrastructure | `DotnetTokenKiller.Infrastructure` | `DotnetTokenKiller.Infrastructure.Tracking.Tracker` |
| CLI | `DotnetTokenKiller.Cli` | `DotnetTokenKiller.Cli.Commands.DotnetBuildCommand` |

## Document Map

| File | Contents |
|---|---|
| [01-OVERVIEW.md](01-OVERVIEW.md) | This file — project overview and scope |
| [02-PROJECT-STRUCTURE.md](02-PROJECT-STRUCTURE.md) | Clean Architecture solution structure, projects, dependencies |
| [03-CLI-PARSING.md](03-CLI-PARSING.md) | Spectre.Console.Cli setup, command routing |
| [04-CORE-INFRASTRUCTURE.md](04-CORE-INFRASTRUCTURE.md) | Tracking, config, tee, utilities |
| [05-FILTER-PATTERNS.md](05-FILTER-PATTERNS.md) | Filtering strategies and patterns |
| [06-DOTNET-BUILD-FILTER.md](06-DOTNET-BUILD-FILTER.md) | `dotnet build` filter specification |
| [07-DOTNET-TEST-FILTER.md](07-DOTNET-TEST-FILTER.md) | `dotnet test` filter specification |
| [08-DOTNET-OTHER-FILTERS.md](08-DOTNET-OTHER-FILTERS.md) | restore, publish, pack, clean, ef, format, nuget |
| [09-TESTING-STRATEGY.md](09-TESTING-STRATEGY.md) | Testing approach, fixtures, snapshots |
| [10-BUILD-AND-DISTRIBUTION.md](10-BUILD-AND-DISTRIBUTION.md) | Build optimizations, NuGet packaging, AOT |
