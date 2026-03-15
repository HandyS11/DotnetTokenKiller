# Story 9.2: Remove Passthrough Use Case and CLI Commands

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a developer maintaining DotnetTokenKiller,
I want the `PassthroughRunUseCase` and all non-core CLI command classes deleted, along with their DI registrations and `Program.cs` routing,
So that `dtk --help` shows only the four core subcommands (`build`, `test`, `restore`, `clean`) and there is no dead routing code.

## Acceptance Criteria

1. **Given** `dtk --help` is run after deletions **When** the help output is displayed **Then** subcommands listed are exactly: `dotnet build`, `dotnet test`, `dotnet restore`, `dotnet clean`, `gain` — and no `publish`, `pack`, `run`, `ef`, `format`, `nuget`, or passthrough entries appear.

2. **Given** `dotnet build DotnetTokenKiller.slnx` is run after deletions **When** the build completes **Then** there are zero errors and zero warnings (TreatWarningsAsErrors is enabled).

3. **Given** `dotnet test DotnetTokenKiller.slnx` is run after deletions **When** the test run completes **Then** all remaining tests pass with zero failures and zero regressions.

4. **Given** `src/DotnetTokenKiller.Cli/Program.cs` is inspected **When** the command configuration block is read **Then** only `DotnetBuildCommand`, `DotnetTestCommand`, `DotnetRestoreCommand`, `DotnetCleanCommand`, and `GainCommand` are registered — no passthrough pre-intercept logic and no `SetDefaultCommand` to a passthrough handler.

5. **Given** `src/DotnetTokenKiller.Application/DependencyInjection.cs` is inspected **When** `AddApplication()` is read **Then** `PassthroughRunUseCase` is not registered — only `FilteredRunUseCase`, `GainReportUseCase`, and the four core filter singletons appear.

6. **Given** the `src/DotnetTokenKiller.Cli/Commands/` directory is inspected **When** files are listed **Then** only `DotnetBuildCommand.cs`, `DotnetTestCommand.cs`, `DotnetRestoreCommand.cs`, `DotnetCleanCommand.cs`, and `GainCommand.cs` exist — no `DotnetPublishCommand.cs`, `DotnetPackCommand.cs`, `DotnetRunCommand.cs`, `DotnetEfCommand.cs`, `DotnetFormatCommand.cs`, `DotnetNugetCommand.cs`, or `DotnetPassthroughCommand.cs`.

## Tasks / Subtasks

