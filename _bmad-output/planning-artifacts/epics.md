---
stepsCompleted: [step-01-validate-prerequisites, step-02-design-epics, step-03-create-stories, step-04-final-validation]
inputDocuments:
  - _bmad-output/planning-artifacts/PRD.md
  - _bmad-output/planning-artifacts/Architecture.md
---

# DotnetTokenKiller - Epic Breakdown

## Overview

This document provides the complete epic and story breakdown for DotnetTokenKiller, decomposing the requirements from the project specification documents into implementable stories.

## Requirements Inventory

### Functional Requirements

FR1: The system shall provide a `dtk dotnet build` command that intercepts `dotnet build` output and filters it to strip restore/compile noise while keeping errors, warnings, and summary (80–90% token reduction)
FR2: The system shall provide a `dtk dotnet test` command that intercepts `dotnet test` output and filters it to show only failures and aggregated suite summary (90–95% token reduction)
FR3: The system shall provide a `dtk dotnet restore` command that intercepts `dotnet restore` output and compacts it to a one-line summary (90–95% token reduction)
FR4: The system shall provide a `dtk dotnet publish` command that intercepts `dotnet publish` output and strips restore noise while keeping the output path and any errors (80–85% token reduction)
FR5: The system shall provide a `dtk dotnet pack` command that intercepts `dotnet pack` output and strips compile noise while keeping the .nupkg path (85–90% token reduction)
FR6: The system shall provide a `dtk dotnet clean` command that intercepts `dotnet clean` output and reduces it to a success marker or error lines (95%+ token reduction)
FR7: The system shall provide a `dtk dotnet run` command that intercepts `dotnet run` output and strips only the build preamble, preserving actual application output (60–80% token reduction)
FR8: The system shall provide a `dtk dotnet ef` command that intercepts `dotnet ef` output and compacts migration/database status messages (70–80% token reduction)
FR9: The system shall provide a `dtk dotnet format` command that intercepts `dotnet format` output and shows only files changed or needing changes (70–80% token reduction)
FR10: The system shall provide a `dtk dotnet nuget` command that intercepts `dotnet nuget` output and strips progress bars while keeping results (75–85% token reduction)
FR11: The system shall support passthrough mode for any unrecognized `dotnet` subcommand (e.g., `dtk dotnet new`, `dtk dotnet watch`) with no filtering applied but exit code preserved
FR12: The system shall track every command execution (including passthrough) in a SQLite database recording: timestamp, command, project path, input tokens, output tokens, saved tokens, savings percentage, and execution time (passthrough commands record 0 for all token fields)
FR13: The system shall provide a `dtk gain` command that displays token savings analytics via a Spectre.Console rich table (with `--days`, `--project`, and `--json` options)
FR14: The system shall preserve the exit code of the underlying `dotnet` process exactly (zero for success, non-zero for failure)
FR15: The system shall forward all arguments after the subcommand name to the real `dotnet` process unchanged (e.g., `dtk dotnet build --configuration Release --no-restore`)
FR16: The system shall fall back to raw unfiltered output if the filter throws an exception, never producing empty or broken output
FR17: The system shall support verbosity flags (`-v`/`--verbose`) on all dotnet subcommands: level 1 shows the command being run, level 2 shows raw output before filtering and timing details
FR18: The system shall provide tee output recovery: on command failure, optionally save the full raw output to a timestamped file and append a one-line hint to the filtered output
FR19: The system shall load and save user configuration as JSON, with configurable tracking (enabled/days/db path), display (colors/emoji/width), and tee (mode/directory/max files/max size) settings
FR20: The system shall strip ANSI escape codes from captured output before filtering and token estimation
FR21: The system shall shorten absolute file paths to project-relative paths in all filter output
FR22: The system shall be packaged and distributed as a .NET Global Tool installable via `dotnet tool install -g DotnetTokenKiller` with command name `dtk`
FR23: The system shall auto-clean tracking records older than 90 days on every write operation
FR24: The system shall estimate token counts using the `chars / 4` heuristic for savings calculation

### NonFunctional Requirements

NFR1: Startup time must be imperceptible — <150ms for the global tool, <15ms for Native AOT binary
NFR2: Each filter must achieve the stated token reduction targets; a hard quality gate of ≥60% savings must pass for every filter against real fixture data
NFR3: The tool must be fail-safe — tracking and tee errors must never surface to the user or affect command output (silent failure)
NFR4: The Domain and Application layers must be fully unit-testable without any real I/O (all infrastructure behind interfaces)
NFR5: Each filter module must handle exactly one subcommand (Single Responsibility Principle)
NFR6: The tool must run cross-platform on Windows, macOS, and Linux without platform-specific code in the Domain or Application layers
NFR7: All regex patterns must use `[GeneratedRegex]` source generators; JSON serialization must use source-generated contexts — required for Native AOT compatibility
NFR8: Memory usage must be <30 MB for the global tool and <10 MB for the Native AOT binary
NFR9: Native AOT binary size must be <15 MB per platform
NFR10: The exit code returned to the caller must exactly match the underlying `dotnet` process exit code for CI/CD compatibility
NFR11: The tool must never consume more than a negligible amount of additional CPU time compared to running `dotnet` directly

### Additional Requirements

Architecture Requirements (from project specification):

- Clean Architecture with four projects: Domain, Application, Infrastructure, Cli — dependency rule strictly enforced (inner layers never depend on outer layers)
- Domain layer must have zero external NuGet dependencies — pure C# contracts (interfaces) and value objects only
- Application layer references Domain only; contains all filter implementations, use cases, and pure helper functions
- Infrastructure layer references Domain only; implements all I/O concerns (SQLite, process execution, file tee, JSON config)
- Cli layer is the composition root — references all other layers, configures DI, and bootstraps Spectre.Console CommandApp
- TypeRegistrar/TypeResolver pattern must be implemented for Spectre.Console DI integration
- SQLite tracking database path: `%LOCALAPPDATA%/dtk/tracking.db` (Windows), `~/.local/share/dtk/tracking.db` (Linux/macOS), overridable via `DTK_DB_PATH` env var
- Config file path: `%APPDATA%/dtk/config.json` (Windows), `~/.config/dtk/config.json` (Linux/macOS)
- Tee directory: `%LOCALAPPDATA%/dtk/tee/` (Windows), `~/.local/share/dtk/tee/` (Linux/macOS), overridable via `DTK_TEE_DIR` env var
- Schema auto-created on first use; directories auto-created if missing
- `ProcessCommandRunner` must read stdout and stderr concurrently to prevent deadlock for large outputs
- `SQLitePCLRaw.bundle_e_sqlite3` required for Native AOT SQLite bundling

