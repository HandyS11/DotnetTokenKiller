---
project: DotnetTokenKiller
version: 0.1.0
status: approved
source: todo/02-PROJECT-STRUCTURE.md, todo/03-CLI-PARSING.md, todo/04-CORE-INFRASTRUCTURE.md, todo/05-FILTER-PATTERNS.md, todo/06-DOTNET-BUILD-FILTER.md, todo/07-DOTNET-TEST-FILTER.md, todo/08-DOTNET-OTHER-FILTERS.md, todo/09-TESTING-STRATEGY.md, todo/10-BUILD-AND-DISTRIBUTION.md
---

# DotnetTokenKiller — Architecture Document

## 1. Architecture Overview

DotnetTokenKiller follows **Clean Architecture** with strict dependency inversion. The system is structured as four projects organized by responsibility. Dependencies flow inward only: outer layers depend on inner layers, never the reverse.

```sh
DotnetTokenKiller.Cli          (entry point, composition root)
  ├── DotnetTokenKiller.Application   (use cases, filters, helpers)
  │     └── DotnetTokenKiller.Domain  (contracts, entities, value objects)
  └── DotnetTokenKiller.Infrastructure (I/O: SQLite, files, processes, config)
        └── DotnetTokenKiller.Domain
```

**Dependency rule**: `Domain` has no external dependencies. `Application` references `Domain` only. `Infrastructure` references `Domain` only. `Cli` references all layers (composition root).

## 2. Technology Stack

| Concern | Technology | Notes |
|---|---|---|
| Runtime | .NET 10 / C# 14 | Latest; Native AOT support |
| CLI Framework | Spectre.Console.Cli | Command parsing, rich rendering |
| Console Rendering | Spectre.Console | Tables, markup, colors for `dtk gain` |
| DI Container | Microsoft.Extensions.DependencyInjection | Wired via TypeRegistrar |
| Regex | `[GeneratedRegex]` source generators | Zero runtime cost; AOT-safe |
| Database | Microsoft.Data.Sqlite | Token tracking persistence |
| Config Serialization | System.Text.Json + JsonSerializerContext | Source-generated; AOT-safe |
| Process Execution | System.Diagnostics.Process | Command runner implementation |
| Testing | xunit + FluentAssertions + Verify.Xunit + NSubstitute | See §8 |
| Distribution | .NET Global Tool / Native AOT | See §9 |

## 3. Solution Structure

```sh
DotnetTokenKiller/
├── DotnetTokenKiller.slnx
├── Directory.Build.props              # Shared: net10.0, C#14, TreatWarningsAsErrors, AnalysisLevel=latest-all
├── Directory.Packages.props           # Central package versions — .csproj files omit versions
├── global.json                        # SDK version pin
├── NuGet.config
│
├── src/
│   ├── DotnetTokenKiller.Domain/
│   │   ├── Filters/IOutputFilter.cs
│   │   ├── Tracking/ITracker.cs
│   │   ├── Tracking/CommandRecord.cs
│   │   ├── Tracking/GainSummary.cs
│   │   ├── Configuration/IConfigProvider.cs
│   │   ├── Configuration/DtkConfig.cs        # TrackingConfig, DisplayConfig, TeeConfig
│   │   ├── Execution/ICommandRunner.cs
│   │   ├── Execution/CommandResult.cs
│   │   └── Tee/ITeeService.cs
│   │
│   ├── DotnetTokenKiller.Application/
│   │   ├── UseCases/FilteredRunUseCase.cs
│   │   ├── UseCases/GainReportUseCase.cs
│   │   ├── Filters/DotnetBuildFilter.cs
│   │   ├── Filters/DotnetTestFilter.cs
│   │   ├── Filters/DotnetRestoreFilter.cs
│   │   ├── Filters/DotnetCleanFilter.cs
│   │   └── Helpers/TokenEstimator.cs
│   │   └── Helpers/AnsiStrip.cs
│   │   └── Helpers/TextHelpers.cs
│   │
│   ├── DotnetTokenKiller.Infrastructure/
│   │   ├── Tracking/SqliteTracker.cs
│   │   ├── Configuration/JsonConfigProvider.cs
│   │   ├── Execution/ProcessCommandRunner.cs
│   │   ├── Tee/FileTeeService.cs
│   │   └── DependencyInjection.cs
│   │
│   └── DotnetTokenKiller.Cli/
│       ├── Program.cs
│       ├── Commands/DotnetBuildCommand.cs
│       ├── Commands/DotnetTestCommand.cs
│       ├── Commands/DotnetRestoreCommand.cs
│       ├── Commands/DotnetCleanCommand.cs
│       ├── Commands/GainCommand.cs
│       ├── Commands/Settings/DotnetCommandSettings.cs
│       ├── Commands/Settings/GainCommandSettings.cs
│       └── Infrastructure/TypeRegistrar.cs
│
└── tests/
    ├── DotnetTokenKiller.Domain.Tests/
    ├── DotnetTokenKiller.Application.Tests/
    │   ├── Filters/
    │   ├── Fixtures/                   # Real dotnet command output (embedded resources)
    │   └── Snapshots/                  # Verify.NET .verified.txt files
    ├── DotnetTokenKiller.Infrastructure.Tests/
    └── DotnetTokenKiller.Cli.IntegrationTests/
```