- [x] Task 1 — Delete `PassthroughRunUseCase` and its test (AC: #5)
  - [x] Delete `src/DotnetTokenKiller.Application/UseCases/PassthroughRunUseCase.cs`
  - [x] Delete `tests/DotnetTokenKiller.Application.Tests/UseCases/PassthroughRunUseCaseTests.cs`

- [x] Task 2 — Delete non-core CLI command classes (AC: #6)
  - [x] Delete `src/DotnetTokenKiller.Cli/Commands/DotnetPublishCommand.cs`
  - [x] Delete `src/DotnetTokenKiller.Cli/Commands/DotnetPackCommand.cs`
  - [x] Delete `src/DotnetTokenKiller.Cli/Commands/DotnetRunCommand.cs`
  - [x] Delete `src/DotnetTokenKiller.Cli/Commands/DotnetEfCommand.cs`
  - [x] Delete `src/DotnetTokenKiller.Cli/Commands/DotnetFormatCommand.cs`
  - [x] Delete `src/DotnetTokenKiller.Cli/Commands/DotnetNugetCommand.cs`
  - [x] Delete `src/DotnetTokenKiller.Cli/Commands/DotnetPassthroughCommand.cs`

- [x] Task 3 — Update `Program.cs` to remove dead routing (AC: #1, #4)
  - [x] Remove passthrough pre-intercept routing block (the logic that detected unrecognized subcommands before normal dispatch)
  - [x] Remove the 6 non-core `AddCommand` lines (`publish`, `pack`, `run`, `ef`, `format`, `nuget`) from the dotnet branch
  - [x] Remove `SetDefaultCommand` wiring to the passthrough handler
  - [x] Confirm remaining registered commands: `dotnet build`, `dotnet test`, `dotnet restore`, `dotnet clean`, `gain`

- [x] Task 4 — Update `DependencyInjection.cs` to remove `PassthroughRunUseCase` (AC: #5)
  - [x] Remove `services.AddTransient<PassthroughRunUseCase>()` (and the `using DotnetTokenKiller.Application.UseCases;` import if it becomes unused)

- [x] Task 5 — Verify build and tests pass (AC: #2, #3)
  - [x] Run `dotnet build DotnetTokenKiller.slnx` — confirm zero errors, zero warnings
  - [x] Run `dotnet test DotnetTokenKiller.slnx` — confirm all tests pass

## Dev Notes

### ⚠️ Batch Implementation Note

**This story's changes were already applied in the Story 9-1 dev session** as a required batch (see Story 9-1 completion notes). The Story 9-1 dev notes explain:

> "Story 9-1 cannot produce a passing build in isolation. CLI commands in `src/DotnetTokenKiller.Cli/Commands/` directly inject filter classes as constructor parameters. Deleting filter files without simultaneously removing those CLI command files will cause compilation errors."

All tasks above are marked complete. The final state of the codebase already reflects this story's requirements. **No implementation work is needed — proceed directly to verification and status update.**

---

### Scope of This Story (9-2 Only)

**This story is strictly scoped to:**

- `PassthroughRunUseCase.cs` and its unit test
- All 7 non-core CLI command classes (Publish, Pack, Run, Ef, Format, Nuget, Passthrough)
- Routing and DI registration cleanup in `Program.cs` and `DependencyInjection.cs`

**Out of scope for 9-2:**

- Application filter files — Story 9-1 (already done)
- Integration test classes for removed commands — Story 9-3
- Sample projects and fixtures — Story 9-4
- Planning artifact updates — Story 9-5

---

### Final State of Key Files

#### `src/DotnetTokenKiller.Cli/Program.cs` (post-batch)

```csharp
using DotnetTokenKiller.Application;
using DotnetTokenKiller.Cli.Commands;
using DotnetTokenKiller.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Spectre.Console;
using Spectre.Console.Cli;
using System.Reflection;
using DtkTypeRegistrar = DotnetTokenKiller.Cli.Infrastructure.TypeRegistrar;

var services = new ServiceCollection();
services.AddInfrastructure();
services.AddApplication();
services.AddSingleton<IAnsiConsole>(_ => AnsiConsole.Console);

var registrar = new DtkTypeRegistrar(services);
var app = new CommandApp(registrar);

app.Configure(config =>
{
    config.SetApplicationName("dtk");
    var version = typeof(Program).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()
        ?.InformationalVersion ?? "0.0.0";
    config.SetApplicationVersion(version);
    config.Settings.StrictParsing = false;

    config.AddBranch("dotnet", dotnet =>
    {
        dotnet.SetDescription("Run dotnet commands with filtered output");
        dotnet.AddCommand<DotnetBuildCommand>("build").WithDescription("Run dotnet build with filtered output");
        dotnet.AddCommand<DotnetTestCommand>("test").WithDescription("Run dotnet test with filtered output");
        dotnet.AddCommand<DotnetRestoreCommand>("restore").WithDescription("Run dotnet restore with filtered output");
        dotnet.AddCommand<DotnetCleanCommand>("clean").WithDescription("Run dotnet clean with filtered output");
    });

    config.AddCommand<GainCommand>("gain").WithDescription("Show token savings analytics");
});

return await app.RunAsync(args);
```

#### `src/DotnetTokenKiller.Application/DependencyInjection.cs` (post-batch)

```csharp
public static IServiceCollection AddApplication(this IServiceCollection services)
{
    services.AddTransient<FilteredRunUseCase>();
    services.AddTransient<GainReportUseCase>();
    services.AddSingleton<DotnetBuildFilter>();
    services.AddSingleton<DotnetTestFilter>();
    services.AddSingleton<DotnetRestoreFilter>();
    services.AddSingleton<DotnetCleanFilter>();
    return services;
}
```

---

### CLI Architecture (Spectre.Console.Cli)

- CLI framework: `Spectre.Console.Cli` (`CommandApp` + `CommandApp<T>`)
- Commands are registered via `config.AddBranch` (for grouped subcommands) and `config.AddCommand<T>`
- `TypeRegistrar` in `DotnetTokenKiller.Cli.Infrastructure` wraps `IServiceCollection` for DI
- `StrictParsing = false` means unrecognized args are not treated as errors at the CLI level
- **There is no passthrough fallback command** — unrecognized subcommands now simply show the help message

---

### Application Architecture

- All command handlers (CLI) call application use cases via constructor injection
- `FilteredRunUseCase` handles the 4 core filters (build, test, restore, clean)
- `GainReportUseCase` handles analytics
- `PassthroughRunUseCase` is gone — it was the handler for unrecognized commands, which now has no routing entry point

---

### Testing Standards

- Framework: xUnit + FluentAssertions
- Snapshot testing: `Verify.Xunit` v28 — `Verifier.Verify(result)` (no `[UsesVerify]`, no `VerifyBase`)
- `PassthroughRunUseCaseTests.cs` covered `PassthroughRunUseCase` — this file is deleted alongside the source

**Retained tests (do not touch):**

- All `DotnetBuildFilter`, `DotnetTestFilter`, `DotnetRestoreFilter`, `DotnetCleanFilter` tests and snapshots
- `FilteredRunUseCaseTests.cs`, `GainReportUseCaseTests.cs`
- Integration tests for `build`, `test`, `restore`, `clean` — those are retained

---

### Code Style Constraints

- `TreatWarningsAsErrors` is enabled — zero warnings required
- File-scoped namespaces (`namespace Foo;`)
- Unused `using` statements cause analyzer warnings — verify `DependencyInjection.cs` doesn't retain orphaned imports after removing `PassthroughRunUseCase`
- Run `dotnet format DotnetTokenKiller.slnx --no-restore` after edits if uncertain

---

### Project Structure Notes

**Files DELETED (this story):**

```sh
src/DotnetTokenKiller.Application/UseCases/PassthroughRunUseCase.cs
src/DotnetTokenKiller.Cli/Commands/DotnetPublishCommand.cs
src/DotnetTokenKiller.Cli/Commands/DotnetPackCommand.cs
src/DotnetTokenKiller.Cli/Commands/DotnetRunCommand.cs
src/DotnetTokenKiller.Cli/Commands/DotnetEfCommand.cs
src/DotnetTokenKiller.Cli/Commands/DotnetFormatCommand.cs
src/DotnetTokenKiller.Cli/Commands/DotnetNugetCommand.cs
src/DotnetTokenKiller.Cli/Commands/DotnetPassthroughCommand.cs
tests/DotnetTokenKiller.Application.Tests/UseCases/PassthroughRunUseCaseTests.cs
```

**Files MODIFIED (this story):**

```sh
src/DotnetTokenKiller.Cli/Program.cs               ← remove passthrough routing + 6 AddCommand lines
src/DotnetTokenKiller.Application/DependencyInjection.cs  ← remove PassthroughRunUseCase registration
```

**Files RETAINED UNCHANGED:**

```sh
src/DotnetTokenKiller.Cli/Commands/DotnetBuildCommand.cs
src/DotnetTokenKiller.Cli/Commands/DotnetTestCommand.cs
src/DotnetTokenKiller.Cli/Commands/DotnetRestoreCommand.cs
src/DotnetTokenKiller.Cli/Commands/DotnetCleanCommand.cs
src/DotnetTokenKiller.Cli/Commands/GainCommand.cs
```

### References

- Story 9-1 file (batch implementation context): [_bmad-output/implementation-artifacts/9-1-remove-non-core-application-filters.md](_bmad-output/implementation-artifacts/9-1-remove-non-core-application-filters.md)
- Sprint Change Proposal: [_bmad-output/planning-artifacts/sprint-change-proposal-2026-03-15.md](_bmad-output/planning-artifacts/sprint-change-proposal-2026-03-15.md) — Section 4, Section 5 (execution order)
- Epics file: [_bmad-output/planning-artifacts/epics.md](_bmad-output/planning-artifacts/epics.md#story-92-remove-passthrough-use-case-and-cli-commands) — Epic 9, Story 9.2 definition
- Architecture: [_bmad-output/planning-artifacts/Architecture.md](_bmad-output/planning-artifacts/Architecture.md) — CLI layer, Application layer structure

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

### Completion Notes List

- ✅ Batch implementation: All 9-2 changes applied during Story 9-1 dev session (required batch due to direct CLI→Filter dependency chain).
- ✅ Task 1 complete: `PassthroughRunUseCase.cs` and `PassthroughRunUseCaseTests.cs` deleted.
- ✅ Task 2 complete: 7 non-core CLI command files deleted (Publish, Pack, Run, Ef, Format, Nuget, Passthrough).
- ✅ Task 3 complete: `Program.cs` updated — passthrough pre-intercept removed, 6 `AddCommand` lines removed, `SetDefaultCommand` removed. Only `build`, `test`, `restore`, `clean`, `gain` registered.
- ✅ Task 4 complete: `DependencyInjection.cs` — `PassthroughRunUseCase` registration and `using DotnetTokenKiller.Application.UseCases;` removed (handled in 9-1 batch).
- ✅ Task 5 complete: `dotnet build` — 0 errors, 0 warnings. `dotnet test` — 149/149 pass.

### File List

**Deleted:**

- src/DotnetTokenKiller.Application/UseCases/PassthroughRunUseCase.cs
- src/DotnetTokenKiller.Cli/Commands/DotnetPublishCommand.cs
- src/DotnetTokenKiller.Cli/Commands/DotnetPackCommand.cs
- src/DotnetTokenKiller.Cli/Commands/DotnetRunCommand.cs
- src/DotnetTokenKiller.Cli/Commands/DotnetEfCommand.cs
- src/DotnetTokenKiller.Cli/Commands/DotnetFormatCommand.cs
- src/DotnetTokenKiller.Cli/Commands/DotnetNugetCommand.cs
- src/DotnetTokenKiller.Cli/Commands/DotnetPassthroughCommand.cs
- tests/DotnetTokenKiller.Application.Tests/UseCases/PassthroughRunUseCaseTests.cs

**Modified:**

- src/DotnetTokenKiller.Cli/Program.cs
- src/DotnetTokenKiller.Application/DependencyInjection.cs