Testing Requirements (from project specification):

- Snapshot tests using `Verify.Xunit` for all filter output formats
- Token savings accuracy tests must enforce ≥60% savings as a hard gate
- Real fixture files must be captured from actual `dotnet` command output and included as embedded resources
- Edge case tests required per filter: empty string, malformed/non-dotnet output, Unicode, ANSI escape codes, very large output (>1MB), null input
- Infrastructure tests use in-memory SQLite (`Data Source=:memory:`)
- CLI integration tests run the actual `dtk` binary and are marked `[Trait("Category", "Integration")]` for selective execution
- `NSubstitute` used for mocking Domain interfaces in Application use case tests

### FR Coverage Map

FR1: Epic 1 — `dotnet build` filter
FR2: Epic 2 — `dotnet test` filter
FR3: Epic 3 — `dotnet restore` filter
FR4: Epic 3 — `dotnet publish` filter
FR5: Epic 3 — `dotnet pack` filter
FR6: Epic 4 — `dotnet clean` filter
FR7: Epic 4 — `dotnet run` filter
FR8: Epic 4 — `dotnet ef` filter
FR9: Epic 4 — `dotnet format` filter
FR10: Epic 4 — `dotnet nuget` filter
FR11: Epic 4 — Passthrough for unrecognized subcommands
FR12: Epic 5 — SQLite token tracking
FR13: Epic 5 — `dtk gain` analytics command
FR14: Epic 1 — Exit code preservation
FR15: Epic 1 — Argument forwarding
FR16: Epic 1 — Fail-safe fallback to raw output
FR17: Epic 1 — Verbosity flags
FR18: Epic 6 — Tee output recovery
FR19: Epic 6 — JSON configuration
FR20: Epic 1 — ANSI escape code stripping
FR21: Epic 1 — Path shortening
FR22: Epic 7 — Global tool distribution
FR23: Epic 5 — 90-day retention cleanup
FR24: Epic 1 — Token estimation (chars/4 heuristic)

## Epic List

### Epic 1: Core Foundation & Build Filter

Users can install and run `dtk dotnet build` to receive dramatically compressed build output (80–90% token reduction). This epic establishes the entire Clean Architecture solution, Spectre.Console CLI routing, DI wiring, process execution pipeline, core helpers, the first fully working filter, and a minimal CI pipeline active from day one — the foundation all subsequent epics build upon.
**FRs covered:** FR1, FR14, FR15, FR16, FR17, FR20, FR21, FR24

### Epic 2: Test Filter Intelligence

Users can run `dtk dotnet test` and see only failed tests with error messages and source locations, plus an aggregated pass/fail summary — eliminating the verbose test runner output that dominates LLM context windows.
**FRs covered:** FR2

### Epic 3: Restore, Publish & Pack Filters

Users can run `dtk dotnet restore`, `dtk dotnet publish`, and `dtk dotnet pack` and receive one-line summaries instead of verbose MSBuild output, with meaningful output paths shown for publish and pack operations.
**FRs covered:** FR3, FR4, FR5

### Epic 4: Remaining Filters & Passthrough Coverage

Every `dotnet` subcommand works with DTK — `clean`, `run`, `ef`, `format`, and `nuget` all produce compact output, and any unrecognized subcommand silently passes through with exit code preserved. The filter suite is now complete.
**FRs covered:** FR6, FR7, FR8, FR9, FR10, FR11

### Epic 5: Token Savings Analytics

Users can run `dtk gain` and see a rich Spectre.Console table showing cumulative token savings across all commands — total tokens saved, savings percentages, per-command breakdown — with filtering by days, project, or JSON output.
**FRs covered:** FR12, FR13, FR23

### Epic 6: Configuration & Tee Recovery

Users can customize DTK's behavior via a JSON config file (tracking retention, display options, tee behavior), and when a command fails they automatically get the full raw output saved to a file with a one-line hint — so no output is ever lost.
**FRs covered:** FR18, FR19

### Epic 7: Distribution as .NET Global Tool

Any .NET developer can install DTK with `dotnet tool install -g DotnetTokenKiller`. The tool is correctly packaged as a NuGet global tool with metadata, versioning, and a CI/CD quality gate pipeline.
**FRs covered:** FR22

---

## Epic 1: Core Foundation & Build Filter

Establish the full Clean Architecture solution, Spectre.Console CLI routing, dependency injection, process execution pipeline, core application helpers, and the first fully working filter (`dotnet build`). After this epic, developers can run `dtk dotnet build` and receive dramatically compressed output with 80–90% token reduction, with argument forwarding, exit code preservation, fail-safe fallback, and verbosity support.

> **Note:** Stories 1.1–1.4 are foundational bootstrapping stories that deliver no standalone user-runnable value — a working `dtk` is only available after Story 1.5 completes. This is expected for a greenfield Clean Architecture project. Plan Epic 1 as a single uninterrupted delivery block.

### Story 1.1: Scaffold Clean Architecture Solution

As a developer,
I want the DotnetTokenKiller solution scaffolded with the correct Clean Architecture project structure and shared build configuration,
So that all subsequent stories have a consistent, compilable foundation to build upon.

**Acceptance Criteria:**

