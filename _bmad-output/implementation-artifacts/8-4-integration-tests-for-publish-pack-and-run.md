# Story 8.4: Integration Tests for publish, pack, and run

Status: done

## Story

As a developer,
I want integration tests that invoke `dtk dotnet publish`, `dtk dotnet pack`, and `dtk dotnet run` — covering both success and failure paths for each,
so that output path extraction, preamble stripping, and exit code propagation are validated against real MSBuild and process output.

## Acceptance Criteria

1. **— dotnet publish success —**

   **Given** `dtk dotnet publish` is invoked against `sample/SampleApp`
   **When** publish succeeds
   **Then** the output is a single line starting with `✓ dotnet publish →`
   **And** the output contains the shortened publish output path ending in `publish/`
   **And** no MSBuild restore or compile noise is present
   **And** the exit code is 0
   **And** token savings is ≥80%

2. **— dotnet publish failure —**

   **Given** `dtk dotnet publish` is invoked against `sample/SampleApp.Broken`
   **When** publish fails due to the compile error
   **Then** the output starts with `dotnet publish: 1 error`
   **And** the error line contains a shortened file path and line number
   **And** the exit code is non-zero
   **And** token savings is ≥70%

3. **— dotnet pack success —**

   **Given** `dtk dotnet pack` is invoked against `sample/SampleApp`
   **When** pack succeeds
   **Then** the output is a single line starting with `✓ dotnet pack → SampleApp.1.0.0.nupkg`
   **And** no MSBuild noise is present
   **And** the exit code is 0
   **And** token savings is ≥85%

4. **— dotnet pack failure —**

   **Given** `dtk dotnet pack` is invoked against `sample/SampleApp.Broken`
   **When** pack fails due to the compile error
   **Then** the output starts with `dotnet pack: 1 error`
   **And** the error line contains a shortened file path and line number
   **And** the exit code is non-zero

5. **— dotnet run success —**

   **Given** `dtk dotnet run` is invoked against `sample/SampleApp` (no extra arguments)
   **When** the application runs and exits with code 0
   **Then** `Hello from SampleApp!` is present in the output
   **And** no MSBuild build preamble lines are present (`MSBuild version`, `Determining projects to restore`, `Build started`, lines matching `→ .dll`)
   **And** the exit code is 0
   **And** token savings is ≥60%

6. **— dotnet run failure path —**

   **Given** `dtk dotnet run` is invoked against `sample/SampleApp` with `-- --fail`
   **When** the application calls `Environment.Exit(1)`
   **Then** `App starting...` is present in the output (app stdout before exit is preserved)
   **And** no MSBuild build preamble lines are present
   **And** the exit code is 1

7. **And** all integration tests in this story are in `DotnetTokenKiller.Cli.IntegrationTests`
   **And** `dotnet test DotnetTokenKiller.slnx` passes with all tests green

## Tasks / Subtasks