## 4. Layer Responsibilities

### Domain Layer — `DotnetTokenKiller.Domain`

The innermost layer. **Zero external NuGet dependencies. Zero project references.**

- **Interfaces (contracts)**: `IOutputFilter`, `ICommandRunner`, `ITracker`, `ITeeService`, `IConfigProvider`
- **Entities**: `CommandRecord`, `GainSummary`
- **Value objects**: `CommandResult`, `DtkConfig` hierarchy (`TrackingConfig`, `DisplayConfig`, `TeeConfig`)

### Application Layer — `DotnetTokenKiller.Application`

Business logic. **References Domain only.**

- **Use cases**: `FilteredRunUseCase`, `GainReportUseCase`
- **Filter implementations**: All `IOutputFilter` implementations (one class per subcommand)
- **Helpers**: `TokenEstimator`, `AnsiStrip`, `TextHelpers` — pure static functions, no I/O

### Infrastructure Layer — `DotnetTokenKiller.Infrastructure`

Implements Domain interfaces with real I/O. **References Domain only.**

- `SqliteTracker` → `ITracker`
- `ProcessCommandRunner` → `ICommandRunner`
- `FileTeeService` → `ITeeService`
- `JsonConfigProvider` → `IConfigProvider`

### CLI Layer — `DotnetTokenKiller.Cli`

Composition root and entry point. **References all layers.**

- `Program.cs` — configures DI and Spectre.Console `CommandApp`
- Command classes — thin adapters delegating to Application use cases
- `TypeRegistrar` / `TypeResolver` — Spectre.Console DI bridge

## 5. CLI Command Tree

```sh
dtk
├── dotnet
│   ├── build [args...]      → DotnetBuildCommand   → FilteredRunUseCase + DotnetBuildFilter
│   ├── test [args...]       → DotnetTestCommand    → FilteredRunUseCase + DotnetTestFilter
│   ├── restore [args...]    → DotnetRestoreCommand → FilteredRunUseCase + DotnetRestoreFilter
│   └── clean [args...]      → DotnetCleanCommand   → FilteredRunUseCase + DotnetCleanFilter
└── gain                     → GainCommand          → GainReportUseCase
```

## 6. Core Data Flow — FilteredRunUseCase

Every filtered `dotnet` subcommand flows through `FilteredRunUseCase`:

1. **Start timer** — begin wall-clock measurement
2. **Execute** — `ICommandRunner.RunCapturedAsync(command, args)` captures stdout + stderr
3. **Combine** — merge stdout and stderr into a single raw string
4. **Strip ANSI** — `AnsiStrip.Strip(raw)` before token estimation and filtering
5. **Apply filter** — `IOutputFilter.Apply(raw)` with the command-specific filter
6. **Tee** — `ITeeService.TeeAndHintAsync(raw, commandSlug, exitCode)` on failure
7. **Print** — write filtered output (+ tee hint if any) to console
8. **Track** — `ITracker.RecordAsync(...)` with input/output token counts and timing
9. **Return exit code** — propagate underlying process exit code unchanged

### Fail-Safe

If step 5 throws, the use case catches the exception, falls back to printing raw output, and continues from step 6. Tracking and tee errors (steps 6, 8) are always swallowed silently.

### Verbosity

- Level 1 (`-v`): print the command being executed before step 2
- Level 2 (`-vv`): print raw output before step 5, plus timing and any filter errors

## 7. Filter Design

### IOutputFilter Contract

```csharp
// Domain layer — pure function, no I/O, no state
public interface IOutputFilter
{
    string Apply(string rawOutput);
}
```