**Given** the repository contains only initial config files
**When** the solution is built with `dotnet build DotnetTokenKiller.slnx`
**Then** all four projects compile without errors or warnings: `DotnetTokenKiller.Domain`, `DotnetTokenKiller.Application`, `DotnetTokenKiller.Infrastructure`, `DotnetTokenKiller.Cli`
**And** project references follow the Clean Architecture dependency rule: Cli → Application + Infrastructure, Application → Domain, Infrastructure → Domain, Domain has no project references
**And** `Directory.Build.props` sets `TargetFramework=net10.0`, `LangVersion=14`, `Nullable=enable`, `ImplicitUsings=enable`, `TreatWarningsAsErrors=true`, `AnalysisLevel=latest-all`
**And** `Directory.Packages.props` exists with central package management (all NuGet versions defined here, `.csproj` files omit versions)
**And** the CLI project is configured as a global tool: `PackAsTool=true`, `ToolCommandName=dtk`, `PackageId=DotnetTokenKiller`
**And** four test projects exist mirroring the four src projects, each referencing `xunit`, `FluentAssertions`, `Microsoft.NET.Test.Sdk`
**And** `dotnet test DotnetTokenKiller.slnx` runs successfully (zero tests, zero failures)

---

### Story 1.2: Implement Core Domain Contracts and Value Objects

As a developer,
I want all Domain layer interfaces and value objects defined,
So that Application and Infrastructure layers have stable contracts to implement against without any I/O dependencies.

**Acceptance Criteria:**

**Given** the Domain project exists with no external NuGet dependencies
**When** the Domain project is inspected
**Then** `IOutputFilter` interface exists in `DotnetTokenKiller.Domain.Filters` with a single `string Apply(string rawOutput)` method
**And** `ICommandRunner` interface exists in `DotnetTokenKiller.Domain.Execution` with `RunCapturedAsync` and `RunPassthroughAsync` methods
**And** `CommandResult` record exists in `DotnetTokenKiller.Domain.Execution` with `StdOut`, `StdErr`, and `ExitCode` properties
**And** `ITracker` interface exists in `DotnetTokenKiller.Domain.Tracking` with `RecordAsync`, `GetSummaryAsync`, `GetHistoryAsync`, and `CleanupAsync` methods
**And** `CommandRecord` entity exists in `DotnetTokenKiller.Domain.Tracking` with all tracking fields (timestamp, command, project path, input/output tokens, saved tokens, savings percentage, execution time)
**And** `GainSummary` entity exists in `DotnetTokenKiller.Domain.Tracking` with aggregated analytics fields
**And** `IConfigProvider` interface exists in `DotnetTokenKiller.Domain.Configuration` with `LoadAsync` and `SaveAsync` methods
**And** `DtkConfig` value object hierarchy exists (`TrackingConfig`, `DisplayConfig`, `TeeConfig`) with sensible defaults
**And** `ITeeService` interface exists in `DotnetTokenKiller.Domain.Tee` with a `TeeAndHintAsync` method
**And** the Domain project has zero NuGet package references and the build produces zero warnings

---

### Story 1.3: Implement Process Execution and CLI Entry Point

As a developer using `dtk`,
I want the Spectre.Console CLI entry point wired up with dependency injection and process execution,
So that `dtk dotnet build` routes to the correct command handler and `dotnet build` is actually executed with all arguments forwarded.

**Acceptance Criteria:**

**Given** the CLI project is built and `dtk` is invocable
**When** `dtk dotnet build --configuration Release` is run
**Then** `dotnet build --configuration Release` is executed as a subprocess with all arguments forwarded verbatim
**And** the exit code returned by `dtk` exactly matches the exit code of the underlying `dotnet build` process
**And** `ProcessCommandRunner` implements `ICommandRunner` and reads stdout and stderr concurrently to prevent deadlock
**And** `TypeRegistrar` and `TypeResolver` in the Cli project bridge Spectre.Console with `Microsoft.Extensions.DependencyInjection`
**And** all Domain interfaces are registered with placeholder/stub Infrastructure implementations in DI so the app starts without runtime errors
**And** `DotnetCommandSettings` base class captures the `-v`/`--verbose` flag (levels 0–2) and remaining arguments
**And** running `dtk --help` displays the command tree without errors
**And** running `dtk dotnet --help` displays all registered dotnet subcommands

---

### Story 1.4: Implement Core Application Helpers and FilteredRunUseCase

As a developer,
I want the `FilteredRunUseCase` orchestrator and core helpers implemented,
So that any filter can be plugged in and the full run→filter→print→track pipeline works end to end.

**Acceptance Criteria:**

**Given** `FilteredRunUseCase` is invoked with a filter, command, and arguments
**When** the underlying command executes successfully
**Then** the use case runs the command via `ICommandRunner.RunCapturedAsync`, combines stdout and stderr, applies the filter, prints filtered output to console, and returns the process exit code
**And** if the filter throws an exception, the use case catches it, falls back to printing raw output, and still returns the correct exit code (fail-safe, FR16)
**And** at verbosity level 1, the command being executed is printed before running
**And** at verbosity level 2, the raw output is printed before the filtered output along with timing details
**And** `TokenEstimator.Estimate(string text)` returns `text.Length / 4` as an integer token count
**And** `AnsiStrip.Strip(string text)` removes all ANSI escape sequences using a `[GeneratedRegex]` pattern, returning clean text
**And** `TextHelpers` provides: `Truncate(string, int maxLen)` appending `...`; `FormatTokens(int)` producing human-readable counts (`1.2K`, `3.5M`); `ShortenPath(string absolutePath, string rootPath)` converting to forward-slash relative path
**And** all helpers are pure static methods with no I/O and are fully unit-tested in `DotnetTokenKiller.Application.Tests`

---

### Story 1.5: Implement dotnet build Filter with Tests

As a developer running `dtk dotnet build`,
I want the build output filtered to a compact, information-dense summary,
So that I save 80–90% of tokens compared to raw `dotnet build` output while retaining all actionable information.

**Acceptance Criteria:**

**Given** `DotnetBuildFilter` is applied to a clean success fixture (~20 lines of raw MSBuild output)
**When** `Apply(rawOutput)` is called
**Then** the output is a single line: `✓ dotnet build (N projects, X.XXs)`
**And** the token savings percentage is ≥85% compared to the raw fixture

**Given** `DotnetBuildFilter` is applied to a build-with-warnings fixture
**When** `Apply(rawOutput)` is called
**Then** the output starts with `dotnet build: 0 errors, N warnings (N projects, X.XXs)`
**And** warnings are grouped by diagnostic code (e.g., `CS0169 (2x)`) with shortened file paths and line numbers
**And** token savings is ≥75%

