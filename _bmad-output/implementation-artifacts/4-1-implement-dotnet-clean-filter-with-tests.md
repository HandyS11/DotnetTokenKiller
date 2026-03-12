# Story 4.1: Implement dotnet clean Filter with Tests

Status: done

<!-- Note: Validation is optional. Run validate-create-story for quality check before dev-story. -->

## Story

As a developer running `dtk dotnet clean`,
I want clean output reduced to a single success marker,
So that I save 95%+ of tokens from the verbose MSBuild clean output.

## Acceptance Criteria

1. **Success (clean run)**: When `DotnetCleanFilter.Apply(rawOutput)` is called with a fixture containing a successful clean (no "FAILED" in output), the output is exactly `✓ dotnet clean\n`. Token savings ≥95%.
2. **Failure (error lines)**: When `Apply(rawOutput)` is called with a fixture where the output contains "FAILED" (case-insensitive), up to 5 lines containing "error" (case-insensitive) are shown in the output.
3. **Null/empty safety**: `Apply(null)` and `Apply("")` return a non-null string without throwing.
4. **`DotnetCleanCommand` wired**: `DotnetCleanCommand` injects `FilteredRunUseCase filteredRun` and `DotnetCleanFilter filter` and calls `filteredRun.RunAsync(filter, "dotnet", args, settings.Verbose.Length, cancellationToken)` instead of `RunPassthroughAsync`.
5. **DI registration**: `DotnetCleanFilter` is registered as a singleton in `AddApplication()`.
6. **Snapshot test**: A Verify.Xunit snapshot test exists for the success scenario in `tests/DotnetTokenKiller.Application.Tests/Filters/`.
7. **Fixture file**: `dotnet_clean_raw.txt` exists as an embedded resource in `tests/DotnetTokenKiller.Application.Tests/Fixtures/`.
8. **All tests pass**: `dotnet test DotnetTokenKiller.slnx` → all tests pass (no regressions on existing 102 tests).
9. **Zero warnings build**: `dotnet build DotnetTokenKiller.slnx` → 0 errors, 0 warnings.

## Tasks / Subtasks