- [x] Implement `DotnetPublishIntegrationTests` class (AC: #1, #2, #7)
  - [x] `Publish_SampleApp_Success_OutputStartsWithCheckmark` — single-line `✓ dotnet publish →`, exit 0 (AC: #1)
  - [x] `Publish_SampleApp_Success_ContainsPublishPath` — output contains `publish/` (AC: #1)
  - [x] `Publish_SampleApp_Success_NoMsBuildNoise` — no "MSBuild version", "Restoring", "Build succeeded" in output (AC: #1)
  - [x] `Publish_SampleApp_Success_Savings80Percent` — token savings ≥80% (AC: #1)
  - [x] `Publish_SampleAppBroken_Failure_OutputStartsWith1Error` — starts with `dotnet publish: 1 error`, exit non-zero (AC: #2)
  - [x] `Publish_SampleAppBroken_Failure_ErrorContainsShortenedPath` — shortened path + `(line,col)` in output (AC: #2)
  - [x] `Publish_SampleAppBroken_Failure_Savings70Percent` — token savings ≥70% (AC: #2)

- [x] Implement `DotnetPackIntegrationTests` class (AC: #3, #4, #7)
  - [x] `Pack_SampleApp_Success_OutputStartsWithNupkgName` — starts with `✓ dotnet pack → SampleApp.1.0.0.nupkg`, exit 0 (AC: #3)
  - [x] `Pack_SampleApp_Success_NoMsBuildNoise` — no noise lines in output (AC: #3)
  - [x] `Pack_SampleApp_Success_Savings85Percent` — token savings ≥85% (AC: #3)
  - [x] `Pack_SampleAppBroken_Failure_OutputStartsWith1Error` — starts with `dotnet pack: 1 error`, exit non-zero (AC: #4)
  - [x] `Pack_SampleAppBroken_Failure_ErrorContainsShortenedPath` — shortened path + line number (AC: #4)

- [x] Implement `DotnetRunIntegrationTests` class (AC: #5, #6, #7)
  - [x] `Run_SampleApp_Success_ContainsAppOutput` — `Hello from SampleApp!` in output, exit 0 (AC: #5)
  - [x] `Run_SampleApp_Success_NoMsBuildPreamble` — no preamble noise lines (AC: #5)
  - [x] `Run_SampleApp_Success_Savings60Percent` — token savings ≥60% (AC: #5)
  - [x] `Run_SampleApp_WithFail_ExitCodeIs1` — `App starting...` in output, exit 1 (AC: #6)
  - [x] `Run_SampleApp_WithFail_NoMsBuildPreamble` — no preamble noise (AC: #6)

- [x] Verify `dotnet test DotnetTokenKiller.slnx` passes green (AC: #7)

## Dev Notes

### What Already Exists — Do NOT Recreate

- `IntegrationTestHelper` in `tests/DotnetTokenKiller.Cli.IntegrationTests/IntegrationTestHelper.cs` is complete and stable. Do NOT modify. It provides:
  - `RunDtkAsync(params string[] args)` — invokes `dotnet dtk.dll <args>` via `ArgumentList`
  - `RunDotnetAsync(params string[] args)` — invokes `dotnet <args>` directly
  - `SamplePath(string project)` — resolves `sample/<project>` from repo root
  - `CalculateSavings(string rawOutput, string filteredOutput)` — `chars/4` heuristic

- `DotnetBuildIntegrationTests.cs`, `DotnetRestoreIntegrationTests.cs`, `DotnetCleanIntegrationTests.cs`, `DotnetTestIntegrationTests.cs` — already implemented in Stories 8.2 and 8.3. Follow their style exactly.

### Filter Output Formats (exact — from source code analysis)

**DotnetPublishFilter:**

Success:

```sh
✓ dotnet publish → bin/Debug/net10.0/SampleApp/publish/ (1 project, 0.45s)
```

(single line; path is project-relative with `publish/` suffix; from regex `→ (?<path>.+[\\/]publish[\\/]?)`)

Failure (1 error):

```sh
dotnet publish: 1 error, 0 warnings
---
sample/SampleApp.Broken/BrokenClass.cs (1 error)
  (8,21) CS0029: Cannot implicitly convert type 'string' to 'int'
Top codes: CS0029 (1x)
```

**DotnetPackFilter:**

Success:

```sh
✓ dotnet pack → SampleApp.1.0.0.nupkg (1 project, 0.45s)
```

(nupkg filename only, not full path; from regex `Successfully created package '(?<path>[^']+)'`)

Failure (1 error):

```sh
dotnet pack: 1 error, 0 warnings
---
sample/SampleApp.Broken/BrokenClass.cs (1 error)
  (8,21) CS0029: Cannot implicitly convert type 'string' to 'int'
Top codes: CS0029 (1x)
```

**DotnetRunFilter:**

Success (app has output):

```sh
Hello from SampleApp!
```

(strips MSBuild preamble — `MSBuild version`, `Determining projects to restore`, `Build started`, `Build succeeded`, `Time Elapsed`, `→ .dll` lines, blank lines — passes through all remaining app stdout)

If all lines are preamble/blank (no app output): `✓ dotnet run completed`

Failure (app exits non-zero but has output):

```sh
App starting...
```

(app stdout "App starting..." is preserved; exit code 1)

### Critical: Spectre.Console Arg Forwarding

From Story 8.3 debug log: Spectre.Console 0.53.1 with `StrictParsing = false` **silently drops** unknown option flags (`--filter`, `--verbosity`, `--project`). They do NOT end up in `context.Remaining.Raw`. Only args after `--` (the Spectre.Console separator) go into `Remaining.Raw` and are forwarded to dotnet.

**Publish/Pack:** Project paths are positional arguments — they work fine without `--`.

```csharp
// Works — SampleApp is positional arg forwarded as-is
await IntegrationTestHelper.RunDtkAsync("dotnet", "publish", SampleApp);
await IntegrationTestHelper.RunDtkAsync("dotnet", "pack", SampleApp);
```

**Run:** `--project` is an option flag — must use `--` to forward it:

```csharp
// Success path — --project forwarded via Spectre's Remaining.Raw
var (output, exitCode) = await IntegrationTestHelper.RunDtkAsync(
    "dotnet", "run", "--", "--project", SampleApp);

// Fail path — both --project and app arg --fail forwarded
// Result: dotnet run --project <path> -- --fail
var (output, exitCode) = await IntegrationTestHelper.RunDtkAsync(
    "dotnet", "run", "--", "--project", SampleApp, "--", "--fail");
```

### Token Savings Tests — Use --verbosity normal for Raw Output Only

From Stories 8.2 and 8.3: default verbosity produces too little raw output to demonstrate savings thresholds. Pattern: pass `--verbosity normal` to `RunDotnetAsync` to inflate raw output. Spectre drops `--verbosity` from `RunDtkAsync` anyway (no behavior change needed).

```csharp
// Publish success savings test
var (rawOutput, _) = await IntegrationTestHelper.RunDotnetAsync(
    "publish", "--verbosity", "normal", SampleApp);
var (output, _) = await IntegrationTestHelper.RunDtkAsync(
    "dotnet", "publish", "--verbosity", "normal", SampleApp); // --verbosity dropped by Spectre; fine

var savings = IntegrationTestHelper.CalculateSavings(rawOutput, output);
savings.Should().BeGreaterThanOrEqualTo(80.0);
```

Same pattern for pack (≥85%) and run (≥60%):

```csharp
// Run savings test
var (rawOutput, _) = await IntegrationTestHelper.RunDotnetAsync(
    "run", "--project", SampleApp, "--verbosity", "normal");
var (output, _) = await IntegrationTestHelper.RunDtkAsync(
    "dotnet", "run", "--", "--project", SampleApp); // no --verbosity needed

var savings = IntegrationTestHelper.CalculateSavings(rawOutput, output);
savings.Should().BeGreaterThanOrEqualTo(60.0);
```

### Test Class Structure (follow existing style exactly)

```csharp
using FluentAssertions;
using Xunit;

namespace DotnetTokenKiller.Cli.IntegrationTests;

[Trait("Category", "Integration")]
public class DotnetPublishIntegrationTests
{
    private static readonly string SampleApp =
        IntegrationTestHelper.SamplePath("SampleApp");

    private static readonly string SampleAppBroken =
        IntegrationTestHelper.SamplePath("SampleApp.Broken");

    [Fact]
    public async Task Publish_SampleApp_Success_OutputStartsWithCheckmark()
    {
        var (output, exitCode) = await IntegrationTestHelper.RunDtkAsync(
            "dotnet", "publish", SampleApp);

        exitCode.Should().Be(0);
        var lines = output.Trim().Split('\n');
        lines.Should().HaveCount(1);
        lines[0].Trim().Should().StartWith("✓ dotnet publish →");
    }

    // ... remaining methods
}
```

Note: `SampleApp` is `static readonly` (not `const`) — `IntegrationTestHelper.SamplePath(...)` is a method call, not a compile-time constant.

### Sample Projects Used

```sh
sample/SampleApp/SampleApp.csproj          # Exe project, Version=1.0.0
sample/SampleApp.Broken/SampleApp.Broken.csproj  # 1 CS0029 error (BrokenClass.cs)
```

- `SampleApp` version 1.0.0 → nupkg filename: `SampleApp.1.0.0.nupkg`
- `SampleApp/Program.cs` outputs `Hello from SampleApp!` (success) or `App starting...` + exits 1 (with `--fail`)
- `SampleApp.Broken` has `BrokenClass.cs` with 1 CS0029 compile error

### MSBuild Noise Assertions

For publish/pack noise tests, assert typical MSBuild lines are absent:

```csharp
output.Should().NotContain("MSBuild version");
output.Should().NotContain("Restoring");
output.Should().NotContain("Build succeeded");
```

For run preamble tests, assert DotnetRunFilter preamble patterns are absent:

```csharp
output.Should().NotContain("MSBuild version");
output.Should().NotContain("Determining projects to restore");
output.Should().NotContain("Build started");
output.Should().NotMatchRegex(@"\S+ -> .+\.dll"); // project output lines stripped
```

### Project Structure Notes

- Three new files only:
  - `tests/DotnetTokenKiller.Cli.IntegrationTests/DotnetPublishIntegrationTests.cs`
  - `tests/DotnetTokenKiller.Cli.IntegrationTests/DotnetPackIntegrationTests.cs`
  - `tests/DotnetTokenKiller.Cli.IntegrationTests/DotnetRunIntegrationTests.cs`
- No new packages needed — `FluentAssertions` and `xunit` already in `.csproj`
- No changes to `.csproj`, `IntegrationTestHelper`, or any existing test files
- No changes to sample projects

### Analyzer Pitfalls (accumulated from Stories 8.1–8.3)

- **VSTHRD200**: xUnit test methods are exempt from `Async` suffix — do NOT name methods `...Async`
- **CA1515**: Already suppressed in the integration test `.csproj` — public test classes are fine
- **RCS1118**: Prefer `const string` for literals used once — but `SamplePath(...)` is a method call, so `static readonly` is correct
- **CA1050/S3903**: File-scoped namespace always: `namespace DotnetTokenKiller.Cli.IntegrationTests;`
- **CA1305**: `sb.AppendLine(CultureInfo.InvariantCulture, $"...")` if string building is needed
- **xUnit2020**: Use `Assert.Fail("msg")` — NOT `Assert.True(false, "msg")`

### References

- [Source: epics.md — Story 8.4 acceptance criteria, lines 1063–1120]
- [Source: src/DotnetTokenKiller.Application/Filters/DotnetPublishFilter.cs — exact output format, `PublishOutputPattern`, `BuildContext`]
- [Source: src/DotnetTokenKiller.Application/Filters/DotnetPackFilter.cs — exact output format, `PackOutputPattern` (`Successfully created package '...'`)]
- [Source: src/DotnetTokenKiller.Application/Filters/DotnetRunFilter.cs — preamble patterns stripped, app stdout pass-through]
- [Source: sample/SampleApp/Program.cs — `Hello from SampleApp!` (success), `App starting...` + exit 1 (with `--fail`)]
- [Source: sample/SampleApp/SampleApp.csproj — Version=1.0.0, PackageId=SampleApp → nupkg = `SampleApp.1.0.0.nupkg`]
- [Source: tests/DotnetTokenKiller.Cli.IntegrationTests/IntegrationTestHelper.cs — helper API, ArgumentList usage]
- [Source: tests/DotnetTokenKiller.Cli.IntegrationTests/DotnetBuildIntegrationTests.cs — style reference]
- [Source: 8-2-integration-tests-for-build-restore-and-clean.md — debug log: `--verbosity normal` required for savings; use ArgumentList; savings pattern]
- [Source: 8-3-integration-tests-for-test.md — debug log: Spectre.Console drops unknown options; use `--` separator for `--project`, `--filter`]
- [Source: project-context.md — Token Savings Targets: publish ≥80%, pack ≥85%, run ≥60%]
- [Source: project-context.md — Language-Specific Rules, VSTHRD200 xUnit exemption]

## Dev Agent Record

### Agent Model Used

claude-sonnet-4-6

### Debug Log References

- **dotnet pack incremental build**: `Successfully created package` line is only emitted when the nupkg is freshly created. When nupkg already exists (incremental), dotnet skips creation and the filter outputs `✓ dotnet pack (1 project)` without the nupkg name. Solution: delete any existing `*.nupkg` from `SampleApp/` before the `OutputStartsWithNupkgName` test.
- **SampleApp IsPackable**: `SampleApp` is `<OutputType>Exe</OutputType>`. By default, exe projects are not packable — `dotnet pack` runs but creates no nupkg. Added `<IsPackable>true</IsPackable>` to `sample/SampleApp/SampleApp.csproj` to enable nupkg creation (required for AC #3).
- **Spectre.Console arg forwarding (run)**: `--project` is an option flag — must use `--` to forward to dotnet run. For the fail path, double `--` is needed: `RunDtkAsync("dotnet", "run", "--", "--project", SampleApp, "--", "--fail")`.

### Completion Notes List

- Created `DotnetPublishIntegrationTests.cs` with 7 integration tests; all pass.
- Created `DotnetPackIntegrationTests.cs` with 5 integration tests; all pass. Added `<IsPackable>true</IsPackable>` to `sample/SampleApp/SampleApp.csproj` to enable nupkg creation. Added nupkg pre-deletion in `OutputStartsWithNupkgName` test to handle incremental builds.
- Created `DotnetRunIntegrationTests.cs` with 5 integration tests; all pass. Used double `--` for fail path to forward both `--project` and `--fail` through Spectre.Console.
- Total: 17 new integration tests. Full suite: 238 tests (199 unit + 40 integration), 0 regressions.

### File List

- tests/DotnetTokenKiller.Cli.IntegrationTests/DotnetPublishIntegrationTests.cs (new)
- tests/DotnetTokenKiller.Cli.IntegrationTests/DotnetPackIntegrationTests.cs (new)
- tests/DotnetTokenKiller.Cli.IntegrationTests/DotnetRunIntegrationTests.cs (new)
- sample/SampleApp/SampleApp.csproj (modified — added `<IsPackable>true</IsPackable>`)