**Given** `DotnetBuildFilter` is applied to a build-failure fixture (~40 lines with duplicate error section)
**When** `Apply(rawOutput)` is called
**Then** the output starts with `dotnet build: N errors, M warnings`
**And** errors are grouped by file, sorted by error count descending, with shortened paths and line numbers
**And** duplicate errors (MSBuild prints errors twice) are deduplicated
**And** top diagnostic codes are listed (max 5)
**And** messages are truncated at 120 characters
**And** token savings is ≥70%

**Given** any noise lines are present in input (MSBuild version header, "Determining projects to restore...", "All projects are up-to-date for restore.", "Restored ... (in N ms).", "Build succeeded.", "Build FAILED.", "N Warning(s)", "N Error(s)", blank lines)
**When** `Apply(rawOutput)` is called
**Then** none of those lines appear in the output

**Given** an empty string or null is passed to `Apply`
**When** the method is called
**Then** a non-null string is returned without throwing an exception

**Given** output containing ANSI escape codes is passed to `Apply`
**When** the method is called
**Then** ANSI codes are stripped before parsing and do not appear in output

**And** all regex patterns in `DotnetBuildFilter` use `[GeneratedRegex]` attributes
**And** Verify.Xunit snapshot tests exist for the success, warnings, and failure scenarios
**And** real fixture files (`dotnet_build_success.txt`, `dotnet_build_errors.txt`) are included as embedded resources in `DotnetTokenKiller.Application.Tests/Fixtures/`
**And** `dotnet test DotnetTokenKiller.slnx` passes with all tests green

---

### Story 1.6: Set Up Minimal CI Quality Gate

As a maintainer of DotnetTokenKiller,
I want a basic GitHub Actions pipeline running on every push from day one,
So that build failures and test regressions are caught automatically throughout all development, not just at the end.

**Acceptance Criteria:**

**Given** a push or pull request is made to any branch
**When** the CI pipeline runs
**Then** it executes: (1) `dotnet build DotnetTokenKiller.slnx --no-restore -warnaserror`, (2) `dotnet test DotnetTokenKiller.slnx --no-build --logger trx`
**And** the pipeline fails if either step fails, blocking merge

**And** the workflow file exists at `.github/workflows/quality-gate.yml`
**And** the workflow uses `actions/setup-dotnet` with `dotnet-version: '10.0.x'` and runs on `ubuntu-latest`
**And** `dotnet format --verify-no-changes` is NOT yet included (deferred to Story 7.2 once codebase is stable)
**And** `dotnet test DotnetTokenKiller.slnx` passes locally before this story is merged

---

## Epic 2: Test Filter Intelligence

Users can run `dtk dotnet test` and see only failed tests with error messages and source locations, plus an aggregated pass/fail summary — eliminating the verbose test runner output that dominates LLM context windows.

### Story 2.1: Implement dotnet test Filter with Tests

As a developer running `dtk dotnet test`,
I want test output filtered to show only failures and a compact summary,
So that I save 90–95% of tokens on passing runs and immediately see what failed without scrolling through noise.

**Acceptance Criteria:**

**Given** `DotnetTestFilter` is applied to an all-pass fixture (~30 lines, single test project)
**When** `Apply(rawOutput)` is called
**Then** the output is a single line: `✓ dotnet test: N passed (1 project, X.XXs)`
**And** token savings is ≥90%

**Given** `DotnetTestFilter` is applied to an all-pass fixture with multiple test assemblies
**When** `Apply(rawOutput)` is called
**Then** the output aggregates across all assemblies: `✓ dotnet test: N passed (M projects, X.XXs)`

**Given** `DotnetTestFilter` is applied to a failure fixture with 2 failed tests
**When** `Apply(rawOutput)` is called
**Then** the output begins with `FAILURES (2):`
**And** each failure is listed with: test name, duration in ms, compacted error message (single line, max 200 chars), and source file + line number from the first stack frame containing a file reference
**And** the output ends with a summary line: `dotnet test: 2 failed, 40 passed (1 project, X.XXs)`
**And** token savings is ≥70%

**Given** more than 15 test failures are present
**When** `Apply(rawOutput)` is called
**Then** only the first 15 failures are shown followed by `+N more failures`

**Given** a multi-line xUnit `Assert.Equal` failure message (Expected/Actual on separate lines)
**When** `Apply(rawOutput)` is called
**Then** the message is compacted to a single line: `Expected: "X", Actual: "Y"`

**Given** a zero-tests-found scenario
**When** `Apply(rawOutput)` is called
**Then** the output is `✓ dotnet test: 0 tests found`

**Given** noise lines are present (test runner header, copyright notice, "Starting test execution...", "A total of N test files...", build preamble lines)
**When** `Apply(rawOutput)` is called
**Then** none of those lines appear in the output

**Given** an empty string or null is passed to `Apply`
**When** the method is called
**Then** a non-null string is returned without throwing an exception

**And** all regex patterns use `[GeneratedRegex]` attributes
**And** `DotnetTestCommand` in the Cli project wires `DotnetTestFilter` into `FilteredRunUseCase` via DI
**And** Verify.Xunit snapshot tests exist for the all-pass and failure scenarios
**And** real fixture files (`dotnet_test_all_pass.txt`, `dotnet_test_failures.txt`) are included as embedded resources in `DotnetTokenKiller.Application.Tests/Fixtures/`
**And** `dotnet test DotnetTokenKiller.slnx` passes with all tests green

---

## Epic 3: Restore, Publish & Pack Filters

Users can run `dtk dotnet restore`, `dtk dotnet publish`, and `dtk dotnet pack` and receive one-line summaries instead of verbose MSBuild output, with meaningful output paths shown for publish and pack operations.

### Story 3.1: Implement dotnet restore Filter with Tests

As a developer running `dtk dotnet restore`,
I want restore output filtered to a compact one-line summary,
So that I save 90–95% of tokens while still seeing the project count, timing, and any NuGet errors.

**Acceptance Criteria:**

**Given** `DotnetRestoreFilter` is applied to a successful restore fixture
**When** `Apply(rawOutput)` is called
**Then** the output is a single line: `✓ dotnet restore (N projects, X.XXs)`
**And** the project count is derived from `Restored ...` lines plus any up-to-date count from "N of M projects are up-to-date for restore"
**And** token savings is ≥90%