Filters must be:

- **Stateless** — no instance state between calls
- **Side-effect-free** — no console writes, no file I/O, no config reads
- **Non-throwing** — catch internally and return best-effort output; `FilteredRunUseCase` wraps all calls anyway

### Regex Patterns

All regex patterns use `[GeneratedRegex]` for compile-time source generation:

- Zero runtime compilation cost
- AOT-compatible
- Declared as `private static partial` methods on each filter class

### Common Noise Lines (all filters strip these)

| Pattern | Source |
|---|---|
| `MSBuild version 17.x.x+...` | Build header |
| `Determining projects to restore...` | Restore progress |
| `All projects are up-to-date for restore.` | Restore result |
| `Restored D:\path\Project.csproj (in N ms).` | Per-project restore |
| `Build started ...` | Build header |
| Blank lines | Padding |

### Output Format

| Outcome | Format |
|---|---|
| Clean success | `✓ dotnet <cmd> (<context>, <time>)` |
| Success with warnings | `dotnet <cmd>: 0 errors, N warnings` + `═══` separator + grouped details |
| Failure | `dotnet <cmd>: N errors, M warnings` + `═══` separator + structured diagnostics |

### Path Shortening

All filters shorten absolute paths:

- Convert to project-relative using forward slashes
- Fall back to filename only if relative computation fails
- Example: `D:\projects\MyApp\Controllers\HomeController.cs` → `Controllers/HomeController.cs`

## 8. Infrastructure Details

### Token Tracking — `SqliteTracker`

- **Schema**: single `commands` table with indexed `timestamp` and `project_path` columns
- **DB path**: `%LOCALAPPDATA%/dtk/tracking.db` (Windows) / `~/.local/share/dtk/tracking.db` (Linux/macOS)
- **Override**: `DTK_DB_PATH` environment variable
- **Auto-create**: schema created on first use
- **Retention**: 90-day rolling window; `CleanupAsync` called on every `RecordAsync`
- **Silent failure**: all errors swallowed — tracking never breaks command output

### Configuration — `JsonConfigProvider`

- **Config path**: `%APPDATA%/dtk/config.json` (Windows) / `~/.config/dtk/config.json` (Linux/macOS)
- **Serialization**: `System.Text.Json` with `JsonSerializerContext` (source-generated, AOT-safe)
- **Auto-create**: directory created if missing; defaults returned if no file exists

### Tee Service — `FileTeeService`

- **Tee path**: `%LOCALAPPDATA%/dtk/tee/` (Windows) / `~/.local/share/dtk/tee/` (Linux/macOS)
- **Override**: `DTK_TEE_DIR` environment variable or config
- **File naming**: `{unix_timestamp}_{command_slug}.log`
- **Hint format**: `[full output: /path/to/file.log]`
- **Modes**: `never` / `failures` (default) / `always`
- **Guards**: skip if raw output <500 chars; enforce maxFiles (20) and maxFileSize (1 MB)
- **Silent failure**: all I/O errors swallowed

### Process Execution — `ProcessCommandRunner`

- `RunCapturedAsync`: redirects stdout/stderr, reads both streams **concurrently** (prevents deadlock on large output), waits for exit, returns `CommandResult`
- `RunPassthroughAsync`: inherits all I/O streams, waits for exit, returns exit code only

### Token Estimation

```sh
tokenCount = text.Length / 4
```

`chars / 4` heuristic — same as RTK. Fast approximation, not exact tokenizer output. Applied after ANSI stripping.

## 9. DI Registration

Registration in `Program.cs` (CLI composition root):

```sh
ICommandRunner     → ProcessCommandRunner
ITracker           → SqliteTracker
IConfigProvider    → JsonConfigProvider
ITeeService        → FileTeeService
FilteredRunUseCase → (transient, per-command)
GainReportUseCase  → (transient)
DotnetBuildFilter  → (singleton)
... (all filter classes)
```

Spectre.Console integration via `TypeRegistrar` / `TypeResolver` — wraps `IServiceCollection` / `IServiceProvider`.

## 10. Testing Strategy

### Test Projects by Layer