- [x] Task 1: Create fixture file as embedded resource (AC: #7)
  - [x] Create `tests/DotnetTokenKiller.Application.Tests/Fixtures/dotnet_clean_raw.txt` — see "Fixture File Content" section below
  - [x] The `EmbeddedResource` glob `Fixtures/**` already exists in the `.csproj` from story 1.5 — no `.csproj` changes needed

- [x] Task 2: Implement `DotnetCleanFilter` (AC: #1, #2, #3)
  - [x] Create `src/DotnetTokenKiller.Application/Filters/DotnetCleanFilter.cs`
  - [x] `public sealed class DotnetCleanFilter : IOutputFilter`
  - [x] Implement `Apply(string rawOutput)` — see "Implementation" section below
  - [x] No regex needed: use `string.Contains` with `StringComparison.OrdinalIgnoreCase`

- [x] Task 3: Register `DotnetCleanFilter` in DI (AC: #5)
  - [x] Add `services.AddSingleton<DotnetCleanFilter>()` to `src/DotnetTokenKiller.Application/DependencyInjection.cs`

- [x] Task 4: Wire `DotnetCleanCommand` to use `FilteredRunUseCase` (AC: #4)
  - [x] Update `src/DotnetTokenKiller.Cli/Commands/DotnetCleanCommand.cs`
  - [x] Inject `FilteredRunUseCase filteredRun` and `DotnetCleanFilter filter` via primary constructor
  - [x] Replace `commandRunner.RunPassthroughAsync` call with `filteredRun.RunAsync(filter, "dotnet", args, settings.Verbose.Length, cancellationToken)`
  - [x] Remove old `ICommandRunner commandRunner` injection and its `using` import

- [x] Task 5: Write filter tests (AC: #1, #2, #3, #6)
  - [x] Create `tests/DotnetTokenKiller.Application.Tests/Filters/DotnetCleanFilterTests.cs`
  - [x] Snapshot test for success scenario (Verify.Xunit — static `Verifier.Verify()`)
  - [x] Savings gate test: ≥95%
  - [x] Noise line tests: verify MSBuild header, "Build succeeded", "0 Warning(s)", etc. absent from success output
  - [x] Error test: verify up to 5 error lines shown on failure (inline fixture)
  - [x] Edge case: `Apply(null!)` → no throw, returns non-null
  - [x] Edge case: `Apply("")` → no throw, returns non-null

- [x] Task 6: Accept Verify snapshots and commit `.verified.txt` files (AC: #6)
  - [x] Run `dotnet test --filter "FullyQualifiedName~DotnetCleanFilterTests"` → first run fails (no `.verified.txt`)
  - [x] Inspect `.received.txt` in `tests/DotnetTokenKiller.Application.Tests/Snapshots/` — should contain `✓ dotnet clean`
  - [x] Rename `.received.txt` → `.verified.txt`
  - [x] Re-run tests → all snapshot tests pass

- [x] Task 7: Build and verify (AC: #8, #9)
  - [x] `dotnet build DotnetTokenKiller.slnx` → 0 errors, 0 warnings
  - [x] `dotnet test DotnetTokenKiller.slnx` → all tests pass (existing 102 + new clean tests = 119 total)
  - [x] `dotnet format DotnetTokenKiller.slnx --no-restore --verify-no-changes` → exit 0

## Dev Notes

### Current Repository State (After Stories 1.1–1.6, 2.1, 3.1–3.3)

All files below exist and **MUST NOT be modified** unless listed as a target in this story:

| File | State |
|---|---|
| `DotnetTokenKiller.slnx` | Complete — 8 projects |
| `Directory.Build.props` | Complete — `net10.0`, C#14, TreatWarningsAsErrors |
| `Directory.Packages.props` | Complete — `Verify.Xunit 28.2.0` already added |
| `.editorconfig` | Complete — CA1031 and CA1303 suppressed |
| `src/DotnetTokenKiller.Domain/Filters/IOutputFilter.cs` | `string Apply(string rawOutput)` — MUST NOT change |
| `src/DotnetTokenKiller.Application/Helpers/AnsiStrip.cs` | `Strip(string text)` — complete |
| `src/DotnetTokenKiller.Application/Helpers/TokenEstimator.cs` | `Estimate(string text)` — complete |
| `src/DotnetTokenKiller.Application/Helpers/TextHelpers.cs` | `ShortenPath`, `Truncate`, `FormatTokens` — complete |
| `src/DotnetTokenKiller.Application/Filters/DotnetBuildFilter.cs` | Complete (story 1.5) — DO NOT BREAK |
| `src/DotnetTokenKiller.Application/Filters/DotnetTestFilter.cs` | Complete (story 2.1) — DO NOT BREAK |
| `src/DotnetTokenKiller.Application/Filters/DotnetRestoreFilter.cs` | Complete (story 3.1) — DO NOT BREAK |
| `src/DotnetTokenKiller.Application/Filters/DotnetPublishFilter.cs` | Complete (story 3.2) — DO NOT BREAK |
| `src/DotnetTokenKiller.Application/Filters/DotnetPackFilter.cs` | Complete (story 3.3) — DO NOT BREAK |
| `src/DotnetTokenKiller.Application/UseCases/FilteredRunUseCase.cs` | Complete — DO NOT MODIFY |
| `src/DotnetTokenKiller.Application/DependencyInjection.cs` | **Needs `DotnetCleanFilter` singleton added** |
| `src/DotnetTokenKiller.Cli/Commands/DotnetCleanCommand.cs` | **Needs rewiring to `FilteredRunUseCase`** — currently uses `ICommandRunner.RunPassthroughAsync` |
| `src/DotnetTokenKiller.Cli/Program.cs` | Complete — DO NOT MODIFY |
| `tests/DotnetTokenKiller.Application.Tests/VerifyInit.cs` | Complete — `UseProjectRelativeDirectory("Snapshots")` + `IgnoreStackTrace()` |
| `tests/DotnetTokenKiller.Application.Tests/Filters/DotnetBuildFilterTests.cs` | Complete — DO NOT BREAK |
| `tests/DotnetTokenKiller.Application.Tests/Filters/DotnetTestFilterTests.cs` | Complete — DO NOT BREAK |
| `tests/DotnetTokenKiller.Application.Tests/Filters/DotnetRestoreFilterTests.cs` | Complete — DO NOT BREAK |
| `tests/DotnetTokenKiller.Application.Tests/Filters/DotnetPublishFilterTests.cs` | Complete — DO NOT BREAK |
| `tests/DotnetTokenKiller.Application.Tests/Filters/DotnetPackFilterTests.cs` | Complete — DO NOT BREAK |
| `tests/DotnetTokenKiller.Application.Tests/Fixtures/` | Exists — add `dotnet_clean_raw.txt` here |
| `.github/workflows/quality-gate.yml` | Complete (story 1.6) |

**Test count baseline**: 102 tests total. All must continue to pass.

**`DotnetCleanCommand` currently calls `commandRunner.RunPassthroughAsync` directly** — this story replaces it with `FilteredRunUseCase.RunAsync`. Same rewiring pattern as stories 3.1 (restore), 3.2 (publish), 3.3 (pack).

### Architecture Constraints (CRITICAL)

- `DotnetCleanFilter` lives in `Application` layer → references `Domain` only (`IOutputFilter`) + `Helpers` namespace
- Filter MUST be `stateless` — no instance fields at all (no `_rootPath` needed: clean filter does not shorten paths)
- Filter MUST be `sealed` (CA1852 analyzer)
- Filter does NOT need `partial` — no `[GeneratedRegex]` patterns are used (no regex at all)
- `IOutputFilter.Apply` signature is `string Apply(string rawOutput)` — MUST NOT change the interface
- File-scoped namespaces: `namespace DotnetTokenKiller.Application.Filters;`
- `using System.Text` is needed for `StringBuilder`

### Why No Regex?

The clean filter's logic requires only two string operations:

1. Check whether output contains "FAILED" (case-insensitive) → `string.Contains(s, StringComparison.OrdinalIgnoreCase)`
2. Check whether a line contains "error" (case-insensitive) → same

These are zero-allocation string searches, faster and simpler than regex. `[GeneratedRegex]` is only needed when actual pattern matching is required — not here.

### Implementation: `DotnetCleanFilter`

```csharp
namespace DotnetTokenKiller.Application.Filters;

using System.Text;
using DotnetTokenKiller.Application.Helpers;
using DotnetTokenKiller.Domain.Filters;

public sealed class DotnetCleanFilter : IOutputFilter
{
    public string Apply(string rawOutput)
    {
        if (string.IsNullOrEmpty(rawOutput))
            return string.Empty;

        var stripped = AnsiStrip.Strip(rawOutput);
        var lines = stripped.Split('\n');

        // Check if build failed
        var failed = Array.Exists(lines, l => l.TrimEnd('\r').Contains("FAILED", StringComparison.OrdinalIgnoreCase));
        if (!failed)
            return "✓ dotnet clean\n";

        // Show up to 5 lines containing "error" (case-insensitive)
        var sb = new StringBuilder();
        var count = 0;
        foreach (var rawLine in lines)
        {
            if (count >= 5)
                break;

            var line = rawLine.TrimEnd('\r');
            if (line.Contains("error", StringComparison.OrdinalIgnoreCase))
            {
                sb.AppendLine(line);
                count++;
            }
        }

        return sb.ToString();
    }
}
```

**Key design decisions:**

- No `_rootPath` field — clean output does not contain file paths needing shortening
- `Array.Exists` instead of LINQ for the FAILED check — avoids LINQ overhead, satisfies CA1851 if raised
- Returns `string.Empty` for null/empty (same as all other filters)
- "FAILED" check gates the error path — prevents false positives from "0 Error(s)" count line in success output

### Updated `DotnetCleanCommand.cs`

```csharp
using DotnetTokenKiller.Application.Filters;
using DotnetTokenKiller.Application.UseCases;
using DotnetTokenKiller.Cli.Commands.Settings;
using Spectre.Console.Cli;

namespace DotnetTokenKiller.Cli.Commands;

public sealed class DotnetCleanCommand(
    FilteredRunUseCase filteredRun,
    DotnetCleanFilter filter) : AsyncCommand<DotnetCommandSettings>
{
    public override async Task<int> ExecuteAsync(CommandContext context, DotnetCommandSettings settings, CancellationToken cancellationToken)
    {
        var args = settings.PositionalArgs.Prepend("clean").Concat(context.Remaining.Raw).ToArray();
        return await filteredRun.RunAsync(filter, "dotnet", args, settings.Verbose.Length, cancellationToken);
    }
}
```

**Note**: Remove `ICommandRunner` injection and its `using DotnetTokenKiller.Domain.Execution;` import — `FilteredRunUseCase` holds it.

### Updated `DependencyInjection.cs` (Application project)

Add one line after `DotnetPackFilter`. Match the existing pattern — but `DotnetCleanFilter` has no constructor args:

```csharp
services.AddSingleton<DotnetCleanFilter>();  // ADD THIS — no-arg constructor
```

**Why no lambda?** `DotnetCleanFilter` has a parameterless constructor — `AddSingleton<T>()` resolves directly without a factory lambda. This is cleaner and avoids the `_ => new DotnetCleanFilter()` pattern since there's no `rootPath` to pass.

Full `AddApplication` method after change:

```csharp
public static IServiceCollection AddApplication(this IServiceCollection services)
{
    services.AddTransient<FilteredRunUseCase>();
    services.AddSingleton<DotnetBuildFilter>(_ => new DotnetBuildFilter());
    services.AddSingleton<DotnetTestFilter>(_ => new DotnetTestFilter());
    services.AddSingleton<DotnetRestoreFilter>(_ => new DotnetRestoreFilter());
    services.AddSingleton<DotnetPublishFilter>(_ => new DotnetPublishFilter());
    services.AddSingleton<DotnetPackFilter>(_ => new DotnetPackFilter());
    services.AddSingleton<DotnetCleanFilter>();  // NEW
    return services;
}
```

### Fixture File Content

Create `tests/DotnetTokenKiller.Application.Tests/Fixtures/dotnet_clean_raw.txt`:

```sh
MSBuild version 17.11.9+a69bbaaf5 for .NET
  Cleaning configuration "Debug" of project "/home/handys11/Dev/DotnetTokenKiller/src/DotnetTokenKiller.Domain/DotnetTokenKiller.Domain.csproj".
  Cleaning configuration "Debug" of project "/home/handys11/Dev/DotnetTokenKiller/src/DotnetTokenKiller.Application/DotnetTokenKiller.Application.csproj".
  Cleaning configuration "Debug" of project "/home/handys11/Dev/DotnetTokenKiller/src/DotnetTokenKiller.Infrastructure/DotnetTokenKiller.Infrastructure.csproj".
  Cleaning configuration "Debug" of project "/home/handys11/Dev/DotnetTokenKiller/src/DotnetTokenKiller.Cli/DotnetTokenKiller.Cli.csproj".
  Cleaning configuration "Debug" of project "/home/handys11/Dev/DotnetTokenKiller/tests/DotnetTokenKiller.Domain.Tests/DotnetTokenKiller.Domain.Tests.csproj".
  Cleaning configuration "Debug" of project "/home/handys11/Dev/DotnetTokenKiller/tests/DotnetTokenKiller.Application.Tests/DotnetTokenKiller.Application.Tests.csproj".
  Cleaning configuration "Debug" of project "/home/handys11/Dev/DotnetTokenKiller/tests/DotnetTokenKiller.Infrastructure.Tests/DotnetTokenKiller.Infrastructure.Tests.csproj".
  Cleaning configuration "Debug" of project "/home/handys11/Dev/DotnetTokenKiller/tests/DotnetTokenKiller.Cli.IntegrationTests/DotnetTokenKiller.Cli.IntegrationTests.csproj".
Build succeeded.
    0 Warning(s)
    0 Error(s)

Time Elapsed 00:00:00.29
```

**Expected output** (for snapshot verification):

```sh
✓ dotnet clean
```

**Token savings calculation:**

- Fixture: ~1 100 chars (8 project cleaning lines × ~120 chars + header/footer)
- Output: `✓ dotnet clean\n` = 15 chars
- Savings: (1100 - 15) / 1100 ≈ 98.6% ≥ 95% ✓

### Test Implementation

```csharp
namespace DotnetTokenKiller.Application.Tests.Filters;

using DotnetTokenKiller.Application.Filters;
using FluentAssertions;

public class DotnetCleanFilterTests
{
    private readonly DotnetCleanFilter _sut = new();

    [Fact]
    public Task Apply_SuccessFixture_MatchesSnapshot()
    {
        var fixture = LoadFixture("dotnet_clean_raw.txt");
        var result = _sut.Apply(fixture);
        return Verify(result);
    }

    [Fact]
    public void Apply_SuccessFixture_SavingsAtLeast95Percent()
    {
        var fixture = LoadFixture("dotnet_clean_raw.txt");
        var result = _sut.Apply(fixture);
        var savings = 100.0 - (result.Length * 100.0 / fixture.Length);
        savings.Should().BeGreaterThanOrEqualTo(95.0, because: "clean filter should achieve ≥95% savings");
    }

    [Theory]
    [InlineData("MSBuild version")]
    [InlineData("Cleaning configuration")]
    [InlineData("Build succeeded")]
    [InlineData("0 Warning(s)")]
    [InlineData("Time Elapsed")]
    public void Apply_SuccessFixture_DoesNotContainNoiseLine(string noiseLine)
    {
        var fixture = LoadFixture("dotnet_clean_raw.txt");
        _sut.Apply(fixture).Should().NotContain(noiseLine);
    }

    [Fact]
    public void Apply_FailedBuild_ShowsUpToFiveErrorLines()
    {
        const string input = """
            MSBuild version 17.11.9+a69bbaaf5 for .NET
            error MSB4057: The target "Clean" does not exist in the project.
            error MSB4057: The target "Clean" does not exist in the project.
            error MSB4057: The target "Clean" does not exist in the project.
            error MSB4057: The target "Clean" does not exist in the project.
            error MSB4057: The target "Clean" does not exist in the project.
            error MSB4057: The target "Clean" does not exist in the project.
            Build FAILED.
                0 Warning(s)
                6 Error(s)
            Time Elapsed 00:00:00.10
            """;
        var result = _sut.Apply(input);
        var errorLines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        errorLines.Should().HaveCountLessOrEqualTo(5, because: "only up to 5 error lines should be shown");
        result.Should().Contain("error MSB4057");
    }

    [Fact]
    public void Apply_FailedBuild_OutputContainsErrorContent()
    {
        const string input = """
            MSBuild version 17.11.9+a69bbaaf5 for .NET
            error MSB4019: The imported project "/missing/file.targets" was not found.
            Build FAILED.
                1 Error(s)
            Time Elapsed 00:00:00.05
            """;
        var result = _sut.Apply(input);
        result.Should().Contain("error MSB4019");
        result.Should().NotStartWith("✓");
    }

    [Fact]
    public void Apply_NullInput_ReturnsNonNull()
    {
        _sut.Apply(null!).Should().NotBeNull();
    }

    [Fact]
    public void Apply_EmptyInput_ReturnsNonNull()
    {
        _sut.Apply(string.Empty).Should().NotBeNull();
    }

    private static string LoadFixture(string resourceName)
    {
        var assembly = typeof(DotnetCleanFilterTests).Assembly;
        var fullName = assembly.GetManifestResourceNames()
            .First(n => n.EndsWith(resourceName, StringComparison.OrdinalIgnoreCase));
        using var stream = assembly.GetManifestResourceStream(fullName)!;
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }
}
```

**Important notes on the test:**

- `_sut = new()` — no constructor args (no `rootPath` parameter)
- `Verify(result)` returns `Task` — test method must return `Task` (not `void`), must NOT be `async`
- `Apply_FailedBuild_ShowsUpToFiveErrorLines` — 6 identical error lines in input; only ≤5 should appear in output
- The "1 Error(s)" / "6 Error(s)" count lines also match "error" case-insensitively — that's acceptable since the cap is 5 and the important content (the actual error message) comes first

### Verify Snapshot Acceptance Workflow

1. Run: `dotnet test --filter "FullyQualifiedName~DotnetCleanFilterTests"`
2. Snapshot test fails; `.received.txt` appears in `tests/DotnetTokenKiller.Application.Tests/Snapshots/`
3. Inspect `DotnetCleanFilterTests.Apply_SuccessFixture_MatchesSnapshot.received.txt` — should contain exactly `✓ dotnet clean`
4. Accept by renaming: `mv DotnetCleanFilterTests.Apply_SuccessFixture_MatchesSnapshot.received.txt DotnetCleanFilterTests.Apply_SuccessFixture_MatchesSnapshot.verified.txt`
5. Re-run tests — all pass
6. Commit the `.verified.txt` file

### Analyzer Pitfalls (CRITICAL — accumulated from stories 1.5, 2.1, 3.1–3.3)

- **CA1852** — `DotnetCleanFilter` MUST be `sealed`
- **CA1050/RCS1110/S3903** — type MUST be in named namespace (file-scoped `namespace DotnetTokenKiller.Application.Filters;` satisfies this)
- **No `[GeneratedRegex]`** — filter uses `string.Contains` only; DO NOT add unnecessary partial/regex infrastructure
- **No `partial`** — `DotnetCleanFilter` is `sealed class`, not `sealed partial class`; only add `partial` if using `[GeneratedRegex]`
- **`using System.Text`** — NOT in implicit usings; must be explicit (needed for `StringBuilder`)
- **Namespace must match folder path**: `src/.../Application/Filters/DotnetCleanFilter.cs` → `namespace DotnetTokenKiller.Application.Filters;`
- **`using` import ordering**: After `dotnet format`, project usings come before system usings per `.editorconfig`
- **CA1851** — Avoid multiple enumerations; `Array.Exists` is used instead of LINQ `.Any()` to check the FAILED condition which avoids potential CA1851 on the subsequent loop
- **`StringComparison.OrdinalIgnoreCase`** — always pass explicitly with `string.Contains`/`string.IndexOf` to avoid CA1307/CA1309

### Git Context (Recent Commits)

```sh
c568abc Feat: restore, publish & pack filters (#5)
992e65f Fix: enhance duration parsing in DotnetTestFilter to support multiple time units
f1dbde2 Feat: test filters (#2)
d534cbd Feat: core & foundation (#1)
4c78002 Scaffold Clean Architecture solution with initial project structure and configurations
```

Current branch: `develop`. Epic 4 stories will be implemented on feature branches.

### Previous Story Intelligence (Story 3.3 — Pack Filter)

Key learnings from story 3.3 applied here:

- **`sealed record Diagnostic` inside filter**: not needed for clean filter — no structured diagnostics
- **Snapshot directory**: configured globally in `VerifyInit.cs` via `[ModuleInitializer]` — no per-test class configuration needed
- **`Verify(result)` returns `Task`**: test method must return `Task`, must NOT be `async`
- **`dotnet format` run last**: always run after all tests pass — catches import ordering issues
- **Fixture absolute paths**: used for path-shortening tests in previous filters; NOT needed here since clean filter produces no paths
- **Test class naming**: `DotnetCleanFilterTests` in `DotnetTokenKiller.Application.Tests.Filters` namespace
- **`DotnetCleanFilter` has no `rootPath` constructor arg** — simpler than publish/pack filter; `_sut = new()` not `new("/home/...")`

### What This Story Does NOT Implement (Scope Guard)

- `DotnetRunFilter`, `DotnetEfFilter`, `DotnetFormatFilter`, `DotnetNugetFilter` — Stories 4.2–4.5
- Passthrough for unrecognized subcommands — Story 4.6
- `SqliteTracker` — Story 5.1
- `JsonConfigProvider` — Story 6.1
- `FileTeeService` — Story 6.2
- Any other CLI command rewiring besides `DotnetCleanCommand`

### Project Structure Notes

- New filter: `src/DotnetTokenKiller.Application/Filters/DotnetCleanFilter.cs`
- Updated DI: `src/DotnetTokenKiller.Application/DependencyInjection.cs` — add `DotnetCleanFilter` singleton
- Updated command: `src/DotnetTokenKiller.Cli/Commands/DotnetCleanCommand.cs` — rewire to `FilteredRunUseCase`
- New fixture: `tests/DotnetTokenKiller.Application.Tests/Fixtures/dotnet_clean_raw.txt`
- New test file: `tests/DotnetTokenKiller.Application.Tests/Filters/DotnetCleanFilterTests.cs`
- New snapshot: `tests/DotnetTokenKiller.Application.Tests/Snapshots/DotnetCleanFilterTests.Apply_SuccessFixture_MatchesSnapshot.verified.txt`
- No new directories to create — all target directories already exist

### References

- [Source: _bmad-output/planning-artifacts/epics.md#Story 4.1]
- [Source: _bmad-output/implementation-artifacts/3-3-implement-dotnet-pack-filter-with-tests.md] — previous story; same rewiring pattern for command + DI
- [Source: src/DotnetTokenKiller.Application/Filters/DotnetPublishFilter.cs] — reference for filter structure (clean is simpler)
- [Source: src/DotnetTokenKiller.Cli/Commands/DotnetCleanCommand.cs] — current state (uses RunPassthroughAsync)
- [Source: src/DotnetTokenKiller.Application/DependencyInjection.cs] — current state (needs `DotnetCleanFilter` added)
- [Source: tests/DotnetTokenKiller.Application.Tests/VerifyInit.cs] — Verify.Xunit v28 init pattern
- [Source: _bmad-output/planning-artifacts/Architecture.md#7] — Filter Design constraints (stateless, sealed, non-throwing)

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

### Completion Notes List

- Implemented `DotnetCleanFilter` — simplest filter in the suite; no regex, no `_rootPath`, pure `string.Contains` logic
- Success path: checks for absence of "FAILED" (case-insensitive) → returns `✓ dotnet clean\n`
- Error path: collects up to 5 lines containing "error" (case-insensitive) from failed output
- Added try/catch per project-context "never let a filter throw" rule — returns `rawOutput` on exception
- Registered `DotnetCleanFilter` singleton in `AddApplication()` using no-arg `AddSingleton<T>()` (no factory lambda needed)
- Rewired `DotnetCleanCommand` from `ICommandRunner.RunPassthroughAsync` to `FilteredRunUseCase.RunAsync` with primary constructor injection
- Fixed import ordering: project usings before system usings per `.editorconfig`
- Fixed FluentAssertions 8.x API: `BeLessThanOrEqualTo` (not `BeLessOrEqualTo`)
- 12 new tests added: snapshot, savings gate (≥95%), 5 noise line theory tests, 2 error tests, ANSI test, null/empty safety
- Verify snapshot accepted: `✓ dotnet clean` (~98.6% savings on 8-project fixture, ≥95% threshold met)
- Total test count: 119 (17 Domain + 100 Application + 1 Infrastructure + 1 Integration) — 0 regressions
- Build: 0 errors, 0 warnings; `dotnet format --verify-no-changes` → exit 0

### File List

- src/DotnetTokenKiller.Application/Filters/DotnetCleanFilter.cs (new)
- src/DotnetTokenKiller.Application/DependencyInjection.cs (modified)
- src/DotnetTokenKiller.Cli/Commands/DotnetCleanCommand.cs (modified)
- tests/DotnetTokenKiller.Application.Tests/Filters/DotnetCleanFilterTests.cs (new)
- tests/DotnetTokenKiller.Application.Tests/Fixtures/dotnet_clean_raw.txt (new)
- tests/DotnetTokenKiller.Application.Tests/Snapshots/DotnetCleanFilterTests.Apply_SuccessFixture_MatchesSnapshot.verified.txt (new)

### Change Log

- 2026-03-11: Implemented DotnetCleanFilter with tests; wired DotnetCleanCommand to FilteredRunUseCase; registered singleton in DI; accepted Verify snapshot `✓ dotnet clean` (~98.6% savings)