**Given** `DotnetRestoreFilter` is applied to a failure fixture containing a NU-prefixed error
**When** `Apply(rawOutput)` is called
**Then** the output begins with `dotnet restore: N error(s)`
**And** each NuGet error is shown with its code and message (e.g., `NU1101: Unable to find package 'X'`)
**And** the affected project path is shown shortened to project-relative

**Given** noise lines are present ("Determining projects to restore...", "Writing assets file to disk.", "NuGet Config files used", individual package download progress lines)
**When** `Apply(rawOutput)` is called
**Then** none of those lines appear in the output

**Given** an empty string or null is passed to `Apply`
**When** the method is called
**Then** a non-null string is returned without throwing an exception

**And** all regex patterns use `[GeneratedRegex]` attributes
**And** `DotnetRestoreCommand` wires `DotnetRestoreFilter` into `FilteredRunUseCase`
**And** a Verify.Xunit snapshot test exists for the success scenario
**And** a real fixture file (`dotnet_restore_raw.txt`) is included as an embedded resource
**And** `dotnet test DotnetTokenKiller.slnx` passes with all tests green

---

### Story 3.2: Implement dotnet publish Filter with Tests

As a developer running `dtk dotnet publish`,
I want publish output filtered to show only the output path and any errors,
So that I save 80–85% of tokens and immediately know where the published output landed.

**Acceptance Criteria:**

**Given** `DotnetPublishFilter` is applied to a successful publish fixture
**When** `Apply(rawOutput)` is called
**Then** the output is a single line: `✓ dotnet publish → bin/Release/net10.0/publish/ (N project, X.XXs)`
**And** the publish path is extracted from the last `→` line containing "publish" and shortened to project-relative using forward slashes
**And** token savings is ≥80%

**Given** `DotnetPublishFilter` is applied to a failure fixture containing build errors
**When** `Apply(rawOutput)` is called
**Then** the output uses the same diagnostic grouping format as `DotnetBuildFilter` (errors grouped by file, top codes listed)

**Given** noise lines are present (MSBuild version header, restore progress, compile output lines)
**When** `Apply(rawOutput)` is called
**Then** none of those lines appear in the output

**Given** an empty string or null is passed to `Apply`
**When** the method is called
**Then** a non-null string is returned without throwing an exception

**And** all regex patterns use `[GeneratedRegex]` attributes
**And** `DotnetPublishCommand` wires `DotnetPublishFilter` into `FilteredRunUseCase`
**And** a Verify.Xunit snapshot test exists for the success scenario
**And** a real fixture file (`dotnet_publish_raw.txt`) is included as an embedded resource
**And** `dotnet test DotnetTokenKiller.slnx` passes with all tests green

---

### Story 3.3: Implement dotnet pack Filter with Tests

As a developer running `dtk dotnet pack`,
I want pack output filtered to show only the generated .nupkg filename,
So that I save 85–90% of tokens and immediately know what package was produced.

**Acceptance Criteria:**

**Given** `DotnetPackFilter` is applied to a successful pack fixture
**When** `Apply(rawOutput)` is called
**Then** the output is a single line: `✓ dotnet pack → MyProject.1.0.0.nupkg (N project, X.XXs)`
**And** the `.nupkg` filename is extracted from the "Successfully created package" line
**And** token savings is ≥85%

**Given** `DotnetPackFilter` is applied to a failure fixture containing build errors
**When** `Apply(rawOutput)` is called
**Then** the output uses the same diagnostic grouping format as `DotnetBuildFilter`

**Given** noise lines are present (MSBuild version header, restore progress, compile output lines)
**When** `Apply(rawOutput)` is called
**Then** none of those lines appear in the output

**Given** an empty string or null is passed to `Apply`
**When** the method is called
**Then** a non-null string is returned without throwing an exception

**And** all regex patterns use `[GeneratedRegex]` attributes
**And** `DotnetPackCommand` wires `DotnetPackFilter` into `FilteredRunUseCase`
**And** a Verify.Xunit snapshot test exists for the success scenario
**And** a real fixture file (`dotnet_pack_raw.txt`) is included as an embedded resource
**And** `dotnet test DotnetTokenKiller.slnx` passes with all tests green

---

## Epic 4: Remaining Filters & Passthrough Coverage

Every `dotnet` subcommand works with DTK — `clean`, `run`, `ef`, `format`, and `nuget` all produce compact output, and any unrecognized subcommand silently passes through with exit code preserved. The filter suite is now complete.

### Story 4.1: Implement dotnet clean Filter with Tests

As a developer running `dtk dotnet clean`,
I want clean output reduced to a single success marker,
So that I save 95%+ of tokens from the verbose MSBuild clean output.

**Acceptance Criteria:**

**Given** `DotnetCleanFilter` is applied to a successful clean fixture
**When** `Apply(rawOutput)` is called
**Then** the output is `✓ dotnet clean`
**And** token savings is ≥95%

**Given** `DotnetCleanFilter` is applied to a fixture containing error lines (case-insensitive match on "error")
**When** `Apply(rawOutput)` is called
**Then** up to 5 error lines are shown in the output

**Given** an empty string or null is passed to `Apply`
**When** the method is called
**Then** a non-null string is returned without throwing an exception

**And** `DotnetCleanCommand` wires `DotnetCleanFilter` into `FilteredRunUseCase`
**And** a real fixture file (`dotnet_clean_raw.txt`) is included as an embedded resource
**And** `dotnet test DotnetTokenKiller.slnx` passes with all tests green

---

### Story 4.2: Implement dotnet run Filter with Tests

As a developer running `dtk dotnet run`,
I want the build preamble stripped from run output while the application's actual output is preserved exactly,
So that I save 60–80% of tokens without losing any of my application's stdout/stderr.

**Acceptance Criteria:**