| Project | Scope | Key Tools |
|---|---|---|
| `Domain.Tests` | Entity validation, value object equality | xunit, FluentAssertions |
| `Application.Tests` | Filters (fixture-based), use cases (mocked), helpers (unit) | xunit, FluentAssertions, Verify.Xunit, NSubstitute |
| `Infrastructure.Tests` | SqliteTracker (in-memory), JsonConfigProvider (temp dir), FileTeeService (temp dir) | xunit, FluentAssertions |
| `Cli.IntegrationTests` | Full `dtk` binary execution | xunit, `[Trait("Category","Integration")]` |

### Filter Testing Pattern

1. **Fixture files** — real `dotnet` command output captured and stored as embedded resources in `Application.Tests/Fixtures/`
2. **Snapshot tests** — `Verify(filter.Apply(fixture))` compared against `.verified.txt` baseline
3. **Savings gate** — `savings = 100 - (outputTokens / inputTokens * 100)` asserted ≥60% (hard gate)
4. **Behavioral tests** — specific output format assertions (prefix, separator, path format)
5. **Edge cases** — empty string, null, ANSI codes, Unicode, >1 MB input

### Use Case Testing Pattern

- Mock `ICommandRunner` to return predefined `CommandResult`
- Mock `ITeeService` to verify tee called on non-zero exit
- Mock `ITracker` to verify `RecordAsync` called with correct token counts
- Verify filter exception → raw output fallback
- Verify exit code propagation

### Infrastructure Testing Pattern

- `SqliteTracker`: `Data Source=:memory:` for full isolation
- `JsonConfigProvider`: `Path.GetTempPath()` directory, cleaned up in `Dispose`
- `FileTeeService`: temp directory, cleaned up in `Dispose`

### Quality Gate (per filter)

- [ ] Real fixture file captured and added as embedded resource
- [ ] Snapshot test with `Verify(output)`
- [ ] Savings test asserting ≥60%
- [ ] Empty string input handled
- [ ] Null input handled
- [ ] ANSI codes stripped correctly
- [ ] Multiple projects (solution-level output) handled
- [ ] All tests pass: `dotnet test DotnetTokenKiller.slnx`

## 11. Build and Distribution

### Global Tool (Primary — Phase 1)

```xml
<PackAsTool>true</PackAsTool>
<ToolCommandName>dtk</ToolCommandName>
<PackageId>DotnetTokenKiller</PackageId>
```

Install: `dotnet tool install -g DotnetTokenKiller`

### Native AOT (Secondary — Phase 2)

All code must be AOT-safe:

| Pattern | AOT Status |
|---|---|
| `[GeneratedRegex]` | Safe |
| `new Regex(...)` at runtime | Unsafe — forbidden |
| `System.Text.Json` + `JsonSerializerContext` | Safe |
| `JsonSerializer.Deserialize<T>` (reflection) | Unsafe — forbidden |
| `Microsoft.Data.Sqlite` | Safe (needs `SQLitePCLRaw.bundle_e_sqlite3`) |
| `Spectre.Console.Cli` | Safe |

AOT publish properties: `PublishAot=true`, `StripSymbols=true`, `OptimizationPreference=Size`, `InvariantGlobalization=true`, `TrimMode=link`.

### Performance Targets

| Metric | Global Tool | Native AOT |
|---|---|---|
| Startup | <150ms | <15ms |
| Memory | <30 MB | <10 MB |
| Binary size | N/A | <15 MB |

### CI/CD Quality Gate Pipeline

Runs on every push and PR:

1. `dotnet format DotnetTokenKiller.slnx --no-restore --verify-no-changes`
2. `dotnet build DotnetTokenKiller.slnx --no-restore -warnaserror`
3. `dotnet test DotnetTokenKiller.slnx --no-build --logger trx`
4. Upload TRX test results as artifact

## 12. Key Design Decisions

| Decision | Choice | Rationale |
|---|---|---|
| Config format | JSON + source-gen | No extra dependency; System.Text.Json built-in; AOT-compatible |
| Tracking DB | SQLite | Single-file; proven; no server; same as RTK |
| Token estimate | `chars / 4` | Fast; good-enough approximation; matches RTK |
| Tracking/tee errors | Silent failure | These features must never break the user's workflow |
| Process I/O | Concurrent stream reads | Prevents deadlock when both stdout and stderr are large |
| Regex | `[GeneratedRegex]` | Zero runtime cost; AOT-safe; zero-allocation where possible |
| All contracts in Domain | Interfaces + value objects | Enables unit testing Application layer without any real I/O |
| CLI framework | Spectre.Console.Cli | Stable; AOT-safe; rich rendering for `dtk gain` table |
