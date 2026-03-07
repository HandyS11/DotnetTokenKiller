# Story 1.2: Implement Core Domain Contracts and Value Objects

Status: review

## Story

As a developer,
I want all Domain layer interfaces and value objects defined,
so that Application and Infrastructure layers have stable contracts to implement against without any I/O dependencies.

## Acceptance Criteria

1. `IOutputFilter` interface exists in `DotnetTokenKiller.Domain.Filters` with a single `string Apply(string rawOutput)` method
2. `ICommandRunner` interface exists in `DotnetTokenKiller.Domain.Execution` with `RunCapturedAsync` and `RunPassthroughAsync` methods
3. `CommandResult` record exists in `DotnetTokenKiller.Domain.Execution` with `StdOut`, `StdErr`, and `ExitCode` properties
4. `ITracker` interface exists in `DotnetTokenKiller.Domain.Tracking` with `RecordAsync`, `GetSummaryAsync`, `GetHistoryAsync`, and `CleanupAsync` methods
5. `CommandRecord` entity exists in `DotnetTokenKiller.Domain.Tracking` with all tracking fields (timestamp, command, project path, input/output tokens, saved tokens, savings percentage, execution time)
6. `GainSummary` entity exists in `DotnetTokenKiller.Domain.Tracking` with aggregated analytics fields
7. `IConfigProvider` interface exists in `DotnetTokenKiller.Domain.Configuration` with `LoadAsync` and `SaveAsync` methods
8. `DtkConfig` value object hierarchy exists (`TrackingConfig`, `DisplayConfig`, `TeeConfig`) with sensible defaults
9. `ITeeService` interface exists in `DotnetTokenKiller.Domain.Tee` with a `TeeAndHintAsync` method
10. The Domain project has zero NuGet package references and the build produces zero warnings

## Tasks / Subtasks