**Given** `DotnetRunFilter` is applied to a fixture containing build preamble followed by application output
**When** `Apply(rawOutput)` is called
**Then** all known build preamble lines are stripped: "Determining projects to restore...", "All projects are up-to-date for restore.", "Restored ...", "MSBuild version ...", "Build started ...", and project output lines matching `→ .dll`
**And** all remaining lines (the application's actual output) are preserved unchanged and in order
**And** token savings is ≥60%

**Given** `DotnetRunFilter` is applied to a fixture where stripping preamble leaves no remaining lines
**When** `Apply(rawOutput)` is called
**Then** the output is `✓ dotnet run completed`

**Given** an empty string or null is passed to `Apply`
**When** the method is called
**Then** a non-null string is returned without throwing an exception

**And** `DotnetRunCommand` wires `DotnetRunFilter` into `FilteredRunUseCase`
**And** a real fixture file (`dotnet_run_raw.txt`) is included as an embedded resource
**And** `dotnet test DotnetTokenKiller.slnx` passes with all tests green

---

### Story 4.3: Implement dotnet ef Filter with Tests

As a developer running `dtk dotnet ef` commands,
I want Entity Framework CLI output compacted to the actionable result,
So that I save 70–80% of tokens by eliminating EF banners, build output, and verbose SQL logs.

**Acceptance Criteria:**

**Given** `DotnetEfFilter` is applied to a `ef migrations list` fixture
**When** `Apply(rawOutput)` is called
**Then** the output is compact: `N migrations (latest: MigrationName)`

**Given** `DotnetEfFilter` is applied to a `ef migrations add` fixture
**When** `Apply(rawOutput)` is called
**Then** the output is: `✓ migration added: MigrationName`

**Given** `DotnetEfFilter` is applied to a `ef database update` fixture
**When** `Apply(rawOutput)` is called
**Then** the output is: `✓ database updated (N migrations applied)`

**Given** noise lines are present (EF CLI banner/logo lines, "Build started", "Build succeeded", verbose SQL execution logs)
**When** `Apply(rawOutput)` is called
**Then** none of those lines appear in the output

**Given** an empty string or null is passed to `Apply`
**When** the method is called
**Then** a non-null string is returned without throwing an exception

**And** all regex patterns use `[GeneratedRegex]` attributes
**And** `DotnetEfCommand` wires `DotnetEfFilter` into `FilteredRunUseCase`
**And** a real fixture file (`dotnet_ef_raw.txt`) is included as an embedded resource
**And** `dotnet test DotnetTokenKiller.slnx` passes with all tests green

---

### Story 4.4: Implement dotnet format Filter with Tests

As a developer running `dtk dotnet format`,
I want format output showing only the count and list of changed files (or files needing changes),
So that I save 70–80% of tokens while knowing exactly what was formatted or needs formatting.

**Acceptance Criteria:**

**Given** `DotnetFormatFilter` is applied to a fix-mode fixture (files were formatted)
**When** `Apply(rawOutput)` is called
**Then** the output is: `✓ dotnet format (N files, X.XXs)`
**And** token savings is ≥70%

**Given** `DotnetFormatFilter` is applied to a check-mode fixture (`--verify-no-changes`) with files needing formatting
**When** `Apply(rawOutput)` is called
**Then** the output begins with `dotnet format: N files need formatting`
**And** each file is listed on its own line, shortened to project-relative path
**And** if more than 20 files need formatting, only 20 are shown followed by `+N more`

**Given** `DotnetFormatFilter` is applied to a check-mode fixture with no files needing changes
**When** `Apply(rawOutput)` is called
**Then** the output is `✓ dotnet format (no changes)`

**Given** an empty string or null is passed to `Apply`
**When** the method is called
**Then** a non-null string is returned without throwing an exception

**And** all regex patterns use `[GeneratedRegex]` attributes
**And** `DotnetFormatCommand` wires `DotnetFormatFilter` into `FilteredRunUseCase`
**And** a real fixture file (`dotnet_format_raw.txt`) is included as an embedded resource
**And** `dotnet test DotnetTokenKiller.slnx` passes with all tests green

---

### Story 4.5: Implement dotnet nuget Filter with Tests

As a developer running `dtk dotnet nuget` commands,
I want NuGet output stripped of progress bars and verbose download indicators,
So that I save 75–85% of tokens while seeing the operation result clearly.

**Acceptance Criteria:**

**Given** `DotnetNugetFilter` is applied to a `nuget push` fixture
**When** `Apply(rawOutput)` is called
**Then** the output is `✓ nuget push succeeded` when the phrase "Your package was pushed" is detected

**Given** `DotnetNugetFilter` is applied to a `nuget locals all --clear` fixture
**When** `Apply(rawOutput)` is called
**Then** the output is `✓ nuget locals cleared` when "local resources have been cleared" is detected

**Given** `DotnetNugetFilter` is applied to an unrecognized nuget subcommand fixture
**When** `Apply(rawOutput)` is called
**Then** progress bars and download indicators are stripped and the remaining output is returned

**Given** an empty string or null is passed to `Apply`
**When** the method is called
**Then** a non-null string is returned without throwing an exception

**And** all regex patterns use `[GeneratedRegex]` attributes
**And** `DotnetNugetCommand` wires `DotnetNugetFilter` into `FilteredRunUseCase`
**And** a real fixture file (`dotnet_nuget_raw.txt`) is included as an embedded resource
**And** `dotnet test DotnetTokenKiller.slnx` passes with all tests green

---

### Story 4.6: Implement Passthrough for Unrecognized Subcommands

As a developer running any dotnet subcommand not explicitly handled by DTK (e.g., `dtk dotnet new`, `dtk dotnet watch`),
I want the command to run transparently with no filtering applied,
So that DTK never blocks or degrades commands it does not understand.

**Acceptance Criteria:**

**Given** `dtk dotnet new console -n MyApp` is run
**When** the command is executed
**Then** `dotnet new console -n MyApp` runs in passthrough mode (inherits stdin, stdout, and stderr directly)
**And** the exit code returned by `dtk` exactly matches the underlying `dotnet` process exit code
**And** no filtering is applied — output goes directly to the terminal unchanged

**Given** any unrecognized subcommand is run with `-v`
**When** the command is executed
**Then** the command still runs in passthrough mode (verbosity does not alter passthrough behavior)

**And** `PassthroughRunUseCase` implements the passthrough via `ICommandRunner.RunPassthroughAsync`
**And** the Cli project's default/fallback command for unrecognized dotnet subcommands routes to `PassthroughRunUseCase`
**And** after the passthrough command completes, `ITracker.RecordAsync` is called with: the subcommand name, current working directory as project path, 0 input tokens, 0 output tokens, 0 saved tokens, 0% savings, and wall-clock execution time (passthrough commands are tracked with no savings data)
**And** if `ITracker.RecordAsync` throws, the error is caught silently and does not affect the passthrough behavior or exit code
**And** `dotnet test DotnetTokenKiller.slnx` passes with all tests green

---

## Epic 5: Token Savings Analytics

Users can run `dtk gain` and see a rich Spectre.Console table showing cumulative token savings across all commands — total tokens saved, savings percentages, per-command breakdown — with filtering by days, project, or JSON output.

### Story 5.1: Implement SQLite Token Tracking

As a developer,
I want every DTK command execution recorded to a SQLite database with token counts and savings data,
So that savings analytics are available for reporting without any impact on command output if tracking fails.

**Acceptance Criteria:**

**Given** `SqliteTracker` is used for the first time on a machine
**When** `RecordAsync` is called
**Then** the database file is auto-created at the platform-appropriate path: `%LOCALAPPDATA%/dtk/tracking.db` (Windows) or `~/.local/share/dtk/tracking.db` (Linux/macOS)
**And** the `commands` table is auto-created with columns for timestamp, command, project path, input tokens, output tokens, saved tokens, savings percentage, and execution time
**And** the record is persisted correctly and retrievable via `GetHistoryAsync`

**Given** `RecordAsync` is called
**When** any tracking error occurs (database locked, disk full, permission denied)
**Then** the error is swallowed silently — no exception propagates to the caller

**Given** records older than 90 days exist in the database
**When** `RecordAsync` is called
**Then** `CleanupAsync` is invoked automatically and records older than 90 days are deleted

**Given** `GetSummaryAsync` is called with a date range
**When** matching records exist
**Then** a `GainSummary` is returned with: total commands, total tokens saved, average savings percentage, time range, and per-command-type breakdown

**And** `SqliteTracker` is registered in DI replacing the stub from Story 1.3
**And** Infrastructure tests use in-memory SQLite (`Data Source=:memory:`) for isolation
**And** `DTK_DB_PATH` environment variable overrides the default database path when set
**And** `dotnet test DotnetTokenKiller.slnx` passes with all tests green

---

### Story 5.2: Wire Token Tracking into FilteredRunUseCase

As a developer,
I want every filtered command execution automatically tracked with accurate token counts,
So that savings data accumulates passively without any extra steps.

> **Dependency note:** This story modifies `FilteredRunUseCase` from Story 1.4 by injecting `ITracker` and calling `RecordAsync` after each run. Filter stories in Epics 2–4 are built against the un-tracked version; tracking activates transparently once this story is complete.

**Acceptance Criteria:**

**Given** `FilteredRunUseCase` completes a command execution
**When** the use case finishes (success or failure)
**Then** `ITracker.RecordAsync` is called with: the command name, current working directory as project path, estimated input token count (raw output via `TokenEstimator`), estimated output token count (filtered output), calculated savings tokens and percentage, and wall-clock execution time
**And** ANSI codes are stripped from both raw and filtered output before token estimation

**Given** `ITracker.RecordAsync` throws any exception
**When** the use case handles tracking
**Then** the exception is caught silently and does not affect the filtered output or exit code returned to the caller

**And** Application-layer use case tests mock `ITracker` with NSubstitute and verify `RecordAsync` is called with correct parameters after successful execution
**And** `dotnet test DotnetTokenKiller.slnx` passes with all tests green

---

### Story 5.3: Implement dtk gain Analytics Command

As a developer,
I want to run `dtk gain` and see a formatted table of my token savings across all commands,
So that I can understand the value DTK is delivering and share it with my team.

**Acceptance Criteria:**

**Given** `dtk gain` is run with recorded tracking data
**When** the command executes
**Then** a Spectre.Console table is rendered showing: command name, run count, total tokens saved, average savings percentage — one row per command type
**And** a summary line shows total tokens saved and overall average savings percentage across all commands
**And** the default time range is the last 30 days

**Given** `dtk gain --days 7` is run
**When** the command executes
**Then** only records from the last 7 days are included in the table

**Given** `dtk gain --project` is run from a project directory
**When** the command executes
**Then** only records matching the current working directory as project path are included

**Given** `dtk gain --json` is run
**When** the command executes
**Then** raw JSON is output to stdout (suitable for LLM consumption or piping) instead of the Spectre.Console table

**Given** no tracking records exist
**When** `dtk gain` is run
**Then** a friendly message is displayed: "No data yet. Run some `dtk dotnet` commands to start tracking savings."

**And** `GainReportUseCase` implements the analytics query using `ITracker.GetSummaryAsync`
**And** `GainCommand` and `GainCommandSettings` exist in the Cli project with `--days`, `--project`, and `--json` options
**And** `dotnet test DotnetTokenKiller.slnx` passes with all tests green

---

## Epic 6: Configuration & Tee Recovery

Users can customize DTK's behavior via a JSON config file (tracking retention, display options, tee behavior), and when a command fails they automatically get the full raw output saved to a file with a one-line hint — so no output is ever lost.

> **Post-MVP Backlog:** A `dtk config set <key> <value>` CLI command for managing settings without hand-editing JSON is explicitly out of scope for this epic. Users edit `config.json` directly for MVP.

### Story 6.1: Implement JSON Configuration

As a developer,
I want DTK to load and save user configuration from a JSON file with sensible defaults,
So that I can customize tracking, display, and tee behavior without any code changes.

**Acceptance Criteria:**

**Given** no config file exists on the machine
**When** `JsonConfigProvider.LoadAsync` is called
**Then** a `DtkConfig` instance is returned with all defaults: tracking enabled, 90-day history, colors enabled, emoji enabled, tee mode "failures", max 20 tee files, max 1 MB tee file size

**Given** a config file exists with partial overrides
**When** `JsonConfigProvider.LoadAsync` is called
**Then** the returned `DtkConfig` reflects the overrides with remaining fields at their defaults

**Given** `JsonConfigProvider.SaveAsync` is called with a modified `DtkConfig`
**When** the method completes
**Then** the config file is written to the platform-appropriate path: `%APPDATA%/dtk/config.json` (Windows) or `~/.config/dtk/config.json` (Linux/macOS)
**And** the directory is auto-created if it does not exist
**And** a subsequent `LoadAsync` call returns a `DtkConfig` equal to what was saved (round-trip fidelity)

**Given** the config file contains invalid JSON
**When** `JsonConfigProvider.LoadAsync` is called
**Then** the error is handled gracefully and default config is returned without throwing

**And** `System.Text.Json` source-generated serialization context is used (AOT-compatible, no reflection)
**And** `JsonConfigProvider` is registered in DI replacing the stub from Story 1.3
**And** Infrastructure tests use a temp directory so tests do not affect real config
**And** `dotnet test DotnetTokenKiller.slnx` passes with all tests green

---

### Story 6.2: Implement Tee Output Recovery

As a developer whose command failed,
I want the full raw output automatically saved to a file with a hint appended to the filtered output,
So that I can read the complete output without re-running the command.

**Acceptance Criteria:**

**Given** tee mode is "failures" (default) and a command exits with a non-zero exit code and raw output is ≥500 characters
**When** `FileTeeService.TeeAndHintAsync` is called
**Then** the raw output is saved to a timestamped file: `{tee_dir}/{unix_timestamp}_{command_slug}.log`
**And** the method returns a hint string: `[full output: /path/to/file.log]`
**And** the hint is appended to the filtered output printed by `FilteredRunUseCase`

**Given** tee mode is "failures" and a command exits with exit code 0
**When** `TeeAndHintAsync` is called
**Then** no file is written and `null` is returned

**Given** tee mode is "never"
**When** `TeeAndHintAsync` is called regardless of exit code
**Then** no file is written and `null` is returned

**Given** tee mode is "always" and a command exits with exit code 0
**When** `TeeAndHintAsync` is called
**Then** the raw output is saved to a file and the hint is returned

**Given** raw output is less than 500 characters
**When** `TeeAndHintAsync` is called
**Then** no file is written (output too small to be worth saving)

**Given** the tee directory already contains `maxFiles` (default 20) files
**When** a new file is written
**Then** the oldest file(s) are deleted first to stay within the limit

**Given** the raw output exceeds `maxFileSize` (default 1 MB)
**When** the file is written
**Then** the content is truncated to `maxFileSize`

**Given** any tee I/O error occurs (permission denied, disk full)
**When** `TeeAndHintAsync` is called
**Then** the error is swallowed silently and `null` is returned — the command output is unaffected

**And** command names are sanitized for safe filenames before use in the file path
**And** the tee directory defaults to `%LOCALAPPDATA%/dtk/tee/` (Windows) or `~/.local/share/dtk/tee/` (Linux/macOS) and is overridable via `DTK_TEE_DIR` env var or config
**And** `FileTeeService` is registered in DI replacing the stub from Story 1.3
**And** Infrastructure tests use a temp directory for file isolation
**And** `dotnet test DotnetTokenKiller.slnx` passes with all tests green

---

## Epic 7: Distribution as .NET Global Tool

Any .NET developer can install DTK with `dotnet tool install -g DotnetTokenKiller`. The tool is correctly packaged as a NuGet global tool with metadata, versioning, and a CI/CD quality gate pipeline.

### Story 7.1: Package and Validate as .NET Global Tool

As a .NET developer,
I want to install DTK with a single `dotnet tool install` command,
So that I can immediately use `dtk` in any terminal without manual binary placement.

**Acceptance Criteria:**

**Given** the CLI project is configured with `PackAsTool=true`, `ToolCommandName=dtk`, `PackageId=DotnetTokenKiller`
**When** `dotnet pack src/DotnetTokenKiller.Cli -c Release -o ./nupkg` is run
**Then** a `.nupkg` file is produced in `./nupkg/` without errors

**Given** the `.nupkg` exists in `./nupkg/`
**When** `dotnet tool install -g --add-source ./nupkg DotnetTokenKiller` is run
**Then** the tool installs successfully and `dtk --version` outputs the package version

**Given** `dtk` is installed
**When** `dtk dotnet build` is run in a .NET project directory
**Then** the tool executes end-to-end: runs `dotnet build`, filters output, and returns the correct exit code

**And** `DotnetTokenKiller.Cli.csproj` includes NuGet package metadata: `Description`, `PackageTags`, `PackageLicenseExpression=MIT`, `PackageReadmeFile=README.md`
**And** `README.md` exists at the repository root with installation and usage instructions
**And** `dotnet test DotnetTokenKiller.slnx` passes with all tests green before packaging

---

### Story 7.2: Expand CI/CD Quality Gate Pipeline to Full Quality Enforcement

As a maintainer of DotnetTokenKiller,
I want the existing GitHub Actions pipeline (from Story 1.6) expanded to enforce full code quality including formatting,
So that no PR can merge with formatting violations, build warnings, or failing tests.

> **Note:** Story 1.1 configures `PackAsTool=true`, `ToolCommandName=dtk`, and `PackageId=DotnetTokenKiller` in the `.csproj` so that `dtk --help` works during development. This story validates the same config produces a correct NuGet package end-to-end — the duplication is intentional.

**Acceptance Criteria:**

**Given** a pull request is opened or a push is made to any branch
**When** the quality gate pipeline runs
**Then** it executes these steps in order: (1) `dotnet format DotnetTokenKiller.slnx --no-restore --verify-no-changes`, (2) `dotnet build DotnetTokenKiller.slnx --no-restore -warnaserror`, (3) `dotnet test DotnetTokenKiller.slnx --no-build --logger trx`
**And** the pipeline fails if any step fails, blocking merge
**And** the `dotnet format` step is added to the existing `quality-gate.yml` from Story 1.6 (not a new file)

**Given** the pipeline runs step (3)
**When** tests complete
**Then** TRX test results are uploaded as a pipeline artifact

**And** the workflow file exists at `.github/workflows/quality-gate.yml`
**And** the workflow uses `dotnet-version: '10.0.x'` for SDK setup
**And** the workflow runs on `ubuntu-latest`
**And** `dotnet test DotnetTokenKiller.slnx` passes locally before the pipeline is pushed