- [x] Task 1: Create `src/DotnetTokenKiller.Domain/Filters/IOutputFilter.cs` (AC: #1)
  - [x] File-scoped namespace `DotnetTokenKiller.Domain.Filters`
  - [x] Single method: `string Apply(string rawOutput)`

- [x] Task 2: Create `src/DotnetTokenKiller.Domain/Execution/CommandResult.cs` (AC: #3)
  - [x] File-scoped namespace `DotnetTokenKiller.Domain.Execution`
  - [x] Positional `record CommandResult(string StdOut, string StdErr, int ExitCode)`

- [x] Task 3: Create `src/DotnetTokenKiller.Domain/Execution/ICommandRunner.cs` (AC: #2)
  - [x] File-scoped namespace `DotnetTokenKiller.Domain.Execution`
  - [x] `Task<CommandResult> RunCapturedAsync(string command, IReadOnlyList<string> args, CancellationToken cancellationToken = default)`
  - [x] `Task<int> RunPassthroughAsync(string command, IReadOnlyList<string> args, CancellationToken cancellationToken = default)`

- [x] Task 4: Create `src/DotnetTokenKiller.Domain/Tracking/CommandRecord.cs` (AC: #5)
  - [x] File-scoped namespace `DotnetTokenKiller.Domain.Tracking`
  - [x] All 8 tracking fields: Timestamp, Command, ProjectPath, InputTokens, OutputTokens, SavedTokens, SavingsPercentage, ExecutionTime

- [x] Task 5: Create `src/DotnetTokenKiller.Domain/Tracking/GainSummary.cs` (AC: #6)
  - [x] File-scoped namespace `DotnetTokenKiller.Domain.Tracking`
  - [x] Aggregated analytics: TotalCommands, TotalInputTokens, TotalOutputTokens, TotalSavedTokens, AverageSavingsPercentage, SavedByCommand

- [x] Task 6: Create `src/DotnetTokenKiller.Domain/Tracking/ITracker.cs` (AC: #4)
  - [x] File-scoped namespace `DotnetTokenKiller.Domain.Tracking`
  - [x] `Task RecordAsync(CommandRecord record, CancellationToken cancellationToken = default)`
  - [x] `Task<GainSummary> GetSummaryAsync(int days, string? projectPath, CancellationToken cancellationToken = default)`
  - [x] `Task<IReadOnlyList<CommandRecord>> GetHistoryAsync(int days, string? projectPath, CancellationToken cancellationToken = default)`
  - [x] `Task CleanupAsync(int retentionDays, CancellationToken cancellationToken = default)`

- [x] Task 7: Create `src/DotnetTokenKiller.Domain/Configuration/DtkConfig.cs` (AC: #8)
  - [x] File-scoped namespace `DotnetTokenKiller.Domain.Configuration`
  - [x] `sealed record DtkConfig` with `TrackingConfig`, `DisplayConfig`, `TeeConfig` properties and `static DtkConfig Default`
  - [x] `sealed record TrackingConfig` with defaults: `Enabled = true`, `RetentionDays = 90`, `DbPath = null`
  - [x] `sealed record DisplayConfig` with defaults: `Colors = true`, `Emoji = true`, `Width = 120`
  - [x] `sealed record TeeConfig` with defaults: `Mode = "failures"`, `Directory = null`, `MaxFiles = 20`, `MaxFileSizeBytes = 1_048_576L`

- [x] Task 8: Create `src/DotnetTokenKiller.Domain/Configuration/IConfigProvider.cs` (AC: #7)
  - [x] File-scoped namespace `DotnetTokenKiller.Domain.Configuration`
  - [x] `Task<DtkConfig> LoadAsync(CancellationToken cancellationToken = default)`
  - [x] `Task SaveAsync(DtkConfig config, CancellationToken cancellationToken = default)`

- [x] Task 9: Create `src/DotnetTokenKiller.Domain/Tee/ITeeService.cs` (AC: #9)
  - [x] File-scoped namespace `DotnetTokenKiller.Domain.Tee`
  - [x] `Task<string?> TeeAndHintAsync(string rawOutput, string commandSlug, int exitCode, CancellationToken cancellationToken = default)`
  - [x] Returns hint string like `[full output: /path/to/file.log]` when tee'd; `null` if not tee'd

- [x] Task 10: Write unit tests in `tests/DotnetTokenKiller.Domain.Tests/` (AC: #10)
  - [x] `CommandResultTests` — record equality, positional construction, ExitCode=0 for success
  - [x] `DtkConfigTests` — `DtkConfig.Default` has all expected defaults; records are immutable value objects
  - [x] `CommandRecordTests` — all fields can be set and read back correctly
  - [x] `GainSummaryTests` — all fields present

- [x] Task 11: Build verification (AC: #10)
  - [x] `dotnet build DotnetTokenKiller.slnx` → 0 errors, 0 warnings
  - [x] `dotnet test DotnetTokenKiller.slnx` → all tests pass (17/17)
  - [x] `dotnet format DotnetTokenKiller.slnx --no-restore --verify-no-changes` → no violations

## Dev Notes

### Critical: Current Repository State

Story 1.1 is **done**. The following already exist and must NOT be recreated or modified:

| File | State |
|---|---|
| `DotnetTokenKiller.slnx` | Complete — all 8 projects registered |
| `Directory.Build.props` | Complete — `net10.0`, `LangVersion=14`, `TreatWarningsAsErrors=true`, `AnalysisLevel=latest-all`, NoWarn configured |
| `src/DotnetTokenKiller.Domain/DotnetTokenKiller.Domain.csproj` | Exists — zero deps, zero project refs |
| `src/DotnetTokenKiller.Cli/Program.cs` | Exists — minimal `await app.RunAsync(args)` |
| All 4 test `.csproj` files | Exist — xunit, FluentAssertions, coverlet wired |

**This story adds only new `.cs` files inside `src/DotnetTokenKiller.Domain/` and test files in `tests/DotnetTokenKiller.Domain.Tests/`. Do NOT modify any `.csproj` or `.props` files — Domain has zero NuGet references by design.**

### Exact File Structure to Create

```sh
src/DotnetTokenKiller.Domain/
  Filters/
    IOutputFilter.cs
  Execution/
    CommandResult.cs
    ICommandRunner.cs
  Tracking/
    CommandRecord.cs
    GainSummary.cs
    ITracker.cs
  Configuration/
    DtkConfig.cs
    IConfigProvider.cs
  Tee/
    ITeeService.cs

tests/DotnetTokenKiller.Domain.Tests/
  CommandResultTests.cs
  DtkConfigTests.cs
  CommandRecordTests.cs
  GainSummaryTests.cs
```

### Precise Interface and Type Signatures

Use these **exactly** — these contracts are stable and all subsequent stories (1.3–1.6, all infrastructure) depend on them.

#### `IOutputFilter.cs`

```csharp
namespace DotnetTokenKiller.Domain.Filters;

public interface IOutputFilter
{
    string Apply(string rawOutput);
}
```

#### `CommandResult.cs`

```csharp
namespace DotnetTokenKiller.Domain.Execution;

public record CommandResult(string StdOut, string StdErr, int ExitCode);
```

#### `ICommandRunner.cs`

```csharp
namespace DotnetTokenKiller.Domain.Execution;

public interface ICommandRunner
{
    Task<CommandResult> RunCapturedAsync(
        string command,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default);

    Task<int> RunPassthroughAsync(
        string command,
        IReadOnlyList<string> args,
        CancellationToken cancellationToken = default);
}
```

#### `CommandRecord.cs`

```csharp
namespace DotnetTokenKiller.Domain.Tracking;

public sealed record CommandRecord(
    DateTimeOffset Timestamp,
    string Command,
    string ProjectPath,
    int InputTokens,
    int OutputTokens,
    int SavedTokens,
    double SavingsPercentage,
    TimeSpan ExecutionTime);
```

#### `GainSummary.cs`

```csharp
namespace DotnetTokenKiller.Domain.Tracking;

public sealed record GainSummary(
    int TotalCommands,
    int TotalInputTokens,
    int TotalOutputTokens,
    int TotalSavedTokens,
    double AverageSavingsPercentage,
    IReadOnlyDictionary<string, int> SavedByCommand);
```

`SavedByCommand` maps command slug (e.g., `"build"`, `"test"`) → tokens saved. Used for per-command breakdown in `dtk gain` table.

#### `ITracker.cs`

```csharp
namespace DotnetTokenKiller.Domain.Tracking;

public interface ITracker
{
    Task RecordAsync(CommandRecord record, CancellationToken cancellationToken = default);

    Task<GainSummary> GetSummaryAsync(
        int days,
        string? projectPath,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<CommandRecord>> GetHistoryAsync(
        int days,
        string? projectPath,
        CancellationToken cancellationToken = default);

    Task CleanupAsync(int retentionDays, CancellationToken cancellationToken = default);
}
```

#### `DtkConfig.cs`

All three nested records go in one file to keep the config hierarchy co-located:

```csharp
namespace DotnetTokenKiller.Domain.Configuration;

public sealed record DtkConfig(
    TrackingConfig Tracking,
    DisplayConfig Display,
    TeeConfig Tee)
{
    public static DtkConfig Default => new(
        new TrackingConfig(),
        new DisplayConfig(),
        new TeeConfig());
}

public sealed record TrackingConfig(
    bool Enabled = true,
    int RetentionDays = 90,
    string? DbPath = null);

public sealed record DisplayConfig(
    bool Colors = true,
    bool Emoji = true,
    int Width = 120);

public sealed record TeeConfig(
    string Mode = "failures",
    string? Directory = null,
    int MaxFiles = 20,
    long MaxFileSizeBytes = 1_048_576L);
```

Valid `TeeConfig.Mode` values: `"never"`, `"failures"` (default), `"always"`. The Infrastructure layer validates this at runtime. Do NOT add an enum here — keep Domain dependency-free and simple.

#### `IConfigProvider.cs`

```csharp
namespace DotnetTokenKiller.Domain.Configuration;

public interface IConfigProvider
{
    Task<DtkConfig> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(DtkConfig config, CancellationToken cancellationToken = default);
}
```

#### `ITeeService.cs`

```csharp
namespace DotnetTokenKiller.Domain.Tee;

public interface ITeeService
{
    Task<string?> TeeAndHintAsync(
        string rawOutput,
        string commandSlug,
        int exitCode,
        CancellationToken cancellationToken = default);
}
```

Returns `null` when tee is disabled or not triggered (e.g., `Mode = "never"`, or `exitCode == 0` with `Mode = "failures"`). Returns a hint string like `[full output: /home/user/.local/share/dtk/tee/1234567890_build.log]` when raw output is saved.

### Architecture Compliance Rules (CRITICAL)

- **Domain MUST have zero NuGet package references** — these types are pure C#; `System.Threading.Tasks`, `System.Collections.Generic`, `System.DateTimeOffset` are all BCL types requiring no packages
- **ImplicitUsings=enable** means `System`, `System.Collections.Generic`, `System.Linq`, `System.Threading`, `System.Threading.Tasks` are available without explicit `using` statements — do NOT add redundant `using` directives
- **File-scoped namespaces** — every file uses `namespace Foo.Bar;` (with semicolon), never block-scoped `namespace Foo.Bar { }`
- **No XML doc comments** — `CS1591` is suppressed; do not add `///` stubs
- **No `#pragma warning disable`** — every warning must be fixed at the source
- **Namespace must match folder path** — `src/DotnetTokenKiller.Domain/Filters/IOutputFilter.cs` must declare `namespace DotnetTokenKiller.Domain.Filters;`

### Known Analyzer Pitfalls (from Story 1.1)

- **S6966** (SonarAnalyzer) — always await async methods; fired in Story 1.1 on `app.Run(args)` → fixed with `await app.RunAsync(args)`. Watch for similar issues in test code.
- **CA1068** — `CancellationToken` parameters must come last in method signatures. All interface methods above already comply.
- **CA1716** — Reserved language keywords in identifiers. `CommandResult`, `CommandRecord`, etc. are safe. Avoid naming types `String`, `Object`, `Exception`.
- **RCS1018** — Roslynator requires explicit accessibility modifiers. All types are `public`; all interface methods are implicitly `public` — no modifiers needed inside interfaces (that would cause a different warning).
- **S2094** — No empty classes/interfaces. Every interface/type must have at least one member.
- **CA1040** — Avoid empty interfaces. Not applicable since all interfaces have methods.

### Testing Guidance

Tests live in `tests/DotnetTokenKiller.Domain.Tests/`. Domain tests are pure unit tests — **no mocks, no I/O, no NSubstitute needed**.

**Test class naming**: `{Subject}Tests` (e.g., `CommandResultTests`)
**Test project already has**: xunit, FluentAssertions. No additional packages needed for this story.

Minimal test examples:

```csharp
// CommandResultTests.cs
namespace DotnetTokenKiller.Domain.Tests;

public class CommandResultTests
{
    [Fact]
    public void CommandResult_stores_all_fields()
    {
        var result = new CommandResult("out", "err", 42);

        result.StdOut.Should().Be("out");
        result.StdErr.Should().Be("err");
        result.ExitCode.Should().Be(42);
    }

    [Fact]
    public void CommandResult_record_equality_by_value()
    {
        var a = new CommandResult("x", "y", 0);
        var b = new CommandResult("x", "y", 0);

        a.Should().Be(b);
    }
}
```

```csharp
// DtkConfigTests.cs
namespace DotnetTokenKiller.Domain.Tests;

public class DtkConfigTests
{
    [Fact]
    public void DtkConfig_Default_has_expected_tracking_values()
    {
        var config = DtkConfig.Default;

        config.Tracking.Enabled.Should().BeTrue();
        config.Tracking.RetentionDays.Should().Be(90);
        config.Tracking.DbPath.Should().BeNull();
    }

    [Fact]
    public void DtkConfig_Default_has_expected_display_values()
    {
        var config = DtkConfig.Default;

        config.Display.Colors.Should().BeTrue();
        config.Display.Emoji.Should().BeTrue();
        config.Display.Width.Should().Be(120);
    }

    [Fact]
    public void DtkConfig_Default_has_expected_tee_values()
    {
        var config = DtkConfig.Default;

        config.Tee.Mode.Should().Be("failures");
        config.Tee.Directory.Should().BeNull();
        config.Tee.MaxFiles.Should().Be(20);
        config.Tee.MaxFileSizeBytes.Should().Be(1_048_576L);
    }
}
```

**Important for test file namespaces**: Use `namespace DotnetTokenKiller.Domain.Tests;` (matches the project root namespace set in the `.csproj`). Test classes do NOT need to be `public` (CA1515 is suppressed in test projects); however using `public` is also fine.

### What NOT to Do

- **Do not add any NuGet `<PackageReference>` to the Domain `.csproj`** — this is the #1 violation risk for this story
- **Do not add `using System.Threading.Tasks;`** — implicit usings cover it
- **Do not create stub/placeholder implementations** — this story defines contracts only; implementations come in Stories 1.3 (ICommandRunner), 1.4 (use cases), and Epic 5 (ITracker, SQLite)
- **Do not add a `PassthroughRunUseCase` or any Application layer code** — that belongs in Story 1.4
- **Do not reference Domain from Infrastructure/Application yet** — those wiring steps are in Story 1.3
- **Do not put `DtkConfig`, `TrackingConfig`, `DisplayConfig`, `TeeConfig` in separate files** — they are co-located in one `DtkConfig.cs` file (see Architecture §3 file layout)

### Project Structure Notes

- All 9 new `.cs` source files go in `src/DotnetTokenKiller.Domain/` subdirectories
- Test files go directly in `tests/DotnetTokenKiller.Domain.Tests/` (no subdirectories needed for this story)
- Namespace must match folder depth: `src/DotnetTokenKiller.Domain/Filters/IOutputFilter.cs` → `namespace DotnetTokenKiller.Domain.Filters;`
- `DtkConfig.cs` hosts all four config records (DtkConfig + 3 nested records) in one file — matching Architecture §3

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 1.2]
- [Source: _bmad-output/planning-artifacts/Architecture.md#3. Solution Structure]
- [Source: _bmad-output/planning-artifacts/Architecture.md#4. Layer Responsibilities]
- [Source: _bmad-output/planning-artifacts/Architecture.md#6. Core Data Flow — FilteredRunUseCase]
- [Source: _bmad-output/planning-artifacts/Architecture.md#7. Filter Design]
- [Source: _bmad-output/planning-artifacts/Architecture.md#8. Infrastructure Details]
- [Source: _bmad-output/planning-artifacts/Architecture.md#9. DI Registration]
- [Source: _bmad-output/project-context.md#Clean Architecture — Dependency Rules]
- [Source: _bmad-output/project-context.md#Language-Specific Rules]
- [Source: _bmad-output/project-context.md#Code Quality & Style Rules]
- [Source: _bmad-output/implementation-artifacts/1-1-scaffold-clean-architecture-solution.md#Dev Agent Record]

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

- `RCS1124` (Roslynator) fired on `var result = new CommandResult(...)` in deconstruction test — `result` was used only once; fixed by inlining directly into the deconstruction: `var (stdOut, stdErr, exitCode) = new CommandResult(...)`.
- `[Fact]` requires explicit `using Xunit;` — not covered by `ImplicitUsings=enable` (which only includes BCL namespaces, not xunit). Added to all four test files.

### Completion Notes List

- Created 9 Domain source files: `IOutputFilter`, `CommandResult`, `ICommandRunner`, `CommandRecord`, `GainSummary`, `ITracker`, `DtkConfig` (with `TrackingConfig`/`DisplayConfig`/`TeeConfig`), `IConfigProvider`, `ITeeService` — all with file-scoped namespaces, zero NuGet deps.
- All interfaces include `CancellationToken cancellationToken = default` as final parameter per CA1068.
- `DtkConfig.cs` co-locates all four config records in one file matching Architecture §3 layout.
- Created 4 test files with 17 total tests; all pass with `dotnet test`. Fixed `RCS1124` by inlining a single-use local variable.
- `dotnet build DotnetTokenKiller.slnx` → 0 errors, 0 warnings across all projects.
- `dotnet format DotnetTokenKiller.slnx --no-restore --verify-no-changes` → exit 0.
- Domain project `.csproj` unchanged — still zero NuGet package references.

### File List

- src/DotnetTokenKiller.Domain/Filters/IOutputFilter.cs (created)
- src/DotnetTokenKiller.Domain/Execution/CommandResult.cs (created)
- src/DotnetTokenKiller.Domain/Execution/ICommandRunner.cs (created)
- src/DotnetTokenKiller.Domain/Tracking/CommandRecord.cs (created)
- src/DotnetTokenKiller.Domain/Tracking/GainSummary.cs (created)
- src/DotnetTokenKiller.Domain/Tracking/ITracker.cs (created)
- src/DotnetTokenKiller.Domain/Configuration/DtkConfig.cs (created)
- src/DotnetTokenKiller.Domain/Configuration/IConfigProvider.cs (created)
- src/DotnetTokenKiller.Domain/Tee/ITeeService.cs (created)
- tests/DotnetTokenKiller.Domain.Tests/CommandResultTests.cs (created)
- tests/DotnetTokenKiller.Domain.Tests/DtkConfigTests.cs (created)
- tests/DotnetTokenKiller.Domain.Tests/CommandRecordTests.cs (created)
- tests/DotnetTokenKiller.Domain.Tests/GainSummaryTests.cs (created)
